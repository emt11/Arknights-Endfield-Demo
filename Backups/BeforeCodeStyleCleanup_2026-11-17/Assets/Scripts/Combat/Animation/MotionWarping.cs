using System;
using UnityEngine;

namespace Endfield.Combat
{
    [Serializable]
    public sealed class MotionWarpWindow
    {
        public float Start;
        public float End;
        public bool WarpTranslation = true;
        public bool WarpRotation = true;
        [Min(0f)] public float StopGap = .15f;
        [Min(0f)] public float MaxApproach = 2f;
        [Min(0f)] public float MaxExtension = 1f;
        [Range(0f, 180f)] public float MaxYawCorrection = 60f;
        [Min(0f)] public float MaxYawSpeed = 720f;
        [Min(0f)] public float MaxSpeed = 8f;
        public AnimationCurve Correction = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    public sealed class WarpState
    {
        public int WindowIndex = -1;
        public Vector3 EntryPosition;
        public Quaternion EntryRotation;
        public Vector3 LockedPosition;
        public Vector3 Direction;
        public Vector3 Correction;
        public float YawCorrection;
        public bool TargetValid;
        public bool Translate;
        public bool Blocked;
        public float CurrentWindowEnd;
    }

    public readonly struct WarpTarget
    {
        public readonly bool Valid;
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly bool Eligible;

        public WarpTarget(bool valid, Vector3 position, float radius, bool eligible = true)
        {
            Valid = valid;
            Position = position;
            Radius = radius;
            Eligible = eligible;
        }
    }

    public readonly struct ActionMovement
    {
        public readonly Vector3 Delta;
        public readonly Quaternion Rotation;

        public ActionMovement(Vector3 delta, Quaternion rotation)
        {
            Delta = delta;
            Rotation = rotation;
        }
    }

    // 只求值当前动作的位移区间，碰撞和最终位置仍由 Motor 决定
    public static class MotionWarping
    {
        public static RootMotionSample[] Preview(ActionDefinition definition, WarpTarget target)
        {
            var runtime = new ActionRuntime(definition, 0, null);
            runtime.PlaybackRate = 1f;
            int count = Mathf.CeilToInt(definition.Duration * 60f) + 1;
            var samples = new RootMotionSample[count];
            samples[0] = new RootMotionSample { Rotation = Quaternion.identity };
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            for (int index = 1; index < count; index++)
            {
                runtime.PreviousTime = runtime.LocalTime;
                runtime.LocalTime = Mathf.Min(index / 60f, definition.Duration);
                ActionMovement movement = Evaluate(runtime, position, rotation, .4f, target, _ => true);
                position += movement.Delta;
                rotation = movement.Rotation;
                samples[index] = new RootMotionSample { Time = runtime.LocalTime, Position = position, Rotation = rotation };
            }

            return samples;
        }

        public static ActionMovement Evaluate(ActionRuntime runtime, Vector3 position, Quaternion rotation, float radius, WarpTarget target, Func<Vector3, bool> safePosition)
        {
            ActionDefinition definition = runtime.Definition;
            Vector3 planned = position;
            Quaternion plannedRotation = rotation;
            float time = runtime.PreviousTime;
            for (int index = 0; index < definition.WarpWindows.Length; index++)
            {
                MotionWarpWindow window = definition.WarpWindows[index];
                if (window.End <= time || window.Start >= runtime.LocalTime)
                    continue;

                if (time < window.Start)
                {
                    ApplySource(definition, time, window.Start, ref planned, ref plannedRotation);
                    time = window.Start;
                }

                WarpState state = runtime.Warp;
                if (state.WindowIndex != index)
                    Enter(definition, window, state, index, planned, plannedRotation, radius, target, safePosition);

                state.TargetValid &= target.Valid;
                float end = Mathf.Min(window.End, runtime.LocalTime);
                float elapsed = (end - time) / runtime.PlaybackRate;
                if (state.TargetValid && state.Translate)
                {
                    Vector3 delta = Trajectory(definition, window, state, end) - Trajectory(definition, window, state, time);
                    float forward = Vector3.Dot(delta, state.Direction);
                    float remaining = Mathf.Max(0f, Vector3.Dot(state.LockedPosition - planned, state.Direction));
                    delta += state.Direction * (Mathf.Clamp(forward, 0f, remaining) - forward);
                    delta = state.Blocked ? Vector3.zero : Vector3.ClampMagnitude(delta, window.MaxSpeed * elapsed);
                    planned += delta;
                }
                else
                {
                    Quaternion unused = plannedRotation;
                    ApplySource(definition, time, end, ref planned, ref unused);
                }

                if (state.TargetValid && window.WarpRotation)
                {
                    RootMotionSample origin = definition.RootMotion.Sample(definition.ClipStart + window.Start);
                    RootMotionSample sample = definition.RootMotion.Sample(definition.ClipStart + end);
                    float yaw = (state.EntryRotation * Quaternion.Inverse(origin.Rotation) * sample.Rotation).eulerAngles.y;
                    yaw += Weight(definition, window, end) * state.YawCorrection;
                    float next = Mathf.MoveTowardsAngle(plannedRotation.eulerAngles.y, yaw, window.MaxYawSpeed * elapsed);
                    plannedRotation = Quaternion.Euler(0f, next, 0f);
                }
                else
                {
                    Vector3 unused = Vector3.zero;
                    ApplySource(definition, time, end, ref unused, ref plannedRotation);
                }

                time = end;
            }

            if (time < runtime.LocalTime)
                ApplySource(definition, time, runtime.LocalTime, ref planned, ref plannedRotation);
            return new ActionMovement(planned - position, plannedRotation);
        }

