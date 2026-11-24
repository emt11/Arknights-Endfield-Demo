using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace Endfield.Input
{
    [Flags]
    public enum CombatInputCommand
    {
        None = 0,
        Attack = 1,
        HeavyAttack = 2,
        Dodge = 4
    }

    public sealed class CombatInputReader : IDisposable
    {
        private readonly InputActionAsset asset;
        private readonly InputAction move, look, sprint, attack, dodge, pause;
        private CombatInputCommand pending, sampled;
        private bool holdArmed, captureRequested, releaseRequested;

        public bool Captured { get; private set; }
        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool SprintHeld { get; private set; }

        public CombatInputReader(InputActionAsset source)
        {
            asset = Object.Instantiate(source);
            move = asset.FindAction("Gameplay/Move", true);
            look = asset.FindAction("Gameplay/Look", true);
            sprint = asset.FindAction("Gameplay/Sprint", true);
            attack = asset.FindAction("Gameplay/Attack", true);
            dodge = asset.FindAction("Gameplay/Dodge", true);
            pause = asset.FindAction("System/Pause", true);
            attack.started += OnAttackPressed;
            attack.performed += OnAttackHeld;
            attack.canceled += OnAttackReleased;
            dodge.performed += OnDodge;
            pause.performed += OnPause;
        }

        public void Enable()
        {
            SetCapture(true);
            move.actionMap.Enable();
            pause.Enable();
        }

        public void Disable()
        {
            if (!asset.enabled) return;
            ReleaseCapture();
            asset.Disable();
        }

        public void ReleaseCapture() => SetCapture(false);

        private void SetCapture(bool value)
        {
            Captured = value;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
            Move = Look = Vector2.zero;
            SprintHeld = false;
            pending = sampled = CombatInputCommand.None;
            holdArmed = captureRequested = releaseRequested = false;
        }

        private void OnAttackPressed(InputAction.CallbackContext context)
        {
            if (!Captured)
            {
                captureRequested = true;
                return;
            }
            holdArmed = true;
            pending |= CombatInputCommand.Attack;
        }

        private void OnAttackHeld(InputAction.CallbackContext context)
        {
            if (holdArmed) pending |= CombatInputCommand.HeavyAttack;
            holdArmed = false;
        }

        private void OnAttackReleased(InputAction.CallbackContext context) => holdArmed = false;

        private void OnDodge(InputAction.CallbackContext context)
        {
            if (Captured) pending |= CombatInputCommand.Dodge;
        }

        private void OnPause(InputAction.CallbackContext context) => releaseRequested = true;

        public void Sample()
        {
            if (releaseRequested || captureRequested)
            {
                // 同帧释放优先；重新捕获的那次点击只用于回到游戏
                SetCapture(!releaseRequested);
                return;
            }
            if (!Captured) return;
            Move = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
            Look = look.ReadValue<Vector2>();
            SprintHeld = sprint.IsPressed();
            sampled = pending;
            pending = CombatInputCommand.None;
        }

        public CombatInputCommand ConsumeCommands()
        {
            var result = sampled;
            sampled = CombatInputCommand.None;
            return result;
        }

        // 闪避接管时终止原按压手势，要求重新按下才能再次提交重击
        public void CancelAttackHold() => holdArmed = false;

        public void Dispose()
        {
            Disable();
            attack.started -= OnAttackPressed;
            attack.performed -= OnAttackHeld;
            attack.canceled -= OnAttackReleased;
            dodge.performed -= OnDodge;
            pause.performed -= OnPause;
            Object.Destroy(asset);
        }
    }
}
