using Endfield.Animation;
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

        internal float WalkSpeed => walkSpeed;
        internal float RunSpeed => runSpeed;
        internal AnimationSource AnimationSource { get; private set; }

        public CharacterState Idle { get; protected set; }
        public CharacterState Walk { get; protected set; }
        public CharacterState Run { get; protected set; }
        public CharacterState Dodge { get; private set; }

        protected override CharacterState CreateInitialState()
        {
            var animator = GetComponent<Combatant>()?.ModelAnimator;
            if (animator && animator.TryGetComponent<AnimationSource>(out var source))
            {
                AnimationSource = source;
                source.Initialize();
            }
            Idle = new PartyMemberIdleState(this);
            Walk = new PartyMemberWalkState(this);
            Run = new PartyMemberRunState(this);
            Dodge = new PartyMemberDodgeState(this);
            return Idle;
        }

        internal void StepGroundMovement(float gaitSpeed, double actionDelta)
        {
            GroundSpeed = Motor.StepLocomotion(MoveWorld, gaitSpeed, turnSpeed, (float)actionDelta);
            AdvanceLocomotion(actionDelta);
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
