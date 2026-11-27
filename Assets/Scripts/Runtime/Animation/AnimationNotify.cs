using System;
using UnityEngine;

namespace Endfield.Animation
{
    [Serializable]
    public class AnimationPointNotify
    {
        [SerializeField] private string signal;
        [SerializeField] private int pointFrame;

        public string Signal => signal;
        public int PointFrame => pointFrame;

        protected AnimationPointNotify() { }

        public AnimationPointNotify(string signal, int frame)
        {
            this.signal = signal;
            pointFrame = frame;
        }

        public virtual void TriggerNotify(AnimationNotice notice, Action<AnimationNotice> publish) => publish?.Invoke(notice);
    }

    [Serializable]
    public class AnimationWindowNotify
    {
        [SerializeField] private string signal;
        [SerializeField] private int startFrame;
        [SerializeField] private int endFrame;

        public string Signal => signal;
        public int StartFrame => startFrame;
        public int EndFrame => endFrame;

        protected AnimationWindowNotify() { }

        public AnimationWindowNotify(string signal, int startFrame, int endFrame)
        {
            this.signal = signal;
            this.startFrame = startFrame;
            this.endFrame = endFrame;
        }

        public virtual void OnEnterNotify(AnimationNotice notice, Action<AnimationNotice> publish) => publish?.Invoke(notice);

        public virtual void OnTickNotify(AnimationNotice notice, Action<AnimationNotice> publish) => publish?.Invoke(notice);

        public virtual void OnExitNotify(AnimationNotice notice, Action<AnimationNotice> publish) => publish?.Invoke(notice);
    }
}
