using System;
using Endfield.Animation;
using UnityEngine;

namespace Endfield.CharacterControl.FSM
{
    public sealed class PartyMemberDodgeState : CharacterState
    {
        internal static readonly int StateHash = Animator.StringToHash("Base Layer.Dodge");
        private static readonly int IsDodging = Animator.StringToHash("IsDodging");
        private const string AllowMoveCancel = "允许移动取消";

        private readonly PartyMemberControl member;
        private bool useRootMotion;
        private double elapsed;
        private Quaternion basisYaw;
        private bool moveCancelAllowed;
        private long playId;
        private IDisposable noticeSubscription;
        private bool ticking, interruptRequested;

        public PartyMemberDodgeState(PartyMemberControl member) : base(member) => this.member = member;

        public override CharacterState SelectNext()
        {
            return moveCancelAllowed && member.MoveWorld.sqrMagnitude > 0f
                ? PartyMemberGroundedState.SelectGait(member)
                : this;
        }

        public override void Enter()
        {
            elapsed = 0;
            playId = 0;
            moveCancelAllowed = false;
            interruptRequested = false;
            Vector3 travel = member.MoveWorld == Vector3.zero ? member.transform.forward : member.MoveWorld;
            basisYaw = Quaternion.LookRotation(travel);
            member.GroundSpeed = 0;
            var source = member.AnimationSource;
            source.SetBool(IsDodging, true);
            source.BeginClip(StateHash);
            var clip = source.ActiveClip;
            if (!clip)
            {
                Debug.LogWarning($"{member.name} 的 Dodge 状态需要一个单次播放的 Humanoid 片段和有限正倍率", member);
                return;
            }
            var animation = member.DodgeAnimation;
            bool matches = animation && animation.Clip == clip;
            useRootMotion = matches && animation.UseRootMotion;
            if (matches && animation.NotificationConfigurationError() == null)
            {
                playId = member.Notifications.Begin(animation, member.ActorId, member.gameObject);
                interruptRequested = playId == 0;
                if (member.Notifications.HasNotifies(playId))
                    noticeSubscription = member.Notifications.Subscribe(OnNotice);
            }
        }

        public override void Tick(double delta)
        {
            if (delta <= 0) return;
            var source = member.AnimationSource;
            var clip = source.ActiveClip;
            if (!clip)
            {
                ChangeState(PartyMemberGroundedState.SelectGait(member));
                member.CurrentState.Tick(delta);
                return;
            }
            double duration = clip.length;
            bool hasNotifies = noticeSubscription != null;
            bool splitAtFrame = useRootMotion || hasNotifies;
            double remaining = delta;
            ticking = true;
            try
            {
                if (hasNotifies && elapsed == 0) member.Notifications.AdvanceTo(playId, 0);
                while (remaining > 0 && elapsed < duration && !Interrupted)
                {
                    var rootRotation = useRootMotion ? member.transform.rotation : Quaternion.identity;
                    var (consumed, end, naturalMotion) = source.EvaluateSegment(
                        elapsed, remaining, splitAtFrame, useRootMotion, rootRotation);
                    if (useRootMotion)
                    {
                        var motion = naturalMotion.InBasis(basisYaw);
                        member.Motor.MovePlanar(motion.Displacement, motion.Rotation);
                    }
                    elapsed = end;
                    remaining -= consumed;
                    if (hasNotifies) member.Notifications.AdvanceTo(playId, elapsed);
                }
            }
            finally { ticking = false; }

            bool interrupted = Interrupted;
            bool finished = !interrupted && elapsed >= duration;
            if (!finished && !interrupted) return;
            member.Notifications.End(playId, finished ? NotifyEndReason.Natural : NotifyEndReason.Interrupted);
            ChangeState(PartyMemberGroundedState.SelectGait(member));
            // 片尾余下的动作秒只更新步态，地面位移从下一步恢复
            if (remaining > 0) member.AdvanceLocomotion(remaining);
        }

        private bool Interrupted => interruptRequested || (playId != 0 && !member.Notifications.IsPlaying(playId));

        private void OnNotice(AnimationNotice notice)
        {
            if (notice.PlayId != playId || notice.Phase != NotifyPhase.Point || notice.Signal != AllowMoveCancel) return;
            moveCancelAllowed = true;
            if (member.MoveWorld.sqrMagnitude > 0f) Interrupt();
        }

        public override void Interrupt()
        {
            if (interruptRequested) return;
            interruptRequested = true;
            member.Notifications.End(playId, NotifyEndReason.Interrupted);
            if (ticking) return;
            ChangeState(PartyMemberGroundedState.SelectGait(member));
        }

        public override void Exit()
        {
            noticeSubscription?.Dispose();
            noticeSubscription = null;
            var source = member.AnimationSource;
            if (source)
            {
                source.SetBool(IsDodging, false);
                source.EndClip();
            }
            member.Notifications.End(playId, NotifyEndReason.Interrupted);
        }
    }
}
