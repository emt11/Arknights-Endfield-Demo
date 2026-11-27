using System;
using System.Collections.Generic;
using Endfield.MotionWarping;
using UnityEngine;

namespace Endfield.Animation
{
    [CreateAssetMenu(menuName = "Endfield/Animation/Animation Defination")]
    public sealed class AnimationDefination : ScriptableObject
    {
        [SerializeField] private AnimationClip clip;
        [SerializeField, InspectorName("使用根运动")] private bool useRootMotion;
        [SerializeField, InspectorName("使用运动扭曲")] private bool useMotionWarping;
        [SerializeField, InspectorName("扭曲资产")] private MotionWarpingAsset warpAsset;
        [SerializeReference] private AnimationPointNotify[] pointNotifies = Array.Empty<AnimationPointNotify>();
        [SerializeReference] private AnimationWindowNotify[] windowNotifies = Array.Empty<AnimationWindowNotify>();

        public AnimationClip Clip => clip;
        public bool UseRootMotion => useRootMotion;
        public bool UseMotionWarping => useMotionWarping;
        public MotionWarpingAsset WarpAsset => useMotionWarping ? warpAsset : null;
        public double Duration => clip ? clip.length : 0;
        public int LastFrame => clip ? Mathf.CeilToInt(clip.length * clip.frameRate - .00001f) : 0;
        public IReadOnlyList<AnimationPointNotify> PointNotifies => pointNotifies;
        public IReadOnlyList<AnimationWindowNotify> WindowNotifies => windowNotifies;

        public string ConfigurationError()
        {
            if (!clip || !clip.humanMotion || clip.isLooping)
                return "请选择单次 Humanoid 片段";
            return NotificationConfigurationError();
        }

        internal string NotificationConfigurationError()
        {
            int lastFrame = LastFrame;
            foreach (var point in pointNotifies)
                if (point != null && (point.PointFrame < 0 || point.PointFrame > lastFrame))
                    return "点通知触发帧超出片段";
            foreach (var window in windowNotifies)
                if (window != null && (window.StartFrame < 0 || window.EndFrame <= window.StartFrame || window.EndFrame > lastFrame))
                    return "窗口通知起止帧无效";
            return null;
        }

        public double FrameTime(int frame) => Math.Min(frame / (double)clip.frameRate, Duration);
        public int FrameBefore(double time) => Mathf.Clamp((int)Math.Floor(time * clip.frameRate + 1e-9), 0, LastFrame - 1);
    }
}
