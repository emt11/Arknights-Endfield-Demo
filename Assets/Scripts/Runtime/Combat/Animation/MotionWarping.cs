using System;
using Endfield.AnimationSystem;
using UnityEngine;

namespace Endfield.Combat
{
    [Serializable]
    public sealed class MotionWarpWindow : TimelineWindowEvent
    {
        public override bool BeforeMovement => true;

        public override void OnInterval(AnimationPlayback playback, float from, float to)
        {
            if (playback.Receiver is ActionRuntime action)
                action.warpIntervals.Add((this, from, to));
        }

        public bool warpTranslation = true;
        public bool warpRotation = true;
        [Min(0f)] public float stopGap = .15f;
        [Min(0f)] public float maxApproach = 2f;
        [Min(0f)] public float maxExtension = 1f;
        [Range(0f, 180f)] public float maxYawCorrection = 60f;
        [Min(0f)] public float maxYawSpeed = 720f;
        [Min(0f)] public float maxSpeed = 8f;
        public AnimationCurve correction = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    internal sealed class WarpState
    {
        internal MotionWarpWindow window;
        internal RootMotionSample origin;
        internal RootMotionSample finish;
        internal Quaternion inverseOrigin;
        internal Quaternion basis;
        internal Vector3 sourceDisplacement;
        internal Vector3 lockedPosition;
        internal Vector3 direction;
        internal Vector3 correction;
        internal float yawCorrection;
        internal bool targetValid;
        internal bool translate;
        internal bool blocked;
        internal float currentWindowEnd;
    }

    public readonly struct WarpTarget
    {
        public readonly bool valid;
        public readonly Vector3 position;
        public readonly float radius;
        public readonly bool eligible;

        public WarpTarget(bool valid, Vector3 position, float radius, bool eligible = true)
        {
            this.valid = valid;
            this.position = position;
            this.radius = radius;
            this.eligible = eligible;
        }
    }

    public readonly struct ActionMovement
    {
        public readonly Vector3 delta;
        public readonly Quaternion rotation;

        public ActionMovement(Vector3 delta, Quaternion rotation)
        {
            this.delta = delta;
            this.rotation = rotation;
        }
    }

    // 只求值当前动作的位移区间，碰撞和最终位置仍由 Motor 决定
    public static class MotionWarping
    {
        public static ActionMovement Evaluate(ActionRuntime runtime, Vector3 position, Quaternion rotation, float radius, WarpTarget target, Func<Vector3, bool> safePosition, Func<Vector3, Vector3, bool> hasLineOfSight = null)
        {
            ActionDefinition definition = runtime.Definition;
            Vector3 planned = position;
            Quaternion plannedRotation = runtime.GetTrajectoryRotationAtStepStart(rotation);
            float time = runtime.PreviousTime;
            foreach (var interval in runtime.warpIntervals)
            {
                MotionWarpWindow window = interval.Window;
                if (interval.To <= time)
                    continue;
                if (time < interval.From)
                {
                    ActionMovement source = SourceMovement(definition, time, interval.From, plannedRotation);
                    planned += source.delta;
                    plannedRotation = source.rotation;
                    time = interval.From;
                }

                WarpState state = runtime.Warp;
                if (state.window != window)
                {
                    bool eligible = target.valid && target.eligible && TargetingController.IsWithinAssistance(target.position - planned)
                        && (hasLineOfSight == null || hasLineOfSight(planned, target.position));
                    Enter(definition, window, state, planned, plannedRotation, radius, target, eligible, safePosition);
                }

                state.targetValid &= target.valid;
                float end = interval.To;
                float elapsed = (end - time) / runtime.PlaybackRate;
                RootMotionSample from = time == window.start ? state.origin : definition.rootMotion.Sample(time);
                RootMotionSample to = end == window.end ? state.finish : definition.rootMotion.Sample(end);
                ActionMovement sourceStep = SourceMovement(from, to, plannedRotation);
                float endWeight = state.targetValid ? Weight(window, state, to, end) : 0f;
                if (state.targetValid && state.translate)
                {
                    Vector3 sourceDelta = Horizontal(state.inverseOrigin * (to.position - from.position));
                    Vector3 delta = state.basis * sourceDelta + state.correction * (endWeight - Weight(window, state, from, time));
                    float forward = Vector3.Dot(delta, state.direction);
                    float remaining = Mathf.Max(0f, Vector3.Dot(state.lockedPosition - planned, state.direction));
                    delta += state.direction * (Mathf.Clamp(forward, 0f, remaining) - forward);
                    delta = state.blocked ? Vector3.zero : Vector3.ClampMagnitude(delta, window.maxSpeed * elapsed);
                    planned += delta;
                }
                else
                    planned += sourceStep.delta;

                if (state.targetValid && window.warpRotation)
                {
                    float yaw = (state.basis * state.inverseOrigin * to.rotation).eulerAngles.y;
                    yaw += endWeight * state.yawCorrection;
                    float next = Mathf.MoveTowardsAngle(plannedRotation.eulerAngles.y, yaw, window.maxYawSpeed * elapsed);
                    plannedRotation = Quaternion.Euler(0f, next, 0f);
                }
                else
                    plannedRotation = sourceStep.rotation;

                time = end;
            }

            if (time < runtime.LocalTime)
            {
                ActionMovement source = SourceMovement(definition, time, runtime.LocalTime, plannedRotation);
                planned += source.delta;
                plannedRotation = source.rotation;
            }
            return runtime.ApplyOpeningTurnAtStepEnd(planned - position, plannedRotation);
        }

