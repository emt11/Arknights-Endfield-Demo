namespace Endfield.CharacterControl.FSM
{
    public abstract class PartyMemberGroundedState : CharacterState
    {
        protected PartyMemberControl Member => (PartyMemberControl)Control;

        protected PartyMemberGroundedState(PartyMemberControl member) : base(member) { }

        public override CharacterState SelectNext()
        {
            if (Member.MoveWorld.sqrMagnitude == 0f) return Member.Idle;
            return Member.SprintHeld ? Member.Run : Member.Walk;
        }
    }
}
