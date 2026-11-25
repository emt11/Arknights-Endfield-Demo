namespace Endfield.CharacterControl.FSM
{
    public sealed class PartyMemberRunState : PartyMemberGroundedState
    {
        public PartyMemberRunState(PartyMemberControl member) : base(member) { }

        public override void Tick(double delta) => Member.StepGroundMovement(Member.RunSpeed, delta);
    }
}
