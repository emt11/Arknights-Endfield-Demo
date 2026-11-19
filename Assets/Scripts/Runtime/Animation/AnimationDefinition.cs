using System;
using UnityEngine;

namespace Endfield.AnimationSystem
{
    [CreateAssetMenu(menuName = "Endfield/Animation Definition", fileName = "AnimationDefinition")]
    public sealed class AnimationDefinition : ScriptableObject
    {
        public AnimationClip clip;
        [Min(.01f)] public float playbackRate = 1f;
        [Min(0f)] public float easeInDuration = .1f;
        public AnimationCurve easeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Min(0f)] public float easeOutDuration = .1f;
        public AnimationCurve easeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        [SerializeReference] public TimelinePointEvent[] pointEvents = Array.Empty<TimelinePointEvent>();
        [SerializeReference] public TimelineWindowEvent[] windowEvents = Array.Empty<TimelineWindowEvent>();

        public float Duration => clip.length;
    }
}
