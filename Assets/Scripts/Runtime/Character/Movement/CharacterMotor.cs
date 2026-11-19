using System;
using UnityEngine;

namespace Endfield.Controller
{
    /// <summary>
    /// 处理角色实际位移和转向
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class CharacterMotor : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float acceleration = 12f;
        [SerializeField, Min(0.01f)] private float deceleration = 18f;
        [SerializeField, Min(0f)] private float turnSpeed = 720f;
        [SerializeField, Min(0.01f)] private float turnSmoothTime = 0.12f;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float groundStickSpeed = -2f;
        [SerializeField, Min(0.01f)] private float walkSpeed = 2f;
        [SerializeField, Min(0.01f)] private float runSpeed = 5f;
        private CharacterController controller;
        private Vector3 moveIntent;
        private bool runHeld;
        private float verticalSpeed;
        private float turnVelocity;

        // 走跑速度用于动画与候选位移，碰撞后的速度见 ActualSpeed
        public float LocomotionSpeed { get; private set; }
        public Vector3 LocomotionDelta { get; private set; }
        public bool IsGrounded { get; private set; }
        public Vector3 RequestedPlanarDelta { get; private set; }
        public Vector3 ActualPlanarDelta { get; private set; }
        public float ActualSpeed { get; private set; }
        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public CollisionFlags Collisions { get; private set; }
        public int MovementSubmissions { get; private set; }
        public LocomotionStateId CurrentState { get; private set; }
        internal event Action Disabled;

        private void Awake() => controller = GetComponent<CharacterController>();

        private void OnEnable() => ResetMotion();

        private void OnDisable()
        {
            Disabled?.Invoke();
            ResetMotion();
        }

        public void SetIntent(Vector3 direction, bool running)
        {
            moveIntent = direction;
            runHeld = running;
        }

        public void ApplyRotation(Quaternion rotation) => transform.rotation = rotation;

        /// <summary>
        /// 更新普通走跑的速度和朝向，动作可以接管本步水平位移
        /// </summary>
        public void UpdateLocomotion(float deltaTime, bool allowInput)
        {
            bool moving = allowInput && moveIntent.sqrMagnitude > 0f;
            if (!moving)
                CurrentState = LocomotionStateId.Idle;
            else if (runHeld)
                CurrentState = LocomotionStateId.Run;
            else
                CurrentState = LocomotionStateId.Walk;
            float targetSpeed = CurrentState switch
            {
                LocomotionStateId.Walk => walkSpeed,
                LocomotionStateId.Run => runSpeed,
                _ => 0f
            };
            float speedChangeRate = targetSpeed > LocomotionSpeed ? acceleration : deceleration;
            LocomotionSpeed = Mathf.MoveTowards(LocomotionSpeed, targetSpeed, speedChangeRate * deltaTime);

            if (moving)
            {
                Vector3 local = transform.InverseTransformDirection(moveIntent);
                float turnAngle = Mathf.Atan2(local.x, local.z);
                float currentYaw = transform.eulerAngles.y;
                float targetYaw = currentYaw + turnAngle * Mathf.Rad2Deg;
                float nextYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref turnVelocity, turnSmoothTime, turnSpeed, deltaTime);
                transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
            }
            else
            {
                // 松键后沿当前朝向减速，不保留转向惯性
                turnVelocity = 0f;
            }

            LocomotionDelta = transform.forward * (LocomotionSpeed * deltaTime);
        }

        // planarDelta 已是本步水平位移，不再次乘速度或 deltaTime
        public void ApplyMovement(Vector3 planarDelta, float deltaTime)
        {
            if (deltaTime <= 0f || !controller.enabled)
                return;

            planarDelta.y = 0f;
            RequestedPlanarDelta = planarDelta;
            if (controller.isGrounded && verticalSpeed < 0f)
                verticalSpeed = groundStickSpeed;
            else
                verticalSpeed += gravity * deltaTime;

            Vector3 previousPosition = transform.position;
            Vector3 displacement = planarDelta + Vector3.up * (verticalSpeed * deltaTime);
            Collisions = controller.Move(displacement);
            MovementSubmissions++;

            // 落地后，恢复较小的向下速度，避免持续累积下落速度
            IsGrounded = (Collisions & CollisionFlags.Below) != 0;
            if (IsGrounded && verticalSpeed < 0f)
                verticalSpeed = groundStickSpeed;

            // 向上运动撞到顶部时，清除向上速度
            bool hitCeiling = (Collisions & CollisionFlags.Above) != 0 && verticalSpeed > 0f;
            if (hitCeiling)
                verticalSpeed = 0f;

            Vector3 actualDelta = transform.position - previousPosition;
            actualDelta.y = 0f;
            ActualPlanarDelta = actualDelta;
            ActualSpeed = actualDelta.magnitude / deltaTime;
        }

        public void ResetMotion()
        {
            LocomotionSpeed = 0f;
            verticalSpeed = groundStickSpeed;
            turnVelocity = 0f;
            IsGrounded = false;
            RequestedPlanarDelta = Vector3.zero;
            ActualPlanarDelta = Vector3.zero;
            ActualSpeed = 0f;
            LocomotionDelta = Vector3.zero;
            moveIntent = Vector3.zero;
            runHeld = false;
            CurrentState = LocomotionStateId.Idle;
        }
    }
}
