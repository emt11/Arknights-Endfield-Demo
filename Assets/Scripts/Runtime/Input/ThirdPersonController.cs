using System;
using Endfield.Animation;
using Endfield.CharacterMotion;
using Endfield.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Endfield.Controller
{
    [DefaultExecutionOrder(-250)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterMotor))]
    public sealed class ThirdPersonController : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private ThirdPersonCamera view;
        [SerializeField] private AnimationClip attackClip;
        [SerializeField] private AnimationClip heavyAttackClip;
        [SerializeField, Min(0f)] private float walkSpeed = 1.43936f;
        [SerializeField, Min(0f)] private float runSpeed = 4.91111f;
        [SerializeField, Min(0f)] private float turnSpeed = 720f;
        [SerializeField, Min(0f)] private float dodgeDistance = 2f;
        [SerializeField, Min(.001f)] private float dodgeTravelTime = .2f;
        private CombatInputReader input;
        private CharacterMotor motor;
        private Vector3 dodgeDirection;
        private double travelRemaining;

        private void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            input = new CombatInputReader(actions);
        }

        private void Start() => view.Initialize(transform);

        private void OnEnable() => input.Enable();

        private void Update()
        {
            input.Sample();
            view.ApplyLook(input.Look);
        }

        public void Step(ICharacterAnimationDriver driver, double actionDelta)
        {
            var commands = input.ConsumeCommands();
            if (actionDelta <= 0) return;

            Vector3 movement = view.PlanarRotation * new Vector3(input.Move.x, 0, input.Move.y);
            bool startDodge = (commands & CombatInputCommand.Dodge) != 0 && !driver.IsDodging;
            if (startDodge)
            {
                dodgeDirection = movement.sqrMagnitude > 0 ? movement.normalized : transform.forward;
                travelRemaining = dodgeTravelTime;
                driver.PlayDodge(.080f);
            }
            if (startDodge || driver.IsDodging)
            {
                input.CancelAttackHold();
                double dt = Math.Min(travelRemaining, actionDelta);
                travelRemaining -= dt;
                motor.MovePlanar(dodgeDirection * (float)(dodgeDistance * dt / dodgeTravelTime), dodgeDirection, 180f);
                driver.SetGroundSpeed(0);
                return;
            }

            if ((commands & CombatInputCommand.HeavyAttack) != 0)
            {
                if (!driver.CurrentClip || driver.CurrentClip == attackClip)
                    driver.TryPlay(heavyAttackClip, 1f, .100f, .080f);
            }
            else if ((commands & CombatInputCommand.Attack) != 0 && !driver.CurrentClip)
                driver.TryPlay(attackClip, 1f, .100f, .080f);
            if (driver.CurrentClip)
            {
                driver.SetGroundSpeed(0);
                return;
            }

            float dtSeconds = (float)actionDelta;
            Vector3 delta = movement * ((input.SprintHeld ? runSpeed : walkSpeed) * dtSeconds);
            Vector3 actual = motor.MovePlanar(delta, movement, turnSpeed * dtSeconds);
            driver.SetGroundSpeed(actual.magnitude / dtSeconds);
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && input.Captured) input.ReleaseCapture();
        }

        private void OnDisable() => input.Disable();

        private void OnDestroy() => input.Dispose();
    }
}
