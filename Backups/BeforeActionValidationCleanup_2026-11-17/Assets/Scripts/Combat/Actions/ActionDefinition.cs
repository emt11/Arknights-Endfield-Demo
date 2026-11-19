using System;
using Endfield.Presentation;
using UnityEngine;

namespace Endfield.Combat
{
    [Flags]
    public enum ActionCategory
    {
        Attack = 1,
        Move = 2
    }

    [Serializable]
    public struct CancelWindow
    {
        public float Start;
        public float End;
        public ActionCategory Categories;

        public bool Allows(float time, ActionCategory category) => time >= Start && time < End && (Categories & category) != 0;
    }

    public enum ActionPointKind
    {
        Audio,
        Effect,
        ComboAdvance
    }

    [Serializable]
    public sealed class HitWindow
    {
        public int Group;
        [Min(0f)] public float DamageMultiplier = 1f;
        public float Start;
        public float End;
        public Vector3 Offset = new Vector3(0f, 1f, 1f);
        [Min(0.01f)] public float Radius = 0.8f;
    }

    [Serializable]
    public sealed class EffectWindow
    {
        public float Start;
        public float End;
        public ParticleEffectDefinition Effect;
        public string BonePath;
        public Vector3 Offset;
        public Vector3 Rotation;
        public bool Follow = true;
    }

    [Serializable]
    public sealed class ActionPoint
    {
        public float Time;
        public ActionPointKind Kind;
        public AudioDefinition Audio;
        public ParticleEffectDefinition Effect;
        public string BonePath;
        public Vector3 Offset;
        public Vector3 Rotation;
        public bool Follow;
    }

    [CreateAssetMenu(menuName = "Endfield/Action Definition", fileName = "ActionDefinition")]
    public sealed class ActionDefinition : ScriptableObject
    {
        public AnimationClip Clip;
        [Min(0f)] public float ClipStart;
        [Min(0f)] public float ClipEnd;
        [Min(0.01f)] public float PlaybackRate = 1f;
        [Min(0f)] public float BlendDuration = 0.1f;
        public bool UseRootMotion = true;
        public ActionCategory Category = ActionCategory.Attack;
        public CancelWindow[] CancelWindows = Array.Empty<CancelWindow>();
        public ActionPoint[] Points = Array.Empty<ActionPoint>();
        public HitWindow[] HitWindows = Array.Empty<HitWindow>();
        public EffectWindow[] EffectWindows = Array.Empty<EffectWindow>();
        public RootMotionDefinition RootMotion;
        public MotionWarpWindow[] WarpWindows = Array.Empty<MotionWarpWindow>();

        public float Duration => ClipEnd - ClipStart;

        public void ValidateConfiguration()
        {
            if (Clip == null || ClipStart < 0f || ClipEnd <= ClipStart || ClipEnd > Clip.length + 0.0001f || PlaybackRate <= 0f)
                throw new InvalidOperationException(name + " has an invalid clip range or rate");

            float previous = -1f;
            foreach (ActionPoint point in Points)
            {
                if (point.Time < previous || point.Time < 0f || point.Time > Duration)
                    throw new InvalidOperationException(name + " requires sorted points inside its duration");

                previous = point.Time;
                if (point.Kind == ActionPointKind.Audio && point.Audio == null || point.Kind == ActionPointKind.Effect && point.Effect == null)
                    throw new InvalidOperationException(name + " has a point with a missing presentation asset");

            }

            foreach (CancelWindow window in CancelWindows)
            {
                if (window.Start < 0f || window.End <= window.Start || window.End > Duration)
                    throw new InvalidOperationException(name + " has an invalid cancel window");
            }

            foreach (HitWindow window in HitWindows)
            {
                if (window.Start < 0f || window.End <= window.Start || window.End > Duration || window.Radius <= 0f)
                    throw new InvalidOperationException(name + " has an invalid hit window");

                foreach (HitWindow other in HitWindows)
                {
                    if (other.Group == window.Group && other.DamageMultiplier != window.DamageMultiplier)
                        throw new InvalidOperationException(name + " has conflicting damage in one hit group");
                }
            }

            foreach (EffectWindow window in EffectWindows)
            {
                if (window.Start < 0f || window.End <= window.Start || window.End > Duration || window.Effect == null)
                    throw new InvalidOperationException(name + " has an invalid effect window or missing asset");
            }

            if (WarpWindows.Length == 0)
                return;
            if (!UseRootMotion || RootMotion == null)
                throw new InvalidOperationException(name + " requires baked root motion for warping");
            RootMotion.Validate(Clip);
            float previousEnd = 0f;
            foreach (MotionWarpWindow window in WarpWindows)
            {
                if (window.Start < previousEnd || window.End <= window.Start || window.End > Duration || window.MaxSpeed <= 0f)
                    throw new InvalidOperationException(name + " requires ordered non-overlapping warp windows");
                previousEnd = window.End;
                if (Mathf.Abs(window.Correction.Evaluate(0f)) > .0001f || Mathf.Abs(window.Correction.Evaluate(1f) - 1f) > .0001f)
                    throw new InvalidOperationException(name + " requires a correction curve from 0 to 1");
                Vector3 origin = RootMotion.Sample(ClipStart + window.Start).Position;
                Vector3 end = RootMotion.Sample(ClipStart + window.End).Position - origin;
                end.y = 0f;
                if (window.WarpTranslation && end.sqrMagnitude < .000001f)
                    throw new InvalidOperationException(name + " cannot translate-warp an in-place interval");
                float lastProgress = 0f;
                float lastWeight = 0f;
                int samples = Mathf.Max(60, Mathf.CeilToInt((window.End - window.Start) * RootMotion.SampleRate));
                for (int index = 1; index <= samples; index++)
                {
                    float fraction = index / (float)samples;
                    float weight = window.Correction.Evaluate(fraction);
                    Vector3 offset = RootMotion.Sample(ClipStart + Mathf.Lerp(window.Start, window.End, fraction)).Position - origin;
                    float progress = end.sqrMagnitude > .000001f ? Vector3.Dot(offset, end) / end.sqrMagnitude : fraction;
                    // Humanoid 导入存在毫米级抖动，回撤超过 1.5 毫米才视为非单调段
                    float sourceRetreat = (lastProgress - progress) * end.magnitude;
                    if (weight < lastWeight - .0001f || weight > 1.0001f || window.WarpTranslation && sourceRetreat > .0015f)
                        throw new InvalidOperationException(name + " requires monotonic source progress and correction curve");
                    lastProgress = progress;
                    lastWeight = weight;
                }
            }
        }
    }
}