        private static void Enter(ActionDefinition definition, MotionWarpWindow window, WarpState state, Vector3 position, Quaternion rotation, float radius, WarpTarget target, bool eligible, Func<Vector3, bool> safePosition)
        {
            state.window = window;
            state.origin = definition.rootMotion.Sample(window.start);
            state.finish = definition.rootMotion.Sample(window.end);
            state.inverseOrigin = Quaternion.Inverse(state.origin.rotation);
            state.basis = rotation;
            state.sourceDisplacement = Horizontal(state.inverseOrigin * (state.finish.position - state.origin.position));
            state.currentWindowEnd = window.end;
            state.targetValid = eligible;
            state.blocked = false;
            state.translate = false;
            if (!state.targetValid)
                return;

            Vector3 distance = Horizontal(target.position - position);
            state.direction = distance.sqrMagnitude < .000001f ? rotation * Vector3.forward : distance.normalized;
            Vector3 sourceEnd = rotation * state.sourceDisplacement;
            float required = Mathf.Max(0f, distance.magnitude - radius - target.radius - window.stopGap);
            float approach = Mathf.Min(required, window.maxApproach, sourceEnd.magnitude + window.maxExtension);
            state.lockedPosition = position + state.direction * approach;
            state.correction = state.lockedPosition - position - sourceEnd;
            state.translate = window.warpTranslation && safePosition(state.lockedPosition);
            float sourceYaw = (rotation * state.inverseOrigin * state.finish.rotation).eulerAngles.y;
            float targetYaw = Quaternion.LookRotation(state.direction).eulerAngles.y;
            state.yawCorrection = Mathf.Clamp(Mathf.DeltaAngle(sourceYaw, targetYaw), -window.maxYawCorrection, window.maxYawCorrection);
        }

        private static float Weight(MotionWarpWindow window, WarpState state, RootMotionSample sample, float time)
        {
            Vector3 offset = Horizontal(state.inverseOrigin * (sample.position - state.origin.position));
            float lengthSquared = state.sourceDisplacement.sqrMagnitude;
            float progress = lengthSquared < .000001f ? Mathf.InverseLerp(window.start, window.end, time) :
                Mathf.Clamp01(Vector3.Dot(offset, state.sourceDisplacement) / lengthSquared);
            return window.correction.Evaluate(progress);
        }

        private static ActionMovement SourceMovement(ActionDefinition definition, float from, float to, Quaternion rotation) =>
            SourceMovement(definition.rootMotion.Sample(from), definition.rootMotion.Sample(to), rotation);

        private static ActionMovement SourceMovement(RootMotionSample from, RootMotionSample to, Quaternion rotation)
        {
            Vector3 delta = rotation * Horizontal(Quaternion.Inverse(from.rotation) * (to.position - from.position));
            Quaternion nextRotation = rotation * (Quaternion.Inverse(from.rotation) * to.rotation);
            return new ActionMovement(delta, nextRotation);
        }

        private static Vector3 Horizontal(Vector3 value) => new Vector3(value.x, 0f, value.z);

        public static void ObserveCollision(ActionRuntime runtime, CollisionFlags flags, Vector3 requested, Vector3 actual)
        {
            WarpState state = runtime.Warp;
            bool translating = state.translate && runtime.PreviousTime < state.currentWindowEnd;
            bool hitSide = (flags & CollisionFlags.Sides) != 0;
            bool movementBlocked = requested.sqrMagnitude > .000001f &&
                Vector3.Dot(actual, requested.normalized) < requested.magnitude * .5f;
            if (translating && hitSide && movementBlocked)
                state.blocked = true;
        }
    }
}
