namespace Endfield.CharacterControl.FSM
{
    public enum GroundGait { Idle, Walk, Run }

    public sealed class PartyMemberGroundedState : CharacterState
    {
        private readonly PartyMemberControl member;
        public GroundGait Gait { get; private set; }

        internal PartyMemberGroundedState(PartyMemberControl member) => this.member = member;

        public override void Enter() => Gait = ReadGait();

        public override void Tick(double delta)
        {
            Gait = ReadGait();
            float speed = Gait switch
            {
                GroundGait.Walk => member.WalkSpeed,
                GroundGait.Run => member.RunSpeed,
                _ => 0
            };
            member.GroundSpeed = member.Motor.StepLocomotion(member.MoveWorld, speed, member.TurnSpeed, (float)delta);
            member.AdvanceLocomotion(delta);
        }

        private GroundGait ReadGait()
        {
            if (member.MoveWorld.sqrMagnitude == 0) return GroundGait.Idle;
            return member.SprintHeld ? GroundGait.Run : GroundGait.Walk;
        }
    }
}
