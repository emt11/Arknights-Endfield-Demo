using System;
using Endfield.AnimationSystem;
using UnityEngine;

namespace Endfield.Combat
{
    [Serializable]
    public sealed class ComboAdvancePoint : TimelinePointEvent
    {
        public override void OnTrigger(AnimationPlayback playback)
        {
            if (playback.Receiver is ActionRuntime action)
                action.ComboAdvanced = true;
        }
    }

    [Serializable]
    public sealed class CancelWindow : TimelineWindowEvent
    {
        public ActionCategory categories;

        public override object OnEnter(AnimationPlayback playback)
        {
            if (playback.Receiver is ActionRuntime action)
                action.cancelWindows.Add(this);
            return null;
        }

        public override void OnExit(AnimationPlayback playback, object state)
        {
            if (playback.Receiver is ActionRuntime action)
                action.cancelWindows.Remove(this);
        }
    }

    [Serializable]
    public sealed class HitWindow : TimelineWindowEvent
    {
        public int group;
        [Min(0f)] public float damageMultiplier = 1f;
        public int[] hitboxIds = Array.Empty<int>();
        public override bool BeforeMovement => true;

        public override void OnInterval(AnimationPlayback playback, float from, float to)
        {
            if (playback.Receiver is ActionRuntime action)
                action.hitIntervals.Add((this, from, to));
        }
    }
}
