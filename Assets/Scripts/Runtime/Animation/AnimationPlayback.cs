using System;
using System.Collections.Generic;
using Endfield.Pooling;
using Endfield.Presentation;
using UnityEngine;

namespace Endfield.AnimationSystem
{
    public sealed class AnimationPlayback
    {
        internal struct Window
        {
            public bool entered;
            public bool active;
            public object state;
        }

        internal readonly Window[] windows;
        internal readonly List<PoolHandle> ownedHandles = new List<PoolHandle>();
        private float dispatchedTime = -1f;
        private bool dispatching;
        private float playbackRate;

        public AnimationDefinition Definition { get; }
        public Transform Model { get; }
        public CombatFeedbackPlayer Feedback { get; }
        public bool Ended { get; private set; }
        internal float PlaybackRate
        {
            get => playbackRate;
            set
            {
                if (!(value > 0f) || float.IsInfinity(value))
                    throw new ArgumentOutOfRangeException(nameof(value), "Playback rate must be finite and positive");
                playbackRate = value;
            }
        }

        public float Time { get; private set; }
        public float PreviousTime { get; private set; }
        public object Receiver { get; }
        internal int NextPoint { get; private set; }
        internal bool IsActionPlayback => Receiver != null;

        internal AnimationPlayback(AnimationDefinition definition, Transform model, CombatFeedbackPlayer feedback, object receiver)
        {
            Definition = definition;
            PlaybackRate = definition.playbackRate;
            Model = model;
            Feedback = feedback;
            Receiver = receiver;
            windows = new Window[definition.windowEvents.Length];
        }

        public Transform ResolveAnchor(AnimationAnchor point) => AnimationAnchorBindings.Resolve(Model, point);

        internal PoolHandle PlayEffect(ParticleEffectDefinition effect, AnimationAnchor point, Vector3 offset, Vector3 rotation, bool follow)
        {
            Transform anchor = ResolveAnchor(point);
            return Feedback.PlayEffect(effect, anchor.TransformPoint(offset), anchor.rotation * Quaternion.Euler(rotation), follow ? anchor : null);
        }

        public void Own(PoolHandle handle)
        {
            if (Ended)
                handle.Return();
            else if (handle.IsValid)
                ownedHandles.Add(handle);
        }

        internal void Advance(float step)
        {
            // 战斗动作由输入提交方派发零时刻事件，普通播放在首次推进前派发
            if (!IsActionPlayback && Time == 0f)
                Dispatch();
            if (Ended)
                return;

            PreviousTime = Time;
            Time = Mathf.Min(Definition.Duration, Time + step * PlaybackRate);
            for (int index = 0; !Ended && index < Definition.windowEvents.Length; index++)
            {
                TimelineWindowEvent entry = Definition.windowEvents[index];
                if (!entry.BeforeMovement)
                    continue;

                float from = Mathf.Max(PreviousTime, entry.start);
                float to = Mathf.Min(Time, entry.end);
                if (from < to)
                    entry.OnInterval(this, from, to);
            }
        }

        internal void Dispatch()
        {
            if (Ended || dispatching || Time == dispatchedTime)
                return;

            bool initial = dispatchedTime < 0f;
            float from = Mathf.Max(0f, dispatchedTime);
            float to = Time;
            dispatchedTime = to;
            dispatching = true;
            try
            {
                if (initial)
                    DispatchBoundary(0f);
                while (!Ended && from < to)
                {
                    // 一个大步可能跨过短窗口和后续替换通知，按局部时间处理它们
                    float boundary = to;
                    if (NextPoint < Definition.pointEvents.Length)
                        boundary = Mathf.Min(boundary, Definition.pointEvents[NextPoint].time);
                    for (int index = 0; index < windows.Length; index++)
                    {
                        TimelineWindowEvent notify = Definition.windowEvents[index];
                        if (notify.BeforeMovement)
                            continue;
                        if (!windows[index].entered)
                            boundary = Mathf.Min(boundary, notify.start);
                        else if (windows[index].active)
                            boundary = Mathf.Min(boundary, notify.end);
                    }

                    for (int index = 0; !Ended && index < windows.Length; index++)
                    {
                        if (!Definition.windowEvents[index].BeforeMovement && windows[index].active)
                            Definition.windowEvents[index].OnInterval(this, from, boundary);
                    }
                    DispatchBoundary(boundary);
                    from = boundary;
                }
            }
            finally
            {
                dispatching = false;
            }
        }

        private void DispatchBoundary(float time)
        {
            TimelinePointEvent[] points = Definition.pointEvents;
            while (!Ended && NextPoint < points.Length && points[NextPoint].time <= time)
                points[NextPoint++].OnTrigger(this);

            for (int index = 0; !Ended && index < windows.Length; index++)
            {
                TimelineWindowEvent notify = Definition.windowEvents[index];
                if (notify.BeforeMovement)
                    continue;
                ref Window window = ref windows[index];
                if (!window.entered && time >= notify.start)
                {
                    window.entered = true;
                    window.state = notify.OnEnter(this);
                    window.active = true;
                    // Enter 可以触发替换，返回的资源仍归本次窗口所有
                }
                if (window.active && (Ended || time >= notify.end))
                    Exit(index);
            }
        }

        private void Exit(int index)
        {
            ref Window window = ref windows[index];
            window.active = false;
            object state = window.state;
            window.state = null;
            Definition.windowEvents[index].OnExit(this, state);
        }

        internal void End()
        {
            if (Ended)
                return;

            Ended = true;
            for (int index = 0; index < windows.Length; index++)
            {
                if (windows[index].active)
                    Exit(index);
            }
            foreach (PoolHandle handle in ownedHandles)
                handle.Return();
            ownedHandles.Clear();
        }
    }
}
