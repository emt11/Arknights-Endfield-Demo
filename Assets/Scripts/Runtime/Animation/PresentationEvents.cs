using System;
using Endfield.Pooling;
using Endfield.Presentation;
using UnityEngine;

namespace Endfield.AnimationSystem
{
    [Serializable]
    public sealed class AudioPoint : TimelinePointEvent
    {
        public AudioDefinition audio;
        public AnimationAnchor anchor;
        public Vector3 offset;

        public override void OnTrigger(AnimationPlayback playback)
        {
            PoolHandle handle = playback.Feedback.PlayAudio(audio, playback.ResolveAnchor(anchor).TransformPoint(offset));
            if (handle.IsValid && audio.Loop)
                playback.Own(handle);
        }
    }

    [Serializable]
    public sealed class EffectPoint : TimelinePointEvent
    {
        public ParticleEffectDefinition effect;
        public AnimationAnchor anchor;
        public Vector3 offset;
        public Vector3 rotation;
        public bool follow;

        public override void OnTrigger(AnimationPlayback playback)
        {
            PoolHandle handle = playback.PlayEffect(effect, anchor, offset, rotation, follow);
            if (follow)
                playback.Own(handle);
        }
    }

    [Serializable]
    public sealed class EffectWindow : TimelineWindowEvent
    {
        public ParticleEffectDefinition effect;
        public AnimationAnchor anchor;
        public Vector3 offset;
        public Vector3 rotation;
        public bool follow = true;

        public override object OnEnter(AnimationPlayback playback) => playback.PlayEffect(effect, anchor, offset, rotation, follow);

        public override void OnExit(AnimationPlayback playback, object state) => ((PoolHandle)state).Return();
    }
}
