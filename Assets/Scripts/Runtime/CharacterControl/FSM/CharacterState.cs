namespace Endfield.CharacterControl.FSM
{
    public abstract class CharacterState
    {
        protected CharacterControl Control { get; }

        protected CharacterState(CharacterControl control) => Control = control;

        public virtual CharacterState SelectNext() => this;

        public virtual void Enter() { }

        public abstract void Tick(double delta);

        public virtual void Interrupt() { }

        public virtual void Exit() { }

        protected void ChangeState(CharacterState next) => Control.Machine.ChangeState(next);
    }
}
