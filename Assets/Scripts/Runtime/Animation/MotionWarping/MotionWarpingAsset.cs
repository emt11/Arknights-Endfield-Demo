using System;
using System.Collections.Generic;
using UnityEngine;

namespace Endfield.MotionWarping
{
    [Serializable]
    public struct WarpWindowMotion
    {
        public int startFrame, endFrame;
        public Vector3 displacement;
        public float endYaw;
    }

    [Serializable]
    public struct WarpWindow
    {
        public int startFrame, endFrame;
        public Vector2 targetOffsetXZ;
        public bool alignYaw;
    }

    [CreateAssetMenu(menuName = "Endfield/动画/运动扭曲")]
    public sealed class MotionWarpingAsset : ScriptableObject
    {
        [SerializeField] private AnimationClip clip;
        [SerializeField] private GameObject sampleModel;
        [SerializeField] private WarpWindow[] warpWindows = Array.Empty<WarpWindow>();
        [SerializeField, HideInInspector] private WarpWindowMotion[] summaries = Array.Empty<WarpWindowMotion>();
        [SerializeField, HideInInspector] private string sourceSignature;

        public AnimationClip Clip => clip;
        public GameObject SampleModel => sampleModel;
        public IReadOnlyList<WarpWindow> Windows => warpWindows;
        public IReadOnlyList<WarpWindowMotion> Summaries => summaries;

        public bool HasCurrentSummary
        {
            get
            {
                if (!clip || !sampleModel || warpWindows.Length == 0 || summaries.Length != warpWindows.Length ||
                    string.IsNullOrEmpty(sourceSignature)) return false;
                for (int i = 0; i < summaries.Length; i++)
                    if (summaries[i].startFrame != warpWindows[i].startFrame || summaries[i].endFrame != warpWindows[i].endFrame)
                        return false;
#if UNITY_EDITOR
                return sourceSignature == Signature();
#else
                return true;
#endif
            }
        }

        public bool Matches(AnimationClip playingClip, Animator actor)
        {
            if (!actor || !sampleModel || playingClip != clip) return false;
            var sample = sampleModel.GetComponentInChildren<Animator>();
            return sample && sample.avatar == actor.avatar &&
                (sample.transform.lossyScale - actor.transform.lossyScale).sqrMagnitude < .000001f;
        }

        public bool ValidWindows()
        {
            if (!clip || warpWindows.Length == 0) return false;
            int last = Mathf.CeilToInt(clip.length * clip.frameRate - .00001f);
            int previous = 0;
            foreach (var window in warpWindows)
            {
                if (window.startFrame < previous || window.endFrame <= window.startFrame || window.endFrame > last) return false;
                previous = window.endFrame;
            }
            return true;
        }

        public double FrameTime(int frame) => Math.Min(frame / (double)clip.frameRate, clip.length);

#if UNITY_EDITOR
        private string Signature()
        {
            var animator = sampleModel.GetComponentInChildren<Animator>();
            string Source(UnityEngine.Object value) => UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(value) + "/" +
                UnityEditor.AssetDatabase.GetAssetDependencyHash(UnityEditor.AssetDatabase.GetAssetPath(value));
            return animator && animator.avatar ? Source(sampleModel) + Source(animator.avatar) + Source(clip) : "";
        }

        public void StoreSummary(WarpWindowMotion[] value)
        {
            summaries = value;
            sourceSignature = Signature();
        }
#endif
    }
}
