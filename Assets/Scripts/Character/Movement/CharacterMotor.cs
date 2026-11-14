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
        private CharacterController controller;
        private float verticalSpeed;
        private float turnVelocity;

        public float CurrentSpeed { get; private set; }
        public bool IsGrounded { get; private set; }
        public Vector3 RequestedPlanarDelta { get; private set; }
        public Vector3 ActualPlanarDelta { get; private set; }
        public float ActualSpeed { get; private set; }

        private void Awake() => controller = GetComponent<CharacterController>();

        public void Tick(float targetSpeed, float turnAngleRadians, float deltaTime)
        {
            if (deltaTime <= 0f || !controller.enabled)
                return;

            float speedChangeRate = targetSpeed > CurrentSpeed ? acceleration : deceleration;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, speedChangeRate * deltaTime);
            UpdateRotation(targetSpeed, turnAngleRadians, deltaTime);

            Vector3 planarDelta = transform.forward * (CurrentSpeed * deltaTime);
            ApplyMovement(planarDelta, deltaTime);
        }

        private void UpdateRotation(float targetSpeed, float turnAngleRadians, float deltaTime)
        {
            if (targetSpeed > 0f)
            {
                float currentYaw = transform.eulerAngles.y;
                float targetYaw = currentYaw + turnAngleRadians * Mathf.Rad2Deg;
                float nextYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref turnVelocity, turnSmoothTime, turnSpeed, deltaTime);
                transform.rotation = Quaternion.Euler(0f, nextYaw, 0f);
            }
            else
                // 松键后沿当前朝向减速，不保留转向惯性
                turnVelocity = 0f;
        }

        // planarDelta 已是本帧水平位移，不再次乘速度或 deltaTime
        public void ApplyMovement(Vector3 planarDelta, float deltaTime)
        {
            if (deltaTime <= 0f || !controller.enabled)
                return;

            planarDelta.y = 0f;
            RequestedPlanarDelta = planarDelta;
            UpdateVerticalSpeed(deltaTime);

            Vector3 previousPosition = transform.position;
            Vector3 displacement = planarDelta + Vector3.up * (verticalSpeed * deltaTime);
            CollisionFlags flags = controller.Move(displacement);
            UpdateCollisionState(flags);
            RecordMovement(previousPosition, deltaTime);
        }

        private void UpdateVerticalSpeed(float deltaTime)
        {
            if (controller.isGrounded && verticalSpeed < 0f)
                verticalSpeed = groundStickSpeed;
            else
                verticalSpeed += gravity * deltaTime;
        }

        private void UpdateCollisionState(CollisionFlags flags)
        {
            // 落地后，恢复较小的向下速度，避免持续累积下落速度
            IsGrounded = (flags & CollisionFlags.Below) != 0;
            if (IsGrounded && verticalSpeed < 0f)
                verticalSpeed = groundStickSpeed;

            // 向上运动撞到顶部时，清除向上速度
            bool hitCeiling = (flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f;
            if (hitCeiling)
                verticalSpeed = 0f;
        }

        private void RecordMovement(Vector3 previousPosition, float deltaTime)
        {
            Vector3 actualDelta = transform.position - previousPosition;
            actualDelta.y = 0f;
            ActualPlanarDelta = actualDelta;
            ActualSpeed = actualDelta.magnitude / deltaTime;
        }

        public void ResetMotion()
        {
            CurrentSpeed = 0f;
            verticalSpeed = groundStickSpeed;
            turnVelocity = 0f;
            IsGrounded = false;
            RequestedPlanarDelta = Vector3.zero;
            ActualPlanarDelta = Vector3.zero;
            ActualSpeed = 0f;
        }
    }
}
