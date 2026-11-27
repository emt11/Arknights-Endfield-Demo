using System.Collections.Generic;
using Endfield.Animation;
using Endfield.MotionWarping;
using Endfield.Actors;
using Endfield.CharacterControl.FSM;
using UnityEngine;

namespace Endfield.CharacterControl
{
    public class PartyMemberControl : CharacterControl
    {
        internal static readonly int MoveAmount = Animator.StringToHash("MoveAmount");

        [SerializeField, Min(0.01f)] private float walkSpeed = 1.43936f;
        [SerializeField, Min(0.01f)] private float runSpeed = 4.91111f;
        [SerializeField, Min(0f)] private float turnSpeed = 720f;
        [SerializeField, Min(.01f)] private float walkReferenceSpeed = 1.43936f;
        [SerializeField, Min(.01f)] private float runReferenceSpeed = 4.91111f;
        [SerializeField, InspectorName("闪避动画配置")] private AnimationDefination dodgeAnimation;

        public AnimationDefination DodgeAnimation => dodgeAnimation;
        internal bool CanDodge => AnimationSource && AnimationSource.Model.isHuman &&
            AnimationSource.HasState(PartyMemberDodgeState.StateHash);

        private bool steppingAction;
        private bool endingAction;

        internal CharacterActionPlayback ActionPlayback { get; private set; }
        internal float TurnSpeed => turnSpeed;
        internal float WalkSpeed => walkSpeed;
        internal float RunSpeed => runSpeed;
        internal AnimationSource AnimationSource { get; private set; }

        public PartyMemberGroundedState Grounded { get; private set; }
        public PartyMemberDodgeState Dodge { get; private set; }

        protected override CharacterState CreateInitialState()
        {
            var animator = GetComponent<Combatant>()?.ModelAnimator;
            if (animator && animator.TryGetComponent<AnimationSource>(out var source))
            {
                AnimationSource = source;
                source.Initialize();
                ActionPlayback = new CharacterActionPlayback(this, source);
            }
            Grounded = new PartyMemberGroundedState(this);
            Dodge = new PartyMemberDodgeState(this);
            return Grounded;
        }

        // 输入、状态切换和本步剩余时间均由队员统一处理
        protected override void StepActor(double actionDelta)
        {
            if (CurrentState == Dodge && Dodge.CanCancel)
                InterruptAction();
            else if (CurrentState == Grounded && ActionPlayback?.Active != true &&
                     (StepCommands & CharacterCommand.Dodge) != 0 && CanDodge)
                Machine.ChangeState(Dodge);

            if (ActionPlayback?.Active != true)
            {
                if (CurrentState == Dodge)
                {
                    Machine.ChangeState(Grounded);
                    AnimationSource.EndClip();
                }
                Grounded.Tick(actionDelta);
                return;
            }

            steppingAction = true;
            double remaining;
            try { remaining = ActionPlayback.Advance(actionDelta); }
            finally { steppingAction = false; }
            if (!ActionPlayback.Active || ActionPlayback.EndReason is not NotifyEndReason reason) return;
            EndAction(reason);
            // 动作段末返回地面时，余量只更新姿态，下一步才恢复输入位移
            if (remaining > 0) AdvanceLocomotion(remaining);
        }

        public bool TryPlayAction(AnimationDefination animation, float speed = 1f,
            IReadOnlyList<WarpTarget> targets = null)
        {
            if (ActionPlayback == null || CurrentState != Grounded || ActionPlayback.Active || steppingAction || endingAction ||
                !animation || animation.ConfigurationError() != null || !(speed > 0) || float.IsInfinity(speed)) return false;
            Motor.ResetLocomotion();
            GroundSpeed = 0;
            AnimationSource.SetFloat(MoveAmount, 0);
            AnimationSource.BeginPlayableClip(animation.Clip, speed);
            ActionPlayback.Begin(animation, transform.rotation, targets);
            return true;
        }

        internal override void InterruptAction() => EndAction(NotifyEndReason.Interrupted);

        private void EndAction(NotifyEndReason reason)
        {
            if (endingAction || ActionPlayback?.Active != true) return;
            endingAction = true;
            try
            {
                if (CurrentState == Dodge) Machine.ChangeState(Grounded);
                ActionPlayback.End(reason);
            }
            finally { endingAction = false; }
        }

        internal void AdvanceLocomotion(double actionSeconds)
        {
            if (!AnimationSource) return;
            float amount = GroundSpeed <= walkReferenceSpeed
                ? Mathf.InverseLerp(0f, walkReferenceSpeed, GroundSpeed)
                : 1f + Mathf.InverseLerp(walkReferenceSpeed, runReferenceSpeed, GroundSpeed);
            AnimationSource.SetFloat(MoveAmount, amount);
            AnimationSource.Advance(actionSeconds);
        }
    }
}
