using System;

namespace Endfield.AnimationSystem
{
    [Serializable]
    public abstract class TimelineWindowEvent
    {
        public float start;
        public float end;
        // 移动前窗口只接收 OnInterval 的时间交集，其余窗口按边界派发进入和退出
        public virtual bool BeforeMovement => false;
        public virtual object OnEnter(AnimationPlayback playback) => null;
        public virtual void OnInterval(AnimationPlayback playback, float from, float to) { }
        public virtual void OnExit(AnimationPlayback playback, object state) { }
    }
}
