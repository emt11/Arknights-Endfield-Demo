namespace Endfield.Controller
{
    public enum LocomotionStateId
    {
        Idle,
        Walk,
        Run
    }

    public abstract class LocomotionState
    {
        protected readonly PlayerController Owner;

        public abstract LocomotionStateId Id { get; }
        protected abstract float TargetSpeed { get; }

        protected LocomotionState(PlayerController owner) => Owner = owner;

        public virtual void Enter()
        {
        }

        public void Tick() => Owner.SetTargetSpeed(TargetSpeed);

        public virtual void Exit()
        {
        }
    }

    public sealed class IdleState : LocomotionState
    {
        public override LocomotionStateId Id => LocomotionStateId.Idle;
        protected override float TargetSpeed => 0f;

        public IdleState(PlayerController owner) : base(owner)
        {
        }
    }

    public sealed class WalkState : LocomotionState
    {
        public override LocomotionStateId Id => LocomotionStateId.Walk;
        protected override float TargetSpeed => Owner.WalkSpeed;

        public WalkState(PlayerController owner) : base(owner)
        {
        }
    }

    public sealed class RunState : LocomotionState
    {
        public override LocomotionStateId Id => LocomotionStateId.Run;
        protected override float TargetSpeed => Owner.RunSpeed;

        public RunState(PlayerController owner) : base(owner)
        {
        }
    }

    /// <summary>
    /// 选择当前移动状态，并由状态输出目标速度
    /// </summary>
    public sealed class LocomotionStateMachine
    {
        private readonly IdleState idle;
        private readonly WalkState walk;
        private readonly RunState run;

        public LocomotionState Current { get; private set; }

        public LocomotionStateMachine(PlayerController owner)
        {
            idle = new IdleState(owner);
            walk = new WalkState(owner);
            run = new RunState(owner);
        }

        public void Start()
        {
            Stop();
            ChangeState(idle);
        }

        public void Tick(bool hasMove, bool runHeld)
        {
            LocomotionState next;
            if (!hasMove)
                next = idle;
            else if (runHeld)
                next = run;
            else
                next = walk;

            ChangeState(next);
            Current.Tick();
        }

        private void ChangeState(LocomotionState next)
        {
            if (ReferenceEquals(Current, next))
                return;

            Current?.Exit();
            Current = next;
            Current.Enter();
        }

        public void Stop()
        {
            Current?.Exit();
            Current = null;
        }
    }
}
