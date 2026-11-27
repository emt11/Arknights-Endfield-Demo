using System;
using Endfield.Animation;
using UnityEngine;

namespace Endfield.CharacterControl.FSM
{
    public sealed class PartyMemberDodgeState : CharacterState
    {
        internal static readonly int StateHash = Animator.StringToHash("Base Layer.Dodge");
        private static readonly int IsDodging = Animator.StringToHash("IsDodging");
        private readonly PartyMemberControl member;
        private IDisposable subscription;
        private bool allowMoveCancel;

        internal PartyMemberDodgeState(PartyMemberControl member) => this.member = member;

        internal bool CanCancel => allowMoveCancel && member.MoveWorld.sqrMagnitude > 0;

        public override void Enter()
        {
            allowMoveCancel = false;
            member.GroundSpeed = 0;
            var source = member.AnimationSource;
            source.SetBool(IsDodging, true);
            if (!source.BeginControllerClip(StateHash))
            {
                Debug.LogWarning($"{member.name} 的 Dodge 状态需要一个单次播放的 Humanoid 片段和有限正倍率", member);
                return;
            }
            Vector3 direction = member.MoveWorld == Vector3.zero ? member.transform.forward : member.MoveWorld;
            member.ActionPlayback.Begin(member.DodgeAnimation, Quaternion.LookRotation(direction));
            subscription = member.Notifications.Subscribe(OnNotice);
        }

        private void OnNotice(AnimationNotice notice)
        {
            if (notice.PlayId != member.ActionPlayback.PlayId || notice.Phase != NotifyPhase.Point ||
                notice.Signal != "允许移动取消") return;
            allowMoveCancel = true;
            if (CanCancel) member.Notifications.End(notice.PlayId, NotifyEndReason.Interrupted);
        }

        public override void Exit()
        {
            subscription?.Dispose();
            subscription = null;
            if (member.AnimationSource) member.AnimationSource.SetBool(IsDodging, false);
        }
    }
}
