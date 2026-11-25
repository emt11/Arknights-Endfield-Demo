using Endfield.CharacterControl.FSM;
using UnityEngine;

namespace Endfield.CharacterControl
{
    public class PartyMemberControl : CharacterControl
    {
        [SerializeField, Min(0.01f)] private float walkSpeed = 1.43936f;
        [SerializeField, Min(0.01f)] private float runSpeed = 4.91111f;
        [SerializeField, Min(0f)] private float turnSpeed = 720f;

        internal float WalkSpeed => walkSpeed;
        internal float RunSpeed => runSpeed;

        public CharacterState Idle { get; protected set; }
        public CharacterState Walk { get; protected set; }
        public CharacterState Run { get; protected set; }

        protected override CharacterState CreateInitialState()
        {
            Idle = new PartyMemberIdleState(this);
            Walk = new PartyMemberWalkState(this);
            Run = new PartyMemberRunState(this);
            return Idle;
        }

        internal void StepGroundMovement(float gaitSpeed, double actionDelta)
        {
            GroundSpeed = Motor.StepLocomotion(MoveWorld, gaitSpeed, turnSpeed, (float)actionDelta);
        }
    }
}
