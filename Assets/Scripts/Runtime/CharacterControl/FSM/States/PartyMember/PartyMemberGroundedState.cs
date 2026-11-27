namespace Endfield.CharacterControl.FSM
{
    public abstract class PartyMemberGroundedState : CharacterState
    {
        protected PartyMemberControl Member => (PartyMemberControl)Control;

        protected PartyMemberGroundedState(PartyMemberControl member) : base(member) { }

        public override CharacterState SelectNext()
        {
            if ((Member.StepCommands & CharacterCommand.Dodge) != 0 && Member.CanDodge) return Member.Dodge;
            return SelectGait(Member);
        }

        internal static CharacterState SelectGait(PartyMemberControl member)
        {
            if (member.MoveWorld.sqrMagnitude == 0f) return member.Idle;
            return member.SprintHeld ? member.Run : member.Walk;
        }
    }
}
