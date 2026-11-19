using System;
using UnityEngine;

namespace Endfield.Combat
{
    [Serializable]
    public sealed class MotionWarpWindow
    {
        public float start;
        public float end;
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

    public sealed class WarpState
    {
        internal int windowIndex = -1;
        internal Vector3 entryPosition;
        internal Quaternion entryRotation;
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
        public static ActionMovement Evaluate(ActionRuntime runtime, Vector3 position, Quaternion rotation, float radius, WarpTarget target, Func<Vector3, bool> safePosition)
        {
            ActionDefinition definition = runtime.Definition;
            Vector3 planned = position;
            Quaternion plannedRotation = rotation;
            float time = runtime.PreviousTime;
            for (int index = 0; index < definition.warpWindows.Length; index++)
            {
                MotionWarpWindow window = definition.warpWindows[index];
                if (window.end <= time || window.start >= runtime.LocalTime)
                    continue;

                if (time < window.start)
                {
                    ActionMovement source = SourceMovement(definition, time, window.start, plannedRotation);
                    planned += source.delta;
                    plannedRotation = source.rotation;
                    time = window.start;
                }

                WarpState state = runtime.Warp;
                if (state.windowIndex != index)
                    Enter(definition, window, state, index, planned, plannedRotation, radius, target, safePosition);

                state.targetValid &= target.valid;
                float end = Mathf.Min(window.end, runtime.LocalTime);
                float elapsed = (end - time) / runtime.PlaybackRate;
                if (state.targetValid && state.translate)
                {
                    Vector3 delta = Trajectory(definition, window, state, end) - Trajectory(definition, window, state, time);
                    float forward = Vector3.Dot(delta, state.direction);
                    float remaining = Mathf.Max(0f, Vector3.Dot(state.lockedPosition - planned, state.direction));
                    delta += state.direction * (Mathf.Clamp(forward, 0f, remaining) - forward);
                    delta = state.blocked ? Vector3.zero : Vector3.ClampMagnitude(delta, window.maxSpeed * elapsed);
                    planned += delta;
                }
                else
                    planned += SourceMovement(definition, time, end, plannedRotation).delta;

                if (state.targetValid && window.warpRotation)
                {
                    RootMotionSample origin = definition.rootMotion.Sample(definition.clipStart + window.start);
                    RootMotionSample sample = definition.rootMotion.Sample(definition.clipStart + end);
                    float yaw = (state.entryRotation * Quaternion.Inverse(origin.rotation) * sample.rotation).eulerAngles.y;
                    yaw += Weight(definition, window, end) * state.yawCorrection;
                    float next = Mathf.MoveTowardsAngle(plannedRotation.eulerAngles.y, yaw, window.maxYawSpeed * elapsed);
                    plannedRotation = Quaternion.Euler(0f, next, 0f);
                }
                else
                    plannedRotation = SourceMovement(definition, time, end, plannedRotation).rotation;

                time = end;
            }

            if (time < runtime.LocalTime)
            {
                ActionMovement source = SourceMovement(definition, time, runtime.LocalTime, plannedRotation);
                planned += source.delta;
                plannedRotation = source.rotation;
            }
            return new ActionMovement(planned - position, plannedRotation);
        }

        private static void Enter(ActionDefinition definition, MotionWarpWindow window, WarpState state, int index, Vector3 position, Quaternion rotation, float radius, WarpTarget target, Func<Vector3, bool> safePosition)
        {
            state.windowIndex = index;
            state.entryPosition = position;
            state.entryRotation = rotation;
            state.currentWindowEnd = window.end;
            state.targetValid = target.valid && target.eligible;
            state.blocked = false;
            state.translate = false;
            if (!state.targetValid)
                return;

            Vector3 distance = Horizontal(target.position - position);
            state.direction = distance.sqrMagnitude < .000001f ? rotation * Vector3.forward : distance.normalized;
            Vector3 sourceEnd = rotation * RelativePosition(definition, window.start, window.end);
            float required = Mathf.Max(0f, distance.magnitude - radius - target.radius - window.stopGap);
            float approach = Mathf.Min(required, window.maxApproach, sourceEnd.magnitude + window.maxExtension);
            state.lockedPosition = position + state.direction * approach;
            state.correction = state.lockedPosition - position - sourceEnd;
            state.translate = window.warpTranslation && safePosition(state.lockedPosition);
            Quaternion sourceRotation = definition.rootMotion.Sample(definition.clipStart + window.end).rotation;
            Quaternion startRotation = definition.rootMotion.Sample(definition.clipStart + window.start).rotation;
            float sourceYaw = (rotation * Quaternion.Inverse(startRotation) * sourceRotation).eulerAngles.y;
            float targetYaw = Quaternion.LookRotation(state.direction).eulerAngles.y;
            state.yawCorrection = Mathf.Clamp(Mathf.DeltaAngle(sourceYaw, targetYaw), -window.maxYawCorrection, window.maxYawCorrection);
        }

        private static Vector3 Trajectory(ActionDefinition definition, MotionWarpWindow window, WarpState state, float time) => state.entryPosition + state.entryRotation * RelativePosition(definition, window.start, time) + state.correction * Weight(definition, window, time);

        private static float Weight(ActionDefinition definition, MotionWarpWindow window, float time)
        {
            Vector3 end = RelativePosition(definition, window.start, window.end);
            float progress = end.sqrMagnitude < .000001f ? Mathf.InverseLerp(window.start, window.end, time) : Mathf.Clamp01(Vector3.Dot(RelativePosition(definition, window.start, time), end) / end.sqrMagnitude);
            return window.correction.Evaluate(progress);
        }

        private static Vector3 RelativePosition(ActionDefinition definition, float start, float time)
        {
            RootMotionSample from = definition.rootMotion.Sample(definition.clipStart + start);
            RootMotionSample to = definition.rootMotion.Sample(definition.clipStart + time);
            return Horizontal(Quaternion.Inverse(from.rotation) * (to.position - from.position));
        }

        private static ActionMovement SourceMovement(ActionDefinition definition, float fromTime, float toTime, Quaternion rotation)
        {
            RootMotionSample from = definition.rootMotion.Sample(definition.clipStart + fromTime);
            RootMotionSample to = definition.rootMotion.Sample(definition.clipStart + toTime);
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