        private static void Enter(ActionDefinition definition, MotionWarpWindow window, WarpState state, int index, Vector3 position, Quaternion rotation, float radius, WarpTarget target, Func<Vector3, bool> safePosition)
        {
            state.WindowIndex = index;
            state.EntryPosition = position;
            state.EntryRotation = rotation;
            state.CurrentWindowEnd = window.End;
            state.TargetValid = target.Valid && target.Eligible;
            state.Blocked = false;
            state.Translate = false;
            if (!state.TargetValid)
                return;

            Vector3 distance = Horizontal(target.Position - position);
            state.Direction = distance.sqrMagnitude < .000001f ? rotation * Vector3.forward : distance.normalized;
            Vector3 sourceEnd = rotation * RelativePosition(definition, window.Start, window.End);
            float required = Mathf.Max(0f, distance.magnitude - radius - target.Radius - window.StopGap);
            float approach = Mathf.Min(required, window.MaxApproach, sourceEnd.magnitude + window.MaxExtension);
            state.LockedPosition = position + state.Direction * approach;
            state.Correction = state.LockedPosition - position - sourceEnd;
            state.Translate = window.WarpTranslation && safePosition(state.LockedPosition);
            Quaternion sourceRotation = definition.RootMotion.Sample(definition.ClipStart + window.End).Rotation;
            Quaternion startRotation = definition.RootMotion.Sample(definition.ClipStart + window.Start).Rotation;
            float sourceYaw = (rotation * Quaternion.Inverse(startRotation) * sourceRotation).eulerAngles.y;
            float targetYaw = Quaternion.LookRotation(state.Direction).eulerAngles.y;
            state.YawCorrection = Mathf.Clamp(Mathf.DeltaAngle(sourceYaw, targetYaw), -window.MaxYawCorrection, window.MaxYawCorrection);
        }

        private static Vector3 Trajectory(ActionDefinition definition, MotionWarpWindow window, WarpState state, float time) => state.EntryPosition + state.EntryRotation * RelativePosition(definition, window.Start, time) + state.Correction * Weight(definition, window, time);

        private static float Weight(ActionDefinition definition, MotionWarpWindow window, float time)
        {
            Vector3 end = RelativePosition(definition, window.Start, window.End);
            float progress = end.sqrMagnitude < .000001f ? Mathf.InverseLerp(window.Start, window.End, time) : Mathf.Clamp01(Vector3.Dot(RelativePosition(definition, window.Start, time), end) / end.sqrMagnitude);
            return window.Correction.Evaluate(progress);
        }

        private static Vector3 RelativePosition(ActionDefinition definition, float start, float time)
        {
            RootMotionSample from = definition.RootMotion.Sample(definition.ClipStart + start);
            RootMotionSample to = definition.RootMotion.Sample(definition.ClipStart + time);
            return Horizontal(Quaternion.Inverse(from.Rotation) * (to.Position - from.Position));
        }

        private static void ApplySource(ActionDefinition definition, float fromTime, float toTime, ref Vector3 position, ref Quaternion rotation)
        {
            RootMotionSample from = definition.RootMotion.Sample(definition.ClipStart + fromTime);
            RootMotionSample to = definition.RootMotion.Sample(definition.ClipStart + toTime);
            position += rotation * Horizontal(Quaternion.Inverse(from.Rotation) * (to.Position - from.Position));
            rotation *= Quaternion.Inverse(from.Rotation) * to.Rotation;
        }

        private static Vector3 Horizontal(Vector3 value) => new Vector3(value.x, 0f, value.z);

        public static void ObserveCollision(ActionRuntime runtime, CollisionFlags flags, Vector3 requested, Vector3 actual)
        {
            WarpState state = runtime.Warp;
            if (state.Translate && runtime.PreviousTime < state.CurrentWindowEnd && (flags & CollisionFlags.Sides) != 0 && requested.sqrMagnitude > .000001f && Vector3.Dot(actual, requested.normalized) < requested.magnitude * .5f)
                state.Blocked = true;
        }
    }
}
