using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

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
        [SerializeField, Range(0f, 0.9f)] private float deadZone = 0.1f;
        [SerializeField] private InputSystemUIInputModule uiInputModule;

        private EndfieldInputActions actions;
        private ValueInput<Vector2> moveInput;
        private ValueInput<Vector2> lookInput;
        private ButtonInput runInput;
        private ButtonInput releaseCursorInput;
        private ButtonInput captureCursorInput;
        private ButtonInput toggleCharacterPanelInput;
        private InputBase[] gameplayInputs;
        private InputBase[] controlInputs;
        private InputActionReference uiPointReference;
        private InputActionReference uiClickReference;
        private bool characterPanelOpen;
        private bool restoreCaptureOnClose; // 关闭面板时，是否应该恢复鼠标捕获和角色控制
        private int suppressGameplayFrame = -1;

        public GameplayInputFrame Frame { get; private set; }
        public bool GameplayCaptured { get; private set; }
        public event Action CharacterPanelToggleRequested;
        public event Action CharacterPanelCloseRequested;

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
            toggleCharacterPanelInput = new ButtonInput(actions.Control.ToggleCharacterPanel);
            gameplayInputs = new InputBase[] { moveInput, lookInput, runInput };
            controlInputs = new InputBase[]
            {
                releaseCursorInput, captureCursorInput, toggleCharacterPanelInput
            };

            // 先解除编辑器配置，再绑定同一份运行时资产
            uiInputModule.enabled = false;
            uiInputModule.UnassignActions();
            uiInputModule.actionsAsset = actions.asset;
            uiPointReference = InputActionReference.Create(actions.UI.Point);
            uiClickReference = InputActionReference.Create(actions.UI.Click);
            uiInputModule.point = uiPointReference;
            uiInputModule.leftClick = uiClickReference;
        }

        private void OnEnable()
        {
            foreach (InputBase input in controlInputs) input.Enable();
            uiInputModule.enabled = true;
            SetCaptured(false);
        }

        private void Update()
        {
            Frame = default;
            if (!Application.isFocused) // Game 视图是否拥有焦点，即是否正在当前的游戏窗口而不是其他程序窗口
            {
                SetCaptured(false);
                return;
            }

            // Esc 优先处理当前面板，不同时执行普通释放逻辑
            if (releaseCursorInput.WasPressedThisFrame)
            {
                if (characterPanelOpen)
                    CharacterPanelCloseRequested?.Invoke(); // 通知 UI 关闭
                else
                    SetCaptured(false);
                return;
            }

            if (toggleCharacterPanelInput.WasPressedThisFrame)
            {
                CharacterPanelToggleRequested?.Invoke(); // 通知 UI 开关
                return;
            }

            // 角色面板打开期间或在开关面板当帧，跳过后续玩法输入处理
            if (characterPanelOpen || suppressGameplayFrame == Time.frameCount)
                return;

            if (GameplayCaptured && Cursor.lockState != CursorLockMode.Locked)
            {
                SetCaptured(false);
                return;
            }

            if (!GameplayCaptured)
            {
                if (captureCursorInput.WasPressedThisFrame) SetCaptured(true);
                return;
            }

            Vector2 move = moveInput.ReadValue();
            move = move.sqrMagnitude > deadZone * deadZone ? move.normalized : Vector2.zero;
            Frame = new GameplayInputFrame(move, lookInput.ReadValue(), runInput.IsPressed);
        }

        public void SetCharacterPanelOpen(bool open)
        {
            if (characterPanelOpen == open) return;
            characterPanelOpen = open;
            suppressGameplayFrame = Time.frameCount;

            if (open)
            {
                restoreCaptureOnClose = GameplayCaptured;
                SetCaptured(false);
            }
            else
            {
                SetCaptured(restoreCaptureOnClose && Application.isFocused && isActiveAndEnabled);
                restoreCaptureOnClose = false;
            }
        }

        /// <summary>
        /// 清空当前帧玩法输入，设置鼠标锁定与可见状态，并启用或禁用角色输入
        /// </summary>
        /// <param name="captured">true 表示锁定并隐藏鼠标，启用角色输入；false 表示释放并显示鼠标，禁用角色输入</param>
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
            if (focused) return;
            restoreCaptureOnClose = false;
            SetCaptured(false);
        }

        private void OnDisable()
        {
            CharacterPanelCloseRequested?.Invoke();
            SetCharacterPanelOpen(false);
            SetCaptured(false);
            foreach (InputBase input in controlInputs) input.Disable();
            if (uiInputModule != null) uiInputModule.enabled = false;
        }

        private void OnDestroy()
        {
            if (uiInputModule != null)
            {
                uiInputModule.enabled = false;
                uiInputModule.UnassignActions();
            }

            Destroy(uiPointReference);
            Destroy(uiClickReference);
            foreach (InputBase input in gameplayInputs) input.Dispose();
            foreach (InputBase input in controlInputs) input.Dispose();
            actions.Dispose();
        }
    }
}
