namespace Endfield.CharacterControl.FSM
{
    public sealed class PartyMemberIdleState : PartyMemberGroundedState
    {
        public PartyMemberIdleState(PartyMemberControl member) : base(member) { }

        public override void Tick(double delta) => Member.StepGroundMovement(0f, delta);
    }
}
