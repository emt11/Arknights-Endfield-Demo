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
            if (rules == null || input == null || player == null || feedback == null || units == null || units.Length == 0)
                throw new InvalidOperationException("CombatSimulation requires rules, input, player, feedback and units");
            if (rules.MaxCombatStep <= 0f || rules.MaxCombatStepsPerFrame < 1)
                throw new InvalidOperationException("Combat time budget must be positive");

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

        public void QueueAttack(int frame) => PlayerInputs.Enqueue(CombatInputKind.Attack, frame, Now, rules.InputBufferDuration);

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

            float stopped = Mathf.Min(hitStopRemaining, unscaledDelta); // 获取本帧中需要用来顿帧的时间
            hitStopRemaining -= stopped;
            float requested = (unscaledDelta - stopped) * rules.BattleRate; // 将本帧中剩余的时间转为战斗时间
            float remaining = Mathf.Min(requested, rules.MaxCombatStep * rules.MaxCombatStepsPerFrame); // 实际剩余战斗时间
            DiscardedTime = requested - remaining; // 记录本帧剩余时间中尚未执行的时间（受限于时间步）
            for (int index = 0; index < rules.MaxCombatStepsPerFrame && remaining > 0f; index++)
            {
                float step = Mathf.Min(remaining, rules.MaxCombatStep);
                PlayerInputs.Expire(Now);
                SimulateStep(step);
                remaining -= step;
                LastStepCount++;
            }
        }

        private void SimulateStep(float step)
        {
            // 取消上一步的结果通知回调
            foreach ((ActionController owner, long id, ActionEndReason reason) in cancellations)
                owner.Cancel(id, reason);
            cancellations.Clear();

            player.SubmitAction();
            AdvanceActions(step);
            MoveUnits(step);
            Now += step;
            foreach (Combatant unit in units)
            {
                if (unit != null && unit.isActiveAndEnabled && unit.Actions != null)
                    unit.Actions.ProcessEvents();
            }

            Physics.SyncTransforms();
            hits.Clear();
            foreach (Combatant unit in units)
            {
                if (unit != null && unit.isActiveAndEnabled && !unit.Health.IsDead && unit.Actions != null)
                    detection.Collect(unit, rules, hits);
            }
            hits.Sort((left, right) =>
            {
                int order = left.Source.UnitId.CompareTo(right.Source.UnitId);
                if (order == 0)
                    order = left.Runtime.ExecutionId.CompareTo(right.Runtime.ExecutionId);
                if (order == 0)
                    order = left.Window.Group.CompareTo(right.Window.Group);
                return order != 0 ? order : left.Target.UnitId.CompareTo(right.Target.UnitId);
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
                unit.StepStartPosition = unit.transform.position;
                unit.StepStartRotation = unit.transform.rotation;
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
                Vector3 delta = action == null ? unit.Motor.PlannedDelta : action.Definition.UseRootMotion ? unit.Animation.RootDelta : Vector3.zero;
                Quaternion rotation = unit.transform.rotation;
                if (action != null && action.Definition.WarpWindows.Length > 0)
                {
                    bool valid = unit.Targeting.IsEnemy(action.Target);
                    WarpTarget target = valid ? new WarpTarget(true, action.Target.transform.position, action.Target.BodyRadius, unit.Targeting.CanAssist(action.Target)) : default;
                    ActionMovement movement = MotionWarping.Evaluate(action, unit.transform.position, rotation, unit.BodyRadius, target, unit.Targeting.IsSafeStandingPosition);
                    delta = movement.Delta;
                    rotation = movement.Rotation;
                }
                else if (action != null && action.Definition.UseRootMotion)
                    rotation *= unit.Animation.RootRotation;

                unit.Motor.ApplyRotation(rotation);
                unit.Motor.ApplyMovement(delta, step);
                if (action != null && action.Definition.WarpWindows.Length > 0)
                    MotionWarping.ObserveCollision(action, unit.Motor.Collisions, unit.Motor.RequestedPlanarDelta, unit.Motor.ActualPlanarDelta);
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
