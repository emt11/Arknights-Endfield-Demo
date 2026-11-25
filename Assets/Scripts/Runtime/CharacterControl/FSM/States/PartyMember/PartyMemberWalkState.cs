namespace Endfield.CharacterControl.FSM
{
    public sealed class PartyMemberWalkState : PartyMemberGroundedState
    {
        public PartyMemberWalkState(PartyMemberControl member) : base(member) { }

        public override void Tick(double delta) => Member.StepGroundMovement(Member.WalkSpeed, delta);
    }
}
