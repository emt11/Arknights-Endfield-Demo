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
        public float start;
        public float end;
        public ActionCategory categories;

        public bool Allows(float time, ActionCategory category) => time >= start && time < end && (categories & category) != 0;
    }

    [Serializable]
    public sealed class HitWindow
    {
        public int group;
        [Min(0f)] public float damageMultiplier = 1f;
        public float start;
        public float end;
        public int[] hitboxIds = Array.Empty<int>();
    }

    [Serializable]
    public sealed class EffectWindow
    {
        public float start;
        public float end;
        public ParticleEffectDefinition effect;
        public string bonePath;
        public Vector3 offset;
        public Vector3 rotation;
        public bool follow = true;
    }

    [Serializable]
    public abstract class ActionPoint
    {
        public float time;
    }

    [Serializable]
    public sealed class ComboAdvancePoint : ActionPoint { }

    [Serializable]
    public sealed class AudioPoint : ActionPoint
    {
        public AudioDefinition audio;
        public string bonePath;
        public Vector3 offset;
    }

    [Serializable]
    public sealed class EffectPoint : ActionPoint
    {
        public ParticleEffectDefinition effect;
        public string bonePath;
        public Vector3 offset;
        public Vector3 rotation;
        public bool follow;
    }

    [CreateAssetMenu(menuName = "Endfield/Action Definition", fileName = "ActionDefinition")]
    public sealed class ActionDefinition : ScriptableObject
    {
        public AnimationClip clip;
        [Min(0f)] public float clipStart;
        [Min(0f)] public float clipEnd;
        [Min(0.01f)] public float playbackRate = 1f;
        [Min(0f)] public float blendDuration = 0.1f;
        public bool useRootMotion = true;
        public ActionCategory category = ActionCategory.Attack;
        public CancelWindow[] cancelWindows = Array.Empty<CancelWindow>();
        [SerializeReference] public ActionPoint[] points = Array.Empty<ActionPoint>();
        public HitWindow[] hitWindows = Array.Empty<HitWindow>();
        public EffectWindow[] effectWindows = Array.Empty<EffectWindow>();
        public RootMotionDefinition rootMotion;
        public MotionWarpWindow[] warpWindows = Array.Empty<MotionWarpWindow>();

        public float Duration => clipEnd - clipStart;
    }
}
