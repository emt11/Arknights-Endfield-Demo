using UnityEngine;

namespace Endfield.Controller
{
    [RequireComponent(typeof(Camera))]
    public sealed class CustomThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private float height = 1.5f;
        [SerializeField] private float sensitivity = 0.1f;
        [SerializeField] private float minPitch = -50f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private float initialPitch = 15f;
        [SerializeField, Min(0.1f)] private float distance = 4f;
        [SerializeField, Min(0.01f)] private float collisionRadius = 0.2f;
        [SerializeField, Min(0f)] private float collisionPadding = 0.03f;
        [SerializeField, Min(0.01f)] private float recoverySpeed = 4f;
        [SerializeField] private LayerMask obstructionMask;
        private Camera cameraComponent;
        private bool snapNextFrame;
        private Transform follow;
        private float yaw;
        private float pitch;

        public float CurrentDistance { get; private set; }
        public float SafeDistance { get; private set; }
        public bool PivotOverlapping { get; private set; }
        public Quaternion PlanarRotation => Quaternion.Euler(0f, yaw, 0f);

        private void Awake() => cameraComponent = GetComponent<Camera>();

        private void OnEnable() => snapNextFrame = true;

        public void Initialize(Transform player)
        {
            follow = player;
            yaw = player.eulerAngles.y;
            pitch = Mathf.Clamp(initialPitch, minPitch, maxPitch);
        }

        public void ApplyLook(Vector2 delta)
        {
            yaw = Mathf.Repeat(yaw + delta.x * sensitivity, 360f);
            pitch = Mathf.Clamp(pitch - delta.y * sensitivity, minPitch, maxPitch);
        }

        private void LateUpdate()
        {
            if (follow == null)
                return;
            Vector3 origin = follow.position + Vector3.up * height;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f); // 相机朝向
            Vector3 backward = rotation * Vector3.back; // 相机朝向的反方向

            UpdateObstruction(origin, backward); // 检测碰撞
            UpdateFollowDistance(Time.deltaTime); // 计算安全距离
            transform.SetPositionAndRotation(origin + backward * CurrentDistance, rotation);
        }

        private void UpdateObstruction(Vector3 origin, Vector3 backward)
        {
            float radius = CalculateCollisionRadius();
            // SphereCast 不处理起点已经重叠的情况，先单独检查
            PivotOverlapping = Physics.CheckSphere(origin, radius, obstructionMask, QueryTriggerInteraction.Ignore);
            SafeDistance = distance;
            if (PivotOverlapping)
                // 最小回退：收至观察点；不宣称能解出观察点本身的穿插
                SafeDistance = 0f;
            else if (Physics.SphereCast(origin, radius, backward, out RaycastHit hit,
                distance, obstructionMask, QueryTriggerInteraction.Ignore))
                SafeDistance = Mathf.Max(0f, hit.distance - collisionPadding);
        }

        private void UpdateFollowDistance(float deltaTime)
        {
            // 收近立即生效，远离逐渐恢复，始终不超过本帧安全距离
            if (snapNextFrame || SafeDistance < CurrentDistance)
                CurrentDistance = SafeDistance;
            else
                CurrentDistance = Mathf.MoveTowards(CurrentDistance, SafeDistance, recoverySpeed * deltaTime);

            snapNextFrame = false;
        }

        private float CalculateCollisionRadius()
        {
            // 用以镜头为球心的球包住近裁面四角，适用于居中的透视投影
            float nearClipDistance = cameraComponent.nearClipPlane;
            float halfHeight = nearClipDistance * Mathf.Tan(cameraComponent.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * cameraComponent.aspect;
            // 相机到任意一个角的直线距离
            float nearPlaneRadius = Mathf.Sqrt(nearClipDistance * nearClipDistance + halfHeight * halfHeight + halfWidth * halfWidth);
            return Mathf.Max(collisionRadius, nearPlaneRadius);
        }
    }
}
