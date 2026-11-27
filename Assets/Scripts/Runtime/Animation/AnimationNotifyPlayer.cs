using System;
using System.Collections.Generic;
using Endfield.CombatContracts;
using Endfield.Common;
using UnityEngine;

namespace Endfield.Animation
{
    public enum NotifyPhase { Point, Enter, Tick, Exit }
    public enum NotifyEndReason { Natural, Interrupted }

    public readonly struct AnimationNotice
    {
        public readonly ActorId ActorId;
        public readonly GameObject User;
        public readonly long PlayId;
        public readonly int RuntimeIndex, Frame;
        public readonly string Signal;
        public readonly NotifyPhase Phase;
        public readonly NotifyEndReason Reason;

        internal AnimationNotice(ActorId actor, GameObject user, long play, int index, int frame, string signal,
            NotifyPhase phase, NotifyEndReason reason)
        {
            ActorId = actor;
            User = user;
            PlayId = play;
            RuntimeIndex = index;
            Frame = frame;
            Signal = signal;
            Phase = phase;
            Reason = reason;
        }
    }

    public sealed class AnimationNotifyPlayer
    {
        private sealed class Playback
        {
            internal ActorId Actor;
            internal GameObject User;
            internal long Id;
            internal AnimationDefination Asset;
            internal NotifyTrack Track;
        }

        private sealed class NotifyTrack
        {
            internal readonly List<(string signal, AnimationPointNotify point, AnimationWindowNotify window, int startFrame, int endFrame)> Entries = new();
            internal readonly List<(int frame, NotifyPhase phase, int index)> Boundaries = new();
            internal readonly SortedSet<int> OpenWindows = new();
            internal int LastFrame = -1, NextBoundary;
        }

        private readonly ChangeNotifier<AnimationNotice> notices = new();
        private readonly Action<AnimationNotice> publishNotice;
        private long sequence;
        private Playback current;

        public AnimationNotifyPlayer() => publishNotice = notices.Publish;

        public IDisposable Subscribe(Action<AnimationNotice> listener) => notices.Subscribe(listener);

        public bool IsPlaying(long playId) => current != null && current.Id == playId;
        internal bool HasNotifies(long playId) => IsPlaying(playId) && current.Track != null;

        public long Begin(AnimationDefination asset, ActorId actor, GameObject user = null)
        {
            if (current != null)
            {
                End(current.Id, NotifyEndReason.Interrupted);
                if (current != null) return 0;
            }
            var play = new Playback { Actor = actor, User = user, Id = ++sequence, Asset = asset };
            var points = asset.PointNotifies;
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                if (point == null) continue;
                var track = play.Track ??= new NotifyTrack();
                int frame = point.PointFrame;
                track.Boundaries.Add((frame, NotifyPhase.Point, track.Entries.Count));
                track.Entries.Add((point.Signal, point, null, frame, frame));
            }
            var windows = asset.WindowNotifies;
            for (int i = 0; i < windows.Count; i++)
            {
                var window = windows[i];
                if (window == null) continue;
                var track = play.Track ??= new NotifyTrack();
                int index = track.Entries.Count;
                int start = window.StartFrame, end = window.EndFrame;
                track.Boundaries.Add((start, NotifyPhase.Enter, index));
                track.Boundaries.Add((end, NotifyPhase.Exit, index));
                track.Entries.Add((window.Signal, null, window, start, end));
            }
            play.Track?.Boundaries.Sort();
            current = play;
            return play.Id;
        }

        public void AdvanceTo(long playId, double clipTime)
        {
            if (!HasNotifies(playId) || !(clipTime >= 0)) return;
            var play = current;
            var track = play.Track;
            int last = clipTime >= play.Asset.Duration ? play.Asset.LastFrame : play.Asset.FrameBefore(clipTime);
            if (last <= track.LastFrame) return;
            while (track.LastFrame < last)
            {
                int frame = track.LastFrame + 1;
                if (track.OpenWindows.Count == 0)
                {
                    if (track.NextBoundary == track.Boundaries.Count) break;
                    frame = track.Boundaries[track.NextBoundary].frame;
                    if (frame > last) break;
                }
                track.LastFrame = frame;

                if (!DispatchBoundaries(play, frame, NotifyPhase.Enter)) return;
                foreach (int index in track.OpenWindows)
                {
                    var entry = track.Entries[index];
                    if (frame <= entry.startFrame || frame >= entry.endFrame) continue;
                    Dispatch(play, index, frame, NotifyPhase.Tick);
                    if (current != play) return;
                }
                if (!DispatchBoundaries(play, frame, NotifyPhase.Exit)) return;
            }
            track.LastFrame = last;
        }

        public void End(long playId, NotifyEndReason reason)
        {
            if (!IsPlaying(playId)) return;
            var play = current;
            var track = play.Track;
            if (track != null && reason == NotifyEndReason.Natural)
            {
                AdvanceTo(playId, play.Asset.Duration);
                if (current != play) return;
            }
            current = null;
            if (track == null) return;
            foreach (int index in track.OpenWindows)
                Dispatch(play, index, track.LastFrame, NotifyPhase.Exit, reason);
        }

        private bool DispatchBoundaries(Playback play, int frame, NotifyPhase lastPhase)
        {
            var track = play.Track;
            while (track.NextBoundary < track.Boundaries.Count)
            {
                var (boundaryFrame, phase, index) = track.Boundaries[track.NextBoundary];
                if (boundaryFrame != frame || phase > lastPhase) break;
                track.NextBoundary++;
                if (phase == NotifyPhase.Enter) track.OpenWindows.Add(index);
                if (phase == NotifyPhase.Exit) track.OpenWindows.Remove(index);
                Dispatch(play, index, frame, phase);
                if (current != play) return false;
            }
            return true;
        }

        private void Dispatch(Playback play, int index, int frame, NotifyPhase phase, NotifyEndReason reason = NotifyEndReason.Natural)
        {
            var entry = play.Track.Entries[index];
            var notice = new AnimationNotice(play.Actor, play.User, play.Id, index, frame, entry.signal, phase, reason);
            switch (phase)
            {
                case NotifyPhase.Point: entry.point.TriggerNotify(notice, publishNotice); break;
                case NotifyPhase.Enter: entry.window.OnEnterNotify(notice, publishNotice); break;
                case NotifyPhase.Tick: entry.window.OnTickNotify(notice, publishNotice); break;
                case NotifyPhase.Exit: entry.window.OnExitNotify(notice, publishNotice); break;
            }
        }
    }
}
