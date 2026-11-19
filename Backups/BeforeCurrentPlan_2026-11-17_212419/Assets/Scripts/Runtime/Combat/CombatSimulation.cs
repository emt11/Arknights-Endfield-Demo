using System;
using System.Collections.Generic;
using Endfield.Character;
using Endfield.Controller;
using Endfield.Presentation;
using UnityEngine;

namespace Endfield.Combat
{
    [DefaultExecutionOrder(0), DisallowMultipleComponent]
    public sealed class CombatSimulation : MonoBehaviour
    {
        [SerializeField] private CombatRulesDefinition rules;
        [SerializeField] private GameInputManager input;
        [SerializeField] private PlayerController player;
        [SerializeField] private Combatant[] units;
        [SerializeField] private CombatFeedbackPlayer feedback;
        private float hitStopRemaining;
        private long executionSequence;
        private readonly List<Action> results = new List<Action>();
        private readonly List<(ActionController Owner, long Id, ActionEndReason Reason)> cancellations = new List<(ActionController, long, ActionEndReason)>();
        private readonly List<HitRequest> hits = new List<HitRequest>();
        private readonly HitDetection detection = new HitDetection();

        public CombatInputBuffer PlayerInputs { get; } = new CombatInputBuffer();
        public double Now { get; private set; }
        public int LastStepCount { get; private set; }
        public float DiscardedTime { get; private set; }
        public CombatRulesDefinition Rules => rules;
        public CombatFeedbackPlayer Feedback => feedback;
        public bool IsPublishing { get; private set; }
        public IReadOnlyList<Combatant> Units => units;
        public CombatResolver Resolver { get; private set; }

        private void Start()
        {
            Resolver = new CombatResolver(this);
            for (int index = 0; index < units.Length; index++)
            {
                Combatant unit = units[index];
                unit.UnitId = index + 1;
                unit.Health.SetNotificationQueue(Defer);
                unit.Stats.SetNotificationQueue(Defer);
                unit.Targeting = new TargetingController(unit, this);
                if (unit.Actions != null)
                {
                    unit.Combo = new ComboController(unit);
                    unit.Actions.Initialize(unit, this);
                }
            }
        }

        private void Update() => AdvanceFrame(Time.unscaledDeltaTime, !input.GameplayCaptured);

        public void RequestHitStop(float duration) => hitStopRemaining = Mathf.Max(hitStopRemaining, duration);

        public void QueueAttack(int frame) => PlayerInputs.Enqueue(CombatInputKind.Attack, frame, Now, rules.inputBufferDuration);

        internal long NextExecutionId() => ++executionSequence;

        internal void Record(ActionController owner, ActionResult result) => Defer(() => owner.Publish(result));

        internal void Defer(Action notification) => results.Add(notification);

        internal void QueueCancellation(ActionController owner, long executionId, ActionEndReason reason) => cancellations.Add((owner, executionId, reason));

        public void AdvanceFrame(float unscaledDelta, bool paused)
        {
            LastStepCount = 0;
            DiscardedTime = 0f;
            if (paused)
            {
                PlayerInputs.Clear();
                return;
            }

            float stopped = Mathf.Min(hitStopRemaining, unscaledDelta);
            hitStopRemaining -= stopped; // 更新顿帧时间
            float requested = (unscaledDelta - stopped) * rules.battleRate;
            float remaining = Mathf.Min(requested, rules.maxCombatStep * rules.maxCombatStepsPerFrame); // 剩余的战斗时间
            // 超出本帧预算的时间直接丢弃，不带到下一帧追赶
            DiscardedTime = requested - remaining;
            for (int index = 0; index < rules.maxCombatStepsPerFrame && remaining > 0f; index++)
            {
                float step = Mathf.Min(remaining, rules.maxCombatStep);
                PlayerInputs.Expire(Now);
                SimulateStep(step);
                remaining -= step;
                LastStepCount++;
            }
        }

