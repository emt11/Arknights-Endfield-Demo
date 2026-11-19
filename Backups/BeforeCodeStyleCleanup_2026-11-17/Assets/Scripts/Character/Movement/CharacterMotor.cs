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
        private LocomotionStateMachine stateMachine;
        private Vector3 moveIntent;
        private bool runHeld;
        private float targetSpeed;
        private float verticalSpeed;
        private float turnVelocity;

        public float CurrentSpeed { get; private set; }
        public bool IsGrounded { get; private set; }
        public Vector3 RequestedPlanarDelta { get; private set; }
        public Vector3 ActualPlanarDelta { get; private set; }
        public float ActualSpeed { get; private set; }
        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public float TurnAngle { get; private set; }
        public Vector3 PlannedDelta { get; private set; }
        public CollisionFlags Collisions { get; private set; }
        public int MovementSubmissions { get; private set; }
        public LocomotionStateId CurrentState => stateMachine.Current.Id;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            stateMachine = new LocomotionStateMachine(this);
        }

        private void OnEnable() => stateMachine.Start();

        private void OnDisable()
        {
            stateMachine.Stop();
            ResetMotion();
        }

        public void SetIntent(Vector3 direction, bool running)
        {
            moveIntent = direction;
            runHeld = running;
        }

        public void SetTargetSpeed(float value) => targetSpeed = value;

        public void ApplyRotation(Quaternion rotation) => transform.rotation = rotation;

        /// <summary>
        /// 更新速度、旋转，计算 PlannedDelta
        /// </summary>
        /// <param name="deltaTime"></param>
        /// <param name="allowInput"></param>
        public void PlanLocomotion(float deltaTime, bool allowInput)
        {
            bool moving = allowInput && moveIntent.sqrMagnitude > 0f;
            stateMachine.Tick(moving, runHeld);
            Vector3 local = transform.InverseTransformDirection(moveIntent);
            TurnAngle = moving ? Mathf.Atan2(local.x, local.z) : 0f; // 计算移动方向相对于角色正前方的旋转角度
            float speedChangeRate = targetSpeed > CurrentSpeed ? acceleration : deceleration;
            CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, targetSpeed, speedChangeRate * deltaTime);
            UpdateRotation(targetSpeed, TurnAngle, deltaTime); // 应用旋转
            PlannedDelta = transform.forward * (CurrentSpeed * deltaTime);
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

        // planarDelta 已是本步水平位移，不再次乘速度或 deltaTime
        public void ApplyMovement(Vector3 planarDelta, float deltaTime)
        {
            if (deltaTime <= 0f || !controller.enabled)
                return;

            planarDelta.y = 0f;
            RequestedPlanarDelta = planarDelta;
            UpdateVerticalSpeed(deltaTime);

            Vector3 previousPosition = transform.position;
            Vector3 displacement = planarDelta + Vector3.up * (verticalSpeed * deltaTime);
            CollisionFlags flags = controller.Move(displacement); // 应用位移
            Collisions = flags;
            MovementSubmissions++;
            UpdateCollisionState(flags); // 处理碰撞后的垂直速度
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
            PlannedDelta = Vector3.zero;
            moveIntent = Vector3.zero;
            targetSpeed = 0f;
            TurnAngle = 0f;
        }
    }
}
