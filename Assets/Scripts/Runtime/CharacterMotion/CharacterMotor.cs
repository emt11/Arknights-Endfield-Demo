using UnityEngine;

namespace Endfield.CharacterMotion
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class CharacterMotor : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float acceleration = 12f;
        [SerializeField, Min(0.01f)] private float deceleration = 18f;
        [SerializeField, Min(0.01f)] private float turnSmoothTime = 0.12f;
        private CharacterController capsule;
        private float locomotionSpeed;
        private float turnVelocity;

        private void Awake() => capsule = GetComponent<CharacterController>();

        private void OnDisable()
        {
            locomotionSpeed = 0f;
            turnVelocity = 0f;
        }

        // 推进一次地面步态，按当前速度加减速并平滑转身，再沿角色朝向执行水平移动
        public float StepLocomotion(Vector3 direction, float gaitSpeed, float turnSpeed, float deltaTime)
        {
            float targetSpeed = gaitSpeed * direction.magnitude;
            float rate = targetSpeed > locomotionSpeed ? acceleration : deceleration;
            locomotionSpeed = Mathf.MoveTowards(locomotionSpeed, targetSpeed, rate * deltaTime);

            if (direction.sqrMagnitude > 0f)
            {
                float targetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw, ref turnVelocity,
                    turnSmoothTime, turnSpeed, deltaTime);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            else
            {
                turnVelocity = 0f;
            }

            Vector3 actual = ApplyPlanarDisplacement(transform.forward * (locomotionSpeed * deltaTime));
            return actual.magnitude / deltaTime;
        }

        // 对齐本段最终偏航，由 CharacterController 落实水平位移
        public void MovePlanar(Vector3 worldDelta, Quaternion worldYaw)
        {
            transform.rotation = worldYaw;
            ApplyPlanarDisplacement(worldDelta);
        }

        private Vector3 ApplyPlanarDisplacement(Vector3 delta)
        {
            delta.y = 0;
            Vector3 before = transform.position;
            if (delta.sqrMagnitude > 0) capsule.Move(delta);
            Vector3 actual = transform.position - before;
            actual.y = 0;
            return actual;
        }
    }
}
