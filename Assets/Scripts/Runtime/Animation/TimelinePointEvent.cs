using System;

namespace Endfield.AnimationSystem
{
    [Serializable]
    public abstract class TimelinePointEvent
    {
        public float time;
        public abstract void OnTrigger(AnimationPlayback playback);
    }
}
