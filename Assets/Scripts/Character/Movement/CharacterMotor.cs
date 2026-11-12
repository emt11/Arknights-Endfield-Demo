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
            if (deltaTime <= 0f || !controller.enabled) return;
            float rate = targetSpeed > CurrentSpeed ? acceleration : deceleration;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, rate * deltaTime);

            if (targetSpeed > 0f)
            {
                float currentYaw = transform.eulerAngles.y;
                float targetYaw = currentYaw + turnAngleRadians * Mathf.Rad2Deg;
                float yaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref turnVelocity, turnSmoothTime, turnSpeed, deltaTime);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            else
            {
                // 松键后沿当前朝向减速，不保留转向惯性
                turnVelocity = 0f;
            }

            Vector3 planarDelta = transform.forward * (CurrentSpeed * deltaTime);
            ApplyMovement(planarDelta, deltaTime);
        }

        // 所有参数都是本帧位移；本方法不再次乘水平速度或 deltaTime
        public void ApplyMovement(Vector3 planarDelta, float deltaTime)
        {
            if (deltaTime <= 0f || !controller.enabled) return;
            planarDelta.y = 0f;
            RequestedPlanarDelta = planarDelta;
            // 处理垂直速度
            if (controller.isGrounded && verticalSpeed < 0f)
                verticalSpeed = groundStickSpeed;
            else
                verticalSpeed += gravity * deltaTime;

            Vector3 before = transform.position;
            // 合并水平位移和垂直位移
            Vector3 displacement = planarDelta + Vector3.up * (verticalSpeed * deltaTime);
            CollisionFlags flags = controller.Move(displacement); // 移动
            // 落地后，恢复较小的向下速度，避免持续累积下落速度
            IsGrounded = (flags & CollisionFlags.Below) != 0;
            if (IsGrounded && verticalSpeed < 0f) verticalSpeed = groundStickSpeed;
            // 向上运动撞到顶部时，清除向上速度
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;

            // 记录实际位移和实际速度
            Vector3 actual = transform.position - before;
            actual.y = 0f;
            ActualPlanarDelta = actual;
            ActualSpeed = actual.magnitude / deltaTime;
        }

        public void ResetMotion()
        {
            CurrentSpeed = 0f;
            verticalSpeed = groundStickSpeed;
            turnVelocity = 0f;
            IsGrounded = false;
            RequestedPlanarDelta = ActualPlanarDelta = Vector3.zero;
            ActualSpeed = 0f;
        }
    }
}
