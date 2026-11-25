using System;

namespace Endfield.CharacterControl.FSM
{
    public sealed class StateMachine
    {
        private bool changing;
        public CharacterState CurrentState { get; private set; }

        public void Initialize(CharacterState initial)
        {
            if (CurrentState != null) throw new InvalidOperationException("状态机已初始化");
            ChangeState(initial);
        }

        public void ChangeState(CharacterState next)
        {
            if (changing) throw new InvalidOperationException("不能在 Enter 或 Exit 中再次切换状态");
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (ReferenceEquals(CurrentState, next)) return;

            changing = true;
            try
            {
                CurrentState?.Exit();
                CurrentState = next;
                next.Enter();
            }
            finally
            {
                changing = false;
            }
        }

        public void Tick(double delta) => CurrentState.Tick(delta);
    }
}
