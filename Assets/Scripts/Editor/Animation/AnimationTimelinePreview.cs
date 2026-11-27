using System;
using System.Collections.Generic;
using System.Globalization;
using Endfield.Animation;
using Endfield.MotionWarping;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using WarpSolver = Endfield.MotionWarping.MotionWarping;

namespace Endfield.Editor.Animation
{
    [Serializable]
    internal sealed class AnimationPreviewSettings
    {
        [InspectorName("预览倍率"), Min(.001f)] public float speed = 1f;
        [InspectorName("淡入时长"), Min(0f)] public float fadeInDuration;
        [InspectorName("淡出时长"), Min(0f)] public float fadeOutDuration;
        [InspectorName("淡入曲线")] public AnimationCurve fadeInCurve = AnimationCurve.Linear(0, 0, 1, 1);
        [InspectorName("淡出曲线")] public AnimationCurve fadeOutCurve = AnimationCurve.Linear(0, 1, 1, 0);

        internal bool ValidSpeed => speed > 0 && !float.IsInfinity(speed);

        internal string ConfigurationError()
        {
            if (!ValidSpeed) return "预览倍率须为有限正数";
            if (!ValidDuration(fadeInDuration) || !ValidDuration(fadeOutDuration) ||
                !ValidCurve(fadeInCurve, 0, 1) || !ValidCurve(fadeOutCurve, 1, 0))
                return "预览淡化时长须为有限非负数，曲线端点须符合淡入或淡出的方向";
            return null;
        }

        internal float EvaluateWeight(double time, double duration)
        {
            float entry = fadeInDuration > 0 && fadeInCurve != null
                ? fadeInCurve.Evaluate(Mathf.Clamp01((float)time / fadeInDuration)) : 1f;
            float exit = fadeOutDuration > 0 && fadeOutCurve != null
                ? fadeOutCurve.Evaluate(Mathf.Clamp01(1f - (float)(duration - time) / fadeOutDuration)) : 1f;
            return Mathf.Clamp01(Mathf.Min(entry, exit));
        }

        private static bool ValidDuration(float value) => value >= 0 && !float.IsInfinity(value);

        private static bool ValidCurve(AnimationCurve curve, float start, float end) =>
            curve != null && curve.length >= 2 && Mathf.Approximately(curve.Evaluate(0), start) &&
            Mathf.Approximately(curve.Evaluate(1), end);
    }

    internal sealed class AnimationTimelinePreview : IDisposable
    {
        private readonly AnimationDefination asset;
        private readonly AnimationPreviewSettings settings;
        private readonly bool dodgePreview;
        private readonly List<WarpWindow> warpWindows = new();
        private MotionWarpingAsset warpAsset;
        private readonly Transform sceneRoot;
        private readonly Pose sceneOrigin;
        private readonly PropertyModification[] sceneRootProperties;
        private AnimationModeDriver sceneDriver;
        private readonly bool previousFireEvents;
        private readonly Animator animator;
        private PlayableGraph graph;
        private AnimationClipPlayable clip;
        private AnimationMixerPlayable mixer;
        private RuntimeAnimatorController controller;
        private AnimationClip sampledClip;
        private GameObject sampleModel;
        private Avatar sampleAvatar;
        private Hash128 clipDependency, modelDependency;
        private AnimationClip referencePose;
        private Vector3[] original, corrected;
        private Pose[] clipPath;
        private Quaternion[] headings;
        private bool hasBake, useRootMotion, canWarp, poseDirty;
        private double lastTime = -1;
        private float lastWeight;

        public Pose[] Targets { get; private set; } = Array.Empty<Pose>();
        public Vector3 Position { get; private set; }
        public bool UsesController => controller;
        public bool HasPreviewTrajectory => hasBake;
        // Unity 采样期间会暂时禁用 Animator 组件
        public bool ScenePreviewActive => sceneDriver && sceneRoot && animator && animator.gameObject.activeInHierarchy &&
            AnimationMode.InAnimationMode(sceneDriver);

        public AnimationTimelinePreview(AnimationDefination asset, RuntimeAnimatorController overrideController,
            Animator sceneAnimator, Transform sceneRoot, AnimationPreviewSettings settings = null, bool dodgePreview = false)
        {
            this.asset = asset;
            this.settings = settings ?? new AnimationPreviewSettings();
            this.dodgePreview = dodgePreview;
            this.sceneRoot = sceneRoot;
            animator = sceneAnimator;
            if (!sceneRoot || !animator || !animator.isHuman)
                throw new InvalidOperationException("请选择带有 Humanoid Animator 的场景角色");
            if (AnimationMode.InAnimationMode())
                throw new InvalidOperationException("请先结束 Unity 其他窗口的动画预览");
            sceneOrigin = new Pose(sceneRoot.position, sceneRoot.rotation);
            sceneRootProperties = CaptureRootProperties(sceneRoot);
            try
            {
                previousFireEvents = animator.fireEvents;
                referencePose = new AnimationClip { hideFlags = HideFlags.HideAndDontSave };
                sceneDriver = ScriptableObject.CreateInstance<AnimationModeDriver>();
                sceneDriver.hideFlags = HideFlags.HideAndDontSave;
                AnimationMode.StartAnimationMode(sceneDriver);
                animator.fireEvents = false;
                Refresh(overrideController, checkDependencies: true);
            }
            catch { Dispose(); throw; }
        }

