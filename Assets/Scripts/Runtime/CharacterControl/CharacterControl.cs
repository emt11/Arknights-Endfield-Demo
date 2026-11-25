using Endfield.CharacterControl.FSM;
using Endfield.CharacterMotion;
using UnityEngine;

namespace Endfield.CharacterControl
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterMotor))]
    public abstract class CharacterControl : MonoBehaviour
    {
        private CharacterCommand pendingPressed;
        internal StateMachine Machine { get; } = new();
        internal CharacterMotor Motor { get; private set; }
        public CharacterState CurrentState => Machine.CurrentState;
        public Vector3 MoveWorld { get; private set; }
        public bool SprintHeld { get; private set; }
        public CharacterCommand StepCommands { get; private set; }
        public float GroundSpeed { get; internal set; }

        protected virtual void Awake() => Motor = GetComponent<CharacterMotor>();

        public void Initialize() => Machine.Initialize(CreateInitialState());

        protected abstract CharacterState CreateInitialState();

        public void SetControlInput(Vector3 moveWorld, bool sprintHeld, CharacterCommand pressed)
        {
            moveWorld.y = 0f;
            MoveWorld = Vector3.ClampMagnitude(moveWorld, 1f);
            SprintHeld = sprintHeld;
            pendingPressed = pressed;
        }

        internal void ClearControlInput()
        {
            MoveWorld = Vector3.zero;
            SprintHeld = false;
            DiscardPresses();
        }

        internal void DiscardPresses() => StepCommands = pendingPressed = CharacterCommand.None;

        public void Step(double actionDelta)
        {
            StepCommands = pendingPressed;
            pendingPressed = CharacterCommand.None;
            Machine.ChangeState(CurrentState.SelectNext());
            Machine.Tick(actionDelta);
        }
    }
}
