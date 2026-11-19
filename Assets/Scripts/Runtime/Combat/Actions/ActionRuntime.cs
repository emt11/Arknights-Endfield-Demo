using System;
using System.Collections.Generic;
using Endfield.Character;
using Endfield.AnimationSystem;
using UnityEngine;

namespace Endfield.Combat
{
    public enum ActionEndReason
    {
        Completed,
        Cancelled,
        Interrupted
    }

    public readonly struct ActionResult
    {
        public readonly long executionId;
        public readonly ActionDefinition definition;
        public readonly ActionEndReason? reason;
        public bool Started => !reason.HasValue;

        public ActionResult(long executionId, ActionDefinition definition, ActionEndReason? reason = null)
        {
            this.executionId = executionId;
            this.definition = definition;
            this.reason = reason;
        }
    }

    public sealed class ActionRuntime
    {
        public AnimationPlayback Playback { get; }
        internal readonly HashSet<(int Group, int Target)> hits = new HashSet<(int, int)>();

        private float openingYawOffset;

        internal readonly List<CancelWindow> cancelWindows = new List<CancelWindow>();
        internal readonly List<(HitWindow Window, float From, float To)> hitIntervals = new List<(HitWindow, float, float)>();
        internal readonly List<(MotionWarpWindow Window, float From, float To)> warpIntervals = new List<(MotionWarpWindow, float, float)>();
        public bool HasWarp { get; }

        public ActionDefinition Definition { get; }
        public long ExecutionId { get; }
        public Combatant Target { get; }
        public float LocalTime => Playback.Time;
        public float PreviousTime => Playback.PreviousTime;

        public float PlaybackRate
        {
            get => Playback.PlaybackRate;
            set => Playback.PlaybackRate = value;
        }

        public bool ComboAdvanced { get; internal set; }
        public bool Ended => EndReason.HasValue;
        public ActionEndReason? EndReason { get; internal set; }
        internal WarpState Warp { get; } = new WarpState();

        public Quaternion InitializeOpeningFacing(Quaternion currentRotation, Vector3 attackDirection)
        {
            attackDirection.y = 0f;
            openingYawOffset = 0f;
            if (attackDirection.sqrMagnitude == 0f)
                return currentRotation;

            Quaternion attackRotation = Quaternion.LookRotation(attackDirection);
            if (Definition.openingTurnDuration <= 0f)
                return attackRotation;

            openingYawOffset = Mathf.DeltaAngle(attackRotation.eulerAngles.y, currentRotation.eulerAngles.y);
            return currentRotation;
        }

        private float RemainingYaw(float time) => Definition.openingTurnDuration > 0f
            ? Mathf.SmoothStep(openingYawOffset, 0f, time / Definition.openingTurnDuration) : 0f;

        // 根轨迹沿出刀方向求值，角色的可见朝向从起手姿态逐步追上
        internal Quaternion GetTrajectoryRotationAtStepStart(Quaternion facing) => Quaternion.Euler(0f, -RemainingYaw(PreviousTime), 0f) * facing;

        internal ActionMovement ApplyOpeningTurnAtStepEnd(Vector3 delta, Quaternion trajectoryRotation)
        {
            float yaw = RemainingYaw(LocalTime);
            float alignment = Mathf.Clamp01(Mathf.Cos(yaw * Mathf.Deg2Rad));
            return new ActionMovement(delta * alignment, Quaternion.Euler(0f, yaw, 0f) * trajectoryRotation);
        }

        public ActionMovement EvaluateAnimationMovement(Quaternion facing, Vector3 localDelta, Quaternion rootRotation)
        {
            Quaternion basis = GetTrajectoryRotationAtStepStart(facing);
            return ApplyOpeningTurnAtStepEnd(basis * localDelta, basis * rootRotation);
        }

        public void Advance(float stepDelta)
        {
            hitIntervals.Clear();
            warpIntervals.Clear();
            Playback.Advance(stepDelta);
        }

        public ActionRuntime(ActionDefinition definition, long executionId, Combatant target, Transform model = null, Endfield.Presentation.CombatFeedbackPlayer feedback = null)
        {
            Definition = definition;
            ExecutionId = executionId;
            Target = target;
            Playback = new AnimationPlayback(definition.animation, model, feedback, this);
            HasWarp = Array.Exists(definition.animation.windowEvents, entry => entry is MotionWarpWindow);
        }
    }
}
