using UnityEngine;
using UnityEngine.InputSystem;

namespace Endfield.Controller
{
    public readonly struct GameplayInputFrame
    {
        public readonly Vector2 Move;
        public readonly Vector2 Look;
        public readonly bool RunHeld;

        public GameplayInputFrame(Vector2 move, Vector2 look, bool runHeld)
        {
            Move = move;
            Look = look;
            RunHeld = runHeld;
        }
    }

    [DefaultExecutionOrder(-100), DisallowMultipleComponent]
    public sealed class GameInputManager : MonoBehaviour
    {
        private EndfieldInputActions actions;
        private ValueInput<Vector2> moveInput;
        private ValueInput<Vector2> lookInput;
        private ButtonInput runInput;
        private ButtonInput releaseCursorInput;
        private ButtonInput captureCursorInput;
        private InputBase[] gameplayInputs;
        private InputBase[] controlInputs;

        public GameplayInputFrame Frame { get; private set; }
        public bool GameplayCaptured { get; private set; }

        [SerializeField, Range(0f, 0.9f)] private float deadZone = 0.1f;

        private void Awake()
        {
            actions = new EndfieldInputActions();
            actions.bindingMask = InputBinding.MaskByGroup("Keyboard&Mouse");
            actions.Player.Run.wantsInitialStateCheck = true;
            moveInput = new ValueInput<Vector2>(actions.Player.Move);
            lookInput = new ValueInput<Vector2>(actions.Player.Look);
            runInput = new ButtonInput(actions.Player.Run);
            releaseCursorInput = new ButtonInput(actions.Control.ReleaseCursor);
            captureCursorInput = new ButtonInput(actions.Control.CaptureCursor);
            gameplayInputs = new InputBase[] { moveInput, lookInput, runInput };
            controlInputs = new InputBase[] { releaseCursorInput, captureCursorInput };
        }

        private void OnEnable()
        {
            foreach (InputBase input in controlInputs) input.Enable();
            SetCaptured(false);
        }

        private void Update()
        {
            Frame = default;
            if (!Application.isFocused || releaseCursorInput.WasPressedThisFrame)
            {
                SetCaptured(false);
                return;
            }
            if (GameplayCaptured && Cursor.lockState != CursorLockMode.Locked)
            {
                SetCaptured(false);
                return;
            }
            if (!GameplayCaptured)
            {
                if (captureCursorInput.WasPressedThisFrame) SetCaptured(true);
                return; // 捕获当帧不移动、不转镜头；未来也不能当作攻击
            }

            Vector2 move = moveInput.ReadValue();
            move = move.sqrMagnitude > deadZone * deadZone ? move.normalized : Vector2.zero;
            Frame = new GameplayInputFrame(move, lookInput.ReadValue(), runInput.IsPressed);
        }

        private void SetCaptured(bool captured)
        {
            Frame = default;
            GameplayCaptured = captured;
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
            foreach (InputBase input in gameplayInputs)
            {
                if (captured) input.Enable();
                else input.Disable();
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) SetCaptured(false);
        }

        private void OnDisable()
        {
            SetCaptured(false);
            foreach (InputBase input in controlInputs) input.Disable();
        }

        private void OnDestroy()
        {
            foreach (InputBase input in gameplayInputs) input.Dispose();
            foreach (InputBase input in controlInputs) input.Dispose();
            actions.Dispose();
        }
    }
}
