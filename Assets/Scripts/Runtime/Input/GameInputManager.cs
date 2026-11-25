using System;
using Endfield.CharacterControl;
using Endfield.Controller;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Endfield.Input
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-300)]
    public sealed class GameInputManager : MonoBehaviour
    {
        private EndfieldInputActions actions;
        private InputAction move, look, sprint, attack, dodge, pause, togglePanel;
        private CharacterCommand pending;
        private bool captureRequested, releaseRequested, toggleRequested, panelOpen;
        private int captureFrame;

        public event Action PanelToggleRequested;
        public event Action PanelCloseRequested;
        public event Action GameplayInputCleared;
        public InputDataFrame Frame { get; private set; }
        public bool Captured { get; private set; }

        private void Awake()
        {
            actions = new EndfieldInputActions();
            var gameplay = actions.Gameplay;
            move = gameplay.Move;
            look = gameplay.Look;
            sprint = gameplay.Sprint;
            attack = gameplay.Attack;
            dodge = gameplay.Dodge;
            pause = actions.System.Pause;
            togglePanel = actions.Control.ToggleCharacterPanel;

            // 重新启用 Gameplay 输入后识别已按住的冲刺键，例如关闭面板时仍按着 Shift
            sprint.wantsInitialStateCheck = true;
            attack.performed += OnAttack;
            dodge.performed += OnDodge;
            pause.performed += OnPause;
            togglePanel.performed += OnTogglePanel;
        }

        private void OnEnable()
        {
            pause.Enable();
            togglePanel.Enable();
            ApplyPanelState();
        }

        public void SetPanelOpen(bool open)
        {
            if (panelOpen == open) return;
            panelOpen = open;
            if (isActiveAndEnabled) ApplyPanelState();
        }

        private void ApplyPanelState()
        {
            if (panelOpen) actions.Gameplay.Disable();
            else actions.Gameplay.Enable();
            SetCapture(!panelOpen);
        }

        private void OnDisable()
        {
            actions.Disable();
            ReleaseCapture();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && isActiveAndEnabled) ReleaseCapture();
        }

        public void ReleaseCapture() => SetCapture(false);

        private void SetCapture(bool value)
        {
            Captured = value;
            if (value) captureFrame = Time.frameCount;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
            captureRequested = releaseRequested = toggleRequested = false;
            pending = CharacterCommand.None;
            Frame = default;
            GameplayInputCleared?.Invoke(); // 通知 PlayerController 立即清空角色已收到的操控输入
        }

        // 光标自由时，第一次点击只恢复操控，后续点击才触发普攻
        private void OnAttack(InputAction.CallbackContext context)
        {
            if (Captured || captureRequested)
                pending |= CharacterCommand.Attack;
            else
                captureRequested = true;
        }

        private void OnDodge(InputAction.CallbackContext context)
        {
            if (Captured || captureRequested) pending |= CharacterCommand.Dodge;
        }

        private void OnPause(InputAction.CallbackContext context) => releaseRequested = true;

        private void OnTogglePanel(InputAction.CallbackContext context) => toggleRequested = true;

        private void Update()
        {
            Frame = default;
            // 先处理角色面板
            if (toggleRequested || (panelOpen && releaseRequested))
            {
                bool close = panelOpen && releaseRequested;
                toggleRequested = releaseRequested = false;
                pending = CharacterCommand.None;
                if (close) PanelCloseRequested?.Invoke();
                else PanelToggleRequested?.Invoke();
                return;
            }
            if (releaseRequested || captureRequested)
            {
                // 同帧按 Esc 时优先释放，重新捕获的点击只用于回到游戏
                var afterCapture = releaseRequested ? CharacterCommand.None : pending;
                SetCapture(!releaseRequested);
                Frame = new InputDataFrame(default, default, false, afterCapture);
                return;
            }
            if (!Captured) return;
            // 光标归中的鼠标增量可能在锁定后的第 2 个渲染帧才到达
            Vector2 lookDelta = Time.frameCount - captureFrame <= 2 ? Vector2.zero : look.ReadValue<Vector2>();
            Frame = new InputDataFrame(Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f),
                lookDelta, sprint.IsPressed(), pending);
            pending = CharacterCommand.None;
        }

        public void ClearPendingPresses()
        {
            pending = CharacterCommand.None;
            Frame = new InputDataFrame(Frame.Move, Frame.LookDelta, Frame.SprintHeld, CharacterCommand.None);
        }

        private void OnDestroy()
        {
            attack.performed -= OnAttack;
            dodge.performed -= OnDodge;
            pause.performed -= OnPause;
            togglePanel.performed -= OnTogglePanel;
            actions.Dispose();
        }
    }
}
