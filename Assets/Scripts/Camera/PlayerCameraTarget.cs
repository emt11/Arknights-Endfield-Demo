using UnityEngine;

namespace Endfield.Controller
{
    public sealed class PlayerCameraTarget : MonoBehaviour
    {
        [SerializeField] private float height = 1.5f;
        [SerializeField] private float sensitivity = 0.1f;
        [SerializeField] private float minPitch = -30f;
        [SerializeField] private float maxPitch = 70f;
        [SerializeField] private float initialPitch = 15f;
        private Transform follow;
        private float yaw;
        private float pitch;

        public Quaternion PlanarRotation => Quaternion.Euler(0f, yaw, 0f);

        public void Initialize(Transform player)
        {
            follow = player;
            yaw = player.eulerAngles.y;
            pitch = Mathf.Clamp(initialPitch, minPitch, maxPitch);
            ApplyLook(Vector2.zero);
            SyncPosition();
        }

        public void ApplyLook(Vector2 delta)
        {
            yaw = Mathf.Repeat(yaw + delta.x * sensitivity, 360f);
            pitch = Mathf.Clamp(pitch - delta.y * sensitivity, minPitch, maxPitch);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        public void SyncPosition() => transform.position = follow.position + Vector3.up * height;
    }
}