        private void BuildGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            graph = PlayableGraph.Create("动画时间线预览");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            if (controller) graph.Connect(AnimatorControllerPlayable.Create(graph, controller), 0, mixer, 0);
            else graph.Connect(AnimationClipPlayable.Create(graph, referencePose), 0, mixer, 0);
            clip = AnimationClipPlayable.Create(graph, asset.Clip);
            clip.SetSpeed(0);
            graph.Connect(clip, 0, mixer, 1);
            AnimationPlayableOutput.Create(graph, "预览姿态", animator).SetSourcePlayable(mixer);
            lastTime = 0;
            poseDirty = true;
        }

        internal void Refresh(RuntimeAnimatorController overrideController, bool checkDependencies)
        {
            var nextClip = asset.Clip;
            var prefab = PrefabUtility.GetCorrespondingObjectFromSource(sceneRoot.gameObject);
            var nextModel = prefab ? prefab : sceneRoot.gameObject;
            bool clipChanged = sampledClip != nextClip;
            bool modelChanged = sampleModel != nextModel || sampleAvatar != animator.avatar;
            if (checkDependencies || clipChanged || modelChanged)
            {
                var nextClipDependency = Dependency(nextClip);
                var nextModelDependency = Dependency(nextModel);
                clipChanged |= clipDependency != nextClipDependency;
                modelChanged |= modelDependency != nextModelDependency;
                clipDependency = nextClipDependency;
                modelDependency = nextModelDependency;
            }
            if (clipChanged || modelChanged) clipPath = null;
            sampledClip = nextClip;
            sampleModel = nextModel;
            sampleAvatar = animator.avatar;

            var source = animator.GetComponent<AnimationSource>();
            var nextController = overrideController ? overrideController :
                source && source.LocomotionController ? source.LocomotionController : animator.runtimeAnimatorController;
            if (clipChanged || modelChanged || controller != nextController || !graph.IsValid())
            {
                controller = nextController;
                BuildGraph();
            }

            var nextWarp = dodgePreview ? null : asset.WarpAsset;
            bool nextCanWarp = nextWarp && nextWarp.Matches(nextClip, animator) && nextWarp.ValidWindows();
            bool warpChanged = warpAsset != nextWarp;
            if (!clipChanged && !modelChanged && !warpChanged && canWarp == nextCanWarp &&
                useRootMotion == asset.UseRootMotion && !WindowsChanged(nextWarp)) return;

            if (warpChanged) Targets = Array.Empty<Pose>();
            warpAsset = nextWarp;
            canWarp = nextCanWarp;
            useRootMotion = asset.UseRootMotion;
            warpWindows.Clear();
            if (warpAsset)
                for (int i = 0; i < warpAsset.Windows.Count; i++) warpWindows.Add(warpAsset.Windows[i]);
            RebuildTrajectory();
        }

        private static Hash128 Dependency(UnityEngine.Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(path) ? default : AssetDatabase.GetAssetDependencyHash(path);
        }

        private bool WindowsChanged(MotionWarpingAsset nextWarp)
        {
            if (!nextWarp) return false;
            var windows = nextWarp.Windows;
            if (windows.Count != warpWindows.Count) return true;
            for (int i = 0; i < windows.Count; i++)
            {
                var before = warpWindows[i];
                var after = windows[i];
                if (before.startFrame != after.startFrame || before.endFrame != after.endFrame ||
                    !before.targetOffsetXZ.Equals(after.targetOffsetXZ) || before.alignYaw != after.alignYaw)
                    return true;
            }
            return false;
        }

        private static PropertyModification[] CaptureRootProperties(Transform root)
        {
            var p = root.localPosition;
            var q = root.localRotation;
            PropertyModification Property(string path, float value) => new()
            {
                target = root,
                propertyPath = path,
                value = value.ToString("R", CultureInfo.InvariantCulture)
            };
            return new[]
            {
                Property("m_LocalPosition.x", p.x), Property("m_LocalPosition.y", p.y), Property("m_LocalPosition.z", p.z),
                Property("m_LocalRotation.x", q.x), Property("m_LocalRotation.y", q.y),
                Property("m_LocalRotation.z", q.z), Property("m_LocalRotation.w", q.w)
            };
        }

