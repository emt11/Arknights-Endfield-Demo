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
    }
}
