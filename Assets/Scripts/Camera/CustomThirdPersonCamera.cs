using UnityEngine;

namespace Endfield.Controller
{
    [RequireComponent(typeof(Camera))]
    public sealed class CustomThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private PlayerCameraTarget target;
        [SerializeField, Min(0.1f)] private float distance = 4f;
        [SerializeField, Min(0.01f)] private float collisionRadius = 0.2f;
        [SerializeField, Min(0f)] private float collisionPadding = 0.03f;
        [SerializeField, Min(0.01f)] private float recoverySpeed = 4f;
        [SerializeField] private LayerMask obstructionMask;
        private Camera cameraComponent;
        private bool snapNextFrame;

        public float CurrentDistance { get; private set; }
        public float SafeDistance { get; private set; }
        public bool PivotOverlapping { get; private set; }

        private void Awake() => cameraComponent = GetComponent<Camera>();

        private void OnEnable() => snapNextFrame = true;

        private void LateUpdate()
        {
            Transform pivot = target.transform;
            Vector3 origin = pivot.position;
            Vector3 backward = -pivot.forward;

            UpdateObstruction(origin, backward);
            UpdateFollowDistance(Time.deltaTime);
            transform.SetPositionAndRotation(origin + backward * CurrentDistance, pivot.rotation);
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
            float nearPlaneRadius = Mathf.Sqrt(nearClipDistance * nearClipDistance + halfHeight * halfHeight + halfWidth * halfWidth);
            return Mathf.Max(collisionRadius, nearPlaneRadius);
        }

        private void OnValidate()
        {
            distance = Mathf.Max(0.1f, distance);
            collisionRadius = Mathf.Max(0.01f, collisionRadius);
            collisionPadding = Mathf.Max(0f, collisionPadding);
            recoverySpeed = Mathf.Max(0.01f, recoverySpeed);
        }
    }
}