        public void RebuildTrajectory()
        {
            hasBake = (useRootMotion || warpAsset) && sampledClip && sampledClip.humanMotion && !sampledClip.isLooping && animator.isHuman;
            if (hasBake && clipPath == null)
                clipPath = AnimationDefinationEditor.SampleClip(sampledClip, sampleModel);
            int count = hasBake ? clipPath.Length : 1;
            original = new Vector3[count];
            corrected = new Vector3[count];
            headings = new Quaternion[count];
            headings[0] = Quaternion.identity;
            int oldCount = Targets?.Length ?? 0;
            var targets = Targets;
            Array.Resize(ref targets, warpAsset ? warpAsset.Windows.Count : 0);
            Targets = targets;
            var snapshots = new WarpTarget[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                if (i >= oldCount)
                {
                    int end = Mathf.Clamp(warpAsset.Windows[i].endFrame, 0, count - 1);
                    targets[i] = new Pose(hasBake ? clipPath[end].position : Vector3.forward, Quaternion.identity);
                }
                snapshots[i] = new WarpTarget(targets[i]);
            }
            var warp = new WarpSolver();
            bool warping = hasBake && canWarp &&
                warp.BeginWarp(warpAsset, snapshots, AnimationDefinationEditor.SummarizeWindows(warpAsset, clipPath));
            for (int i = 1; i < count; i++)
            {
                double from = asset.FrameTime(i - 1), to = asset.FrameTime(i);
                original[i] = clipPath[i].position;
                var raw = new PlanarMotion(original[i] - original[i - 1], clipPath[i].rotation);
                bool warped = false;
                var motion = warping ? warp.UpdateWarp(from, to, new Pose(corrected[i - 1], headings[i - 1]), Quaternion.identity, raw, out warped)
                    : raw;
                bool moves = useRootMotion || warped;
                corrected[i] = moves ? corrected[i - 1] + motion.Displacement : corrected[i - 1];
                headings[i] = moves ? motion.Rotation : headings[i - 1];
            }
            warp.EndWarp();
            poseDirty = true;
        }

        public bool Seek(double time)
        {
            time = Math.Clamp(time, 0, asset.Duration);
            float w = settings.EvaluateWeight(time, asset.Duration);
            if (!poseDirty && time == lastTime && w == lastWeight) return false;
            if (time < lastTime) BuildGraph();
            clip.SetTime(time);
            mixer.SetInputWeight(0, 1 - w);
            mixer.SetInputWeight(1, w);
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            if (hasBake)
            {
                int i = asset.FrameBefore(time);
                float u = (float)((time - asset.FrameTime(i)) / (asset.FrameTime(i + 1) - asset.FrameTime(i)));
                position = Vector3.Lerp(corrected[i], corrected[i + 1], u);
                rotation = Quaternion.Slerp(headings[i], headings[i + 1], u);
            }
            AnimationMode.BeginSampling();
            try
            {
                AnimationMode.SamplePlayableGraph(graph, 0, (float)time);
                // 轨迹写入的根位姿也需要在退出时还原
                foreach (var property in sceneRootProperties)
                {
                    var binding = EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), property.propertyPath);
                    AnimationMode.AddPropertyModification(binding, property, true);
                }
                sceneRoot.SetPositionAndRotation(sceneOrigin.position + sceneOrigin.rotation * position,
                    sceneOrigin.rotation * rotation);
            }
            finally { AnimationMode.EndSampling(); }
            Position = position;
            lastTime = time;
            lastWeight = w;
            poseDirty = false;
            return true;
        }

        public bool DrawScene(int window, bool moveTarget)
        {
            if (!HasPreviewTrajectory) return false;
            Matrix4x4 space = Matrix4x4.TRS(sceneOrigin.position, sceneOrigin.rotation, Vector3.one);
            using var scope = new Handles.DrawingScope(space);
            DrawTrajectories(window);
            if (!moveTarget || window < 0 || window >= Targets.Length) return false;

            var target = Targets[window];
            EditorGUI.BeginChangeCheck();
            Vector3 position = Handles.PositionHandle(target.position, Quaternion.identity);
            if (!EditorGUI.EndChangeCheck()) return false;

            position.y = 0;
            Targets[window] = new Pose(position, target.rotation);
            return true;
        }

        private void DrawTrajectories(int window)
        {
            using (new Handles.DrawingScope(Color.cyan))
                for (int i = 1; i < original.Length; i++) Handles.DrawLine(original[i - 1], original[i]);
            if (window >= 0 && window < Targets.Length)
            {
                using (new Handles.DrawingScope(Color.yellow))
                {
                    for (int i = 1; i < corrected.Length; i++) Handles.DrawLine(corrected[i - 1], corrected[i]);
                    var target = Targets[window];
                    Handles.DrawLine(target.position, target.position + target.rotation * Vector3.forward * .4f);
                    Handles.Label(target.position, "目标");
                    int end = warpAsset.Windows[window].endFrame;
                    if (end >= 0 && end < corrected.Length)
                    {
                        Handles.DrawLine(corrected[end], corrected[end] + headings[end] * Vector3.forward * .4f);
                        Handles.Label(corrected[end], "终点");
                    }
                }
            }
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
            if (sceneDriver)
            {
                if (AnimationMode.InAnimationMode(sceneDriver)) AnimationMode.StopAnimationMode(sceneDriver);
                if (animator) animator.fireEvents = previousFireEvents;
                UnityEngine.Object.DestroyImmediate(sceneDriver);
            }
            if (referencePose) UnityEngine.Object.DestroyImmediate(referencePose);
        }
    }
}