        /// <summary>
        /// 这个函数非常关键！
        /// </summary>
        /// <param name="step">模拟时间步</param>
        private void SimulateStep(float step)
        {
            // 执行上一步结果回调提出的取消请求，避免在发布结果期间修改动作
            foreach ((ActionController owner, long id, ActionEndReason reason) in cancellations)
                owner.Cancel(id, reason);
            cancellations.Clear();

            player.SubmitAction(); // 尝试攻击
            AdvanceActions(step); // 推进动作时间
            MoveUnits(step); // 移动 Units
            Now += step;
            foreach (Combatant unit in units)
            {
                if (unit != null && unit.isActiveAndEnabled && unit.Actions != null)
                    unit.Actions.ProcessEvents(); // 处理事件
            }

            Physics.SyncTransforms();
            // Hit
            hits.Clear();
            foreach (Combatant unit in units)
            {
                if (unit != null && unit.isActiveAndEnabled && !unit.Health.IsDead && unit.Actions != null)
                    detection.Collect(unit, rules, hits);
            }
            hits.Sort((left, right) =>
            {
                int order = left.source.UnitId.CompareTo(right.source.UnitId);
                if (order == 0)
                    order = left.runtime.ExecutionId.CompareTo(right.runtime.ExecutionId);
                if (order == 0)
                    order = left.window.group.CompareTo(right.window.group);
                return order != 0 ? order : left.target.UnitId.CompareTo(right.target.UnitId);
            });
            foreach (HitRequest hit in hits)
                Resolver.Resolve(hit);

            foreach (Combatant unit in units)
            {
                if (unit != null && unit.isActiveAndEnabled && unit.Actions != null)
                    unit.Actions.FinishAtEnd();
            }
            PublishResults();
        }

        private void AdvanceActions(float step)
        {
            foreach (Combatant unit in units)
            {
                if (unit == null || !unit.isActiveAndEnabled || unit.Motor == null)
                    continue;
                // 起手已建立初始混合姿态，每步重新采样，瞬移或换动作不会继承旧轨迹
                unit.Weapon?.CaptureStart();
                unit.Actions.Advance(step);
                unit.Motor.PlanLocomotion(step, unit.Actions.Current == null && !unit.Health.IsDead);
                unit.Animation.Evaluate(step, unit.Motor, unit.Actions.Current);
            }
        }

        private void MoveUnits(float step)
        {
            foreach (Combatant unit in units)
            {
                if (unit == null || !unit.isActiveAndEnabled || unit.Motor == null)
                    continue;
                ActionRuntime action = unit.Actions.Current;
                Vector3 delta = unit.Motor.PlannedDelta;
                // 如果没有动作则使用输入移动
                // 如果有动作并开启跟运动，使用跟运动；否则 zero
                if (action != null)
                    delta = action.Definition.useRootMotion ? unit.Animation.RootDelta : Vector3.zero;
                Quaternion rotation = unit.transform.rotation;
                if (action != null && action.Definition.warpWindows.Length > 0)
                {
                    bool targetValid = unit.Targeting.IsEnemy(action.Target);
                    WarpTarget target = targetValid ? new WarpTarget(true, action.Target.transform.position, action.Target.BodyRadius, unit.Targeting.CanAssist(action.Target)) : default;
                    ActionMovement movement = MotionWarping.Evaluate(action, unit.transform.position, rotation, unit.BodyRadius, target, unit.Targeting.IsSafeStandingPosition);
                    delta = movement.delta;
                    rotation = movement.rotation;
                }
                else if (action != null && action.Definition.useRootMotion)
                    rotation *= unit.Animation.RootRotation;

                unit.Motor.ApplyRotation(rotation);
                unit.Motor.ApplyMovement(delta, step);
                if (action != null && action.Definition.warpWindows.Length > 0)
                    MotionWarping.ObserveCollision(action, unit.Motor.Collisions, unit.Motor.RequestedPlanarDelta, unit.Motor.ActualPlanarDelta);
                unit.Weapon?.CaptureEnd();
            }
        }

        private void PublishResults()
        {
            IsPublishing = true;
            int count = results.Count;
            for (int index = 0; index < count; index++)
                results[index]();
            results.RemoveRange(0, count);
            IsPublishing = false;
        }

        private void OnDestroy()
        {
            results.Clear();
            cancellations.Clear();
            hits.Clear();
            PlayerInputs.Clear();
        }
    }
}
