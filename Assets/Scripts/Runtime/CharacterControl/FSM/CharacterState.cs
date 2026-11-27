namespace Endfield.CharacterControl.FSM
{
    public abstract class CharacterState
    {
        public virtual void Enter() { }

        public virtual void Tick(double delta) { }

        public virtual void Exit() { }
    }
}
