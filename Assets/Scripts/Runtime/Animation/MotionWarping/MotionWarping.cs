using System.Collections.Generic;
using UnityEngine;

namespace Endfield.MotionWarping
{
    public readonly struct WarpTarget
    {
        private readonly Transform tracked;
        private readonly Pose fixedPose;
        private readonly bool fixedTarget;

        public WarpTarget(Transform target) { tracked = target; fixedPose = default; fixedTarget = false; }
        public WarpTarget(Pose pose) { fixedPose = pose; tracked = null; fixedTarget = true; }

        internal bool TryRead(out Pose pose)
        {
            pose = tracked ? new Pose(tracked.position, tracked.rotation) : fixedPose;
            return tracked || fixedTarget;
        }
    }

    public readonly struct PlanarMotion
    {
        public readonly Vector3 Displacement;
        public readonly Quaternion Rotation;
        public PlanarMotion InBasis(Quaternion basis) => new(basis * Displacement, basis * Rotation);

        public PlanarMotion(Vector3 displacement, Quaternion rotation)
        {
            Displacement = displacement;
            Rotation = rotation;
        }
    }

    public sealed class MotionWarping
    {
        private MotionWarpingAsset asset;
        private WarpTarget[] targets;
        private IReadOnlyList<WarpWindowMotion> summaries;
        private int nextWindow;
        private bool entered, hasTarget;
        private Vector3 correction;
        private Quaternion windowBasis;
        private float yawCorrection, accumulatedYaw;

        public bool BeginWarp(MotionWarpingAsset animation, IReadOnlyList<WarpTarget> windowTargets) =>
            BeginWarp(animation, windowTargets, animation && animation.HasCurrentSummary ? animation.Summaries : null);

        public bool BeginWarp(MotionWarpingAsset animation, IReadOnlyList<WarpTarget> windowTargets, IReadOnlyList<WarpWindowMotion> windowMotion)
        {
            EndWarp();
            if (!animation || !animation.ValidWindows() || windowMotion == null || windowMotion.Count != animation.Windows.Count ||
                windowTargets == null || windowTargets.Count != animation.Windows.Count) return false;
            asset = animation;
            summaries = windowMotion;
            targets = new WarpTarget[windowTargets.Count];
            for (int i = 0; i < targets.Length; i++) targets[i] = windowTargets[i];
            return true;
        }

        public PlanarMotion UpdateWarp(double from, double to, Pose actorPose, Quaternion basisYaw, PlanarMotion clipMotion) =>
            UpdateWarp(from, to, actorPose, basisYaw, clipMotion, out _);

        // 调用者按采样点分段并将每段送入 Motor，下一窗口由碰撞后的实际位置起算
        public PlanarMotion UpdateWarp(double from, double to, Pose actorPose, Quaternion basisYaw, PlanarMotion clipMotion, out bool warped)
        {
            warped = false;
            Quaternion basis = Quaternion.AngleAxis(accumulatedYaw, Vector3.up) * basisYaw;
            if (entered) basis = windowBasis;
            var move = clipMotion.InBasis(basis);
            if (nextWindow >= targets.Length) return move;
            var window = asset.Windows[nextWindow];
            double start = asset.FrameTime(window.startFrame);
            double end = asset.FrameTime(window.endFrame);
            if (from < start || to <= start) return move;
            if (!entered)
            {
                entered = true;
                windowBasis = basis;
                correction = Vector3.zero;
                yawCorrection = 0;
                hasTarget = targets[nextWindow].TryRead(out var target);
                if (hasTarget)
                {
                    Quaternion targetYaw = Quaternion.Euler(0, target.rotation.eulerAngles.y, 0);
                    Vector3 goal = target.position + targetYaw * new Vector3(window.targetOffsetXZ.x, 0, window.targetOffsetXZ.y);
                    var summary = summaries[nextWindow];
                    var natural = new PlanarMotion(summary.displacement, Quaternion.Euler(0, summary.endYaw, 0)).InBasis(basis);
                    correction = goal - actorPose.position - natural.Displacement;
                    correction.y = 0;
                    if (window.alignYaw)
                        yawCorrection = Mathf.DeltaAngle(natural.Rotation.eulerAngles.y, targetYaw.eulerAngles.y);
                }
            }
            warped = hasTarget;
            Vector3 delta = move.Displacement + correction * (float)((to - from) / (end - start));
            Quaternion rotation = Quaternion.AngleAxis(yawCorrection * (float)((to - start) / (end - start)), Vector3.up) * move.Rotation;
            if (to >= end)
            {
                accumulatedYaw += yawCorrection;
                nextWindow++;
                entered = false;
            }
            return new PlanarMotion(delta, rotation);
        }

        public void EndWarp()
        {
            asset = null;
            targets = null;
            summaries = null;
            nextWindow = 0;
            entered = false;
            accumulatedYaw = 0;
        }
    }
}
