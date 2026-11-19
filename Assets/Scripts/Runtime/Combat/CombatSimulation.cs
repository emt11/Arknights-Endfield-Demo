using System;
using System.Collections.Generic;
using Endfield.AnimationSystem;
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
        private readonly List<(ActionController Owner, long Id, ActionEndReason Reason)> pendingActionEnds = new List<(ActionController, long, ActionEndReason)>();
        private readonly List<HitRequest> hits = new List<HitRequest>();
        private readonly HitDetection detection = new HitDetection();
        private readonly CombatInputBuffer playerInputs = new CombatInputBuffer();

        internal int PendingInputCount => playerInputs.Count;
        public double Now { get; private set; }
        public int LastStepCount { get; private set; }
        public float DiscardedTime { get; private set; }
        public CombatRulesDefinition Rules => rules;
        public CombatFeedbackPlayer Feedback => feedback;
        public bool IsPublishing { get; private set; }
        public IReadOnlyList<Combatant> Units => units;
        public CombatResolver Resolver { get; private set; }

        private void Awake()
        {
            player.AttackRequested += QueueAttack;
            player.Disabled += playerInputs.Clear;
        }

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
                unit.Animation?.UseSimulationClock(feedback);
                if (unit.Actions != null)
                {
                    unit.Combo = new ComboController(unit);
                    unit.Actions.Initialize(unit, this);
                }
            }
        }

        private void Update() => AdvanceFrame(Time.unscaledDeltaTime, !input.GameplayCaptured);

        public void RequestHitStop(float duration) => hitStopRemaining = Mathf.Max(hitStopRemaining, duration);

        public void QueueAttack(int frame) => playerInputs.Enqueue(CombatInputKind.Attack, frame, Now, rules.inputBufferDuration);

        internal long NextExecutionId() => ++executionSequence;

        internal void Record(ActionController owner, ActionResult result) => Defer(() => owner.Publish(result));

        internal void Defer(Action notification) => results.Add(notification);

        internal void QueueActionEnd(ActionController owner, long executionId, ActionEndReason reason) => pendingActionEnds.Add((owner, executionId, reason));

        /// <summary>
        /// 推进帧
        /// </summary>
        public void AdvanceFrame(float unscaledDelta, bool paused)
        {
            LastStepCount = 0;
            DiscardedTime = 0f;
            if (paused)
            {
                playerInputs.Clear();
                return;
            }

            // 顿帧按未缩放时间计时，战斗倍率只作用于扣除顿帧后的时间
            float stopped = Mathf.Min(hitStopRemaining, unscaledDelta);
            hitStopRemaining -= stopped;
            float requested = (unscaledDelta - stopped) * rules.battleRate;

            // 单步时长和步数共同限制本帧的模拟量，避免卡顿时补算过多战斗步
            float remaining = Mathf.Min(requested, rules.maxCombatStep * rules.maxCombatStepsPerFrame);
            // 超出预算的时间仅记录，不结转到下一帧补跑
            DiscardedTime = requested - remaining;
            for (int index = 0; index < rules.maxCombatStepsPerFrame && remaining > 0f; index++)
            {
                float step = Mathf.Min(remaining, rules.maxCombatStep);
                playerInputs.Expire(Now);
                SimulateStep(step);
                remaining -= step;
                LastStepCount++;
            }
        }

        // 玩法通知和命中使用本步已提交的动画、位移，外部通知统一在步末发布
        private void SimulateStep(float step)
        {
            // 执行上一步结果回调提出的结束请求，避免在发布结果期间修改动作
            foreach ((ActionController owner, long id, ActionEndReason reason) in pendingActionEnds)
                owner.End(id, reason);
            pendingActionEnds.Clear();

            // 在推进动作前裁决攻击和移动取消，让结果从本步开始生效
            SubmitPlayerAction();

            // 先更新动画并提交位移，让本步音效、特效和命中使用更新后的姿态
            UpdateActionsAndAnimation(step);
            MoveUnits(step);
            Now += step;
            foreach (Combatant unit in units)
            {
                if (unit == null || !unit.isActiveAndEnabled || unit.Animation == null || !unit.Animation.isActiveAndEnabled)
                    continue;

                if (unit.Actions?.Current != null)
                {
                    if (unit.CanExecuteAction && unit.EvaluatedPlayback == unit.Actions.Current.Playback)
                        unit.Actions.ProcessEvents();
                }
                else
                    unit.Animation.DispatchEvents(unit.EvaluatedPlayback);
            }

            // 先结算命中，确保到达片段末尾的动作仍能判定本步的武器轨迹
            ResolveHits();

            foreach (Combatant unit in units)
            {
                if (unit != null && unit.CanExecuteAction)
                    unit.Actions.FinishAtEnd();
            }

            // 本步状态全部提交后，再通知 UI 等外部订阅者
            PublishResults();
        }

        internal void SubmitPlayerAction()
        {
            Combatant character = player.Character;
            if (!player.isActiveAndEnabled || !character.isActiveAndEnabled)
            {
                playerInputs.Clear();
                return;
            }

            Vector3 worldMove = player.WorldMove;
            if (playerInputs.TryPeek(CombatInputKind.Attack, out CombatInput attack) &&
                character.Combo.TryAttack(out long executionId, worldMove))
            {
                // 零时刻事件可能重入，先消费已获准的输入
                playerInputs.Consume(attack.sequence);
                character.Actions.ProcessInitialPoints(executionId);
                return;
            }

            ActionRuntime current = character.Actions.Current;
            if (worldMove.sqrMagnitude > 0f && current != null && character.Actions.CanCancel(ActionCategory.Move))
                character.Actions.End(current.ExecutionId, ActionEndReason.Cancelled);
        }

        private void UpdateActionsAndAnimation(float step)
        {
            foreach (Combatant unit in units)
            {
                if (unit == null || !unit.isActiveAndEnabled || unit.Animation == null || !unit.Animation.isActiveAndEnabled)
                    continue;
                unit.EvaluatedPlayback = null;
                if (unit.Motor != null && !unit.Motor.isActiveAndEnabled)
                    continue;

                AnimationPlayback playback = unit.Animation.Current;
                ActionRuntime action = unit.Actions?.Current;
                if (action == null)
                {
                    playback?.Advance(step);
                    // 零时刻回调可能停用角色或移动组件
                    if (!unit.isActiveAndEnabled || !unit.Animation.isActiveAndEnabled || (unit.Motor != null && !unit.Motor.isActiveAndEnabled))
                        continue;
                }
                else if (unit.CanExecuteAction)
                {
                    action.Advance(step);
                    if (action.hitIntervals.Count > 0)
                        unit.Weapon?.CaptureStart();
                }

                if (unit.Motor != null)
                {
                    unit.Motor.UpdateLocomotion(step, unit.Actions?.Current == null && !unit.Health.IsDead);
                    unit.UpdateLocomotionParameters();
                }

                // 替换后的播放已建立首帧，本步只采样刚推进的播放
                if (unit.Animation.Current != playback)
                    continue;

                unit.Animation.EvaluatePose(step);
                unit.EvaluatedPlayback = playback;
            }
        }

        private void MoveUnits(float step)
        {
            foreach (Combatant unit in units)
            {
                if (unit == null || !unit.CanSimulateMovement)
                    continue;
                ActionRuntime action = unit.Actions?.Current;
                Quaternion rotation = unit.transform.rotation;
                bool useWarping = action != null && action.Definition.useRootMotion && action.HasWarp;
                ActionMovement movement;
                if (action == null)
                    movement = new ActionMovement(unit.Motor.LocomotionDelta, rotation);
                else if (!action.Definition.useRootMotion)
                    movement = action.EvaluateAnimationMovement(rotation, Vector3.zero, Quaternion.identity);
                else if (useWarping)
                {
                    bool targetValid = unit.Targeting.IsEnemy(action.Target);
                    WarpTarget target = targetValid ? new WarpTarget(true, action.Target.transform.position, action.Target.BodyRadius) : default;
                    movement = MotionWarping.Evaluate(action, unit.transform.position, rotation, unit.BodyRadius, target, unit.Targeting.CheckStandingPosition, unit.Targeting.CheckLineOfSight);
                }
                else
                {
                    Vector3 localDelta = Quaternion.Inverse(rotation) * unit.Animation.RootDelta;
                    movement = action.EvaluateAnimationMovement(rotation, localDelta, unit.Animation.RootRotation);
                }

                unit.Motor.ApplyRotation(movement.rotation);
                unit.Motor.ApplyMovement(movement.delta, step);
                if (useWarping)
                    MotionWarping.ObserveCollision(action, unit.Motor.Collisions, unit.Motor.RequestedPlanarDelta, unit.Motor.ActualPlanarDelta);
                if (action != null && action.hitIntervals.Count > 0)
                    unit.Weapon?.CaptureEnd();
            }
        }

        private void ResolveHits()
        {
            Physics.SyncTransforms();
            hits.Clear();
            foreach (Combatant unit in units)
            {
                if (unit != null && unit.CanExecuteAction)
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
        }

        private void PublishResults()
        {
            IsPublishing = true;
            int count = results.Count;
            int attempted = 0;
            try
            {
                while (attempted < count)
                {
                    Action notification = results[attempted];
                    attempted++;
                    notification();
                }
            }
            finally
            {
                IsPublishing = false;
                results.RemoveRange(0, attempted);
            }
        }

        private void OnDestroy()
        {
            player.AttackRequested -= QueueAttack;
            player.Disabled -= playerInputs.Clear;
            results.Clear();
            pendingActionEnds.Clear();
            hits.Clear();
            playerInputs.Clear();
        }
    }
}
