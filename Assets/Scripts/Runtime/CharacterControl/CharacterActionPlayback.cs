using System.Collections.Generic;
using Endfield.Animation;
using Endfield.MotionWarping;
using UnityEngine;

namespace Endfield.CharacterControl
{
    // 一次角色动作共用的执行流程，片段时间由 AnimationSource 持有
    internal sealed class CharacterActionPlayback
    {
        private readonly CharacterControl actor;
        private readonly AnimationSource source;
        private readonly MotionWarping.MotionWarping warping = new();
        private Quaternion basis;
        private bool useRootMotion, useWarp, hasNotifies;
        public long PlayId { get; private set; }
        public bool Active { get; private set; }

        public CharacterActionPlayback(CharacterControl actor, AnimationSource source)
        {
            this.actor = actor;
            this.source = source;
        }

        public void Begin(AnimationDefination animation, Quaternion basisYaw, IReadOnlyList<WarpTarget> targets = null)
        {
            Active = true;
            basis = basisYaw;
            bool matches = animation && animation.Clip == source.ActiveClip;
            useRootMotion = matches && animation.UseRootMotion;
            var warpAsset = useRootMotion ? animation.WarpAsset : null;
            useWarp = warpAsset && targets != null && warpAsset.Matches(source.ActiveClip, source.Model)
                && warping.BeginWarp(warpAsset, targets);
            if (matches && animation.NotificationConfigurationError() == null)
                PlayId = actor.Notifications.Begin(animation, actor.ActorId, actor.gameObject);
            hasNotifies = actor.Notifications.HasNotifies(PlayId);
        }

        // 段末通知可以立即结束播放，因此下一段从通知返回后再决定是否推进
        public double Advance(double remaining)
        {
            if (hasNotifies && source.ClipTime == 0) actor.Notifications.AdvanceTo(PlayId, 0);
            while (Active && remaining > 0 && !EndReason.HasValue)
            {
                double from = source.ClipTime;
                var (consumed, end, natural) = source.EvaluateSegment(
                    remaining, useRootMotion || hasNotifies, actor.transform.rotation);
                remaining -= consumed;
                if (useRootMotion)
                {
                    var motion = useWarp ? warping.UpdateWarp(from, end,
                        new Pose(actor.transform.position, actor.transform.rotation), basis, natural) : natural.InBasis(basis);
                    actor.Motor.MovePlanar(motion.Displacement, motion.Rotation);
                }
                if (hasNotifies) actor.Notifications.AdvanceTo(PlayId, end);
            }
            return remaining;
        }

        public NotifyEndReason? EndReason
        {
            get
            {
                if (PlayId != 0 && !actor.Notifications.IsPlaying(PlayId)) return NotifyEndReason.Interrupted;
                return source.AtEnd ? NotifyEndReason.Natural : null;
            }
        }

        public void End(NotifyEndReason reason)
        {
            if (!Active) return;
            Active = false;
            actor.Notifications.End(PlayId, reason);
            PlayId = 0;
            warping.EndWarp();
            if (source) source.EndClip();
        }
    }
}
