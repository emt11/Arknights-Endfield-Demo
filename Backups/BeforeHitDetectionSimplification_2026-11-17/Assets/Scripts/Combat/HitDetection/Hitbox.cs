using UnityEngine;

namespace Endfield.Combat
{
    public enum HitboxShape
    {
        Capsule,
        Sphere
    }

    public enum HitboxAxis
    {
        X,
        Y,
        Z
    }

    public readonly struct HitboxPose
    {
        public readonly Vector3 position;
        public readonly Quaternion rotation;
        public readonly Vector3 scale;

        public HitboxPose(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }

        public Vector3 TransformPoint(Vector3 point) => position + rotation * Vector3.Scale(scale, point);

        public static HitboxPose Lerp(HitboxPose from, HitboxPose to, float weight) => new HitboxPose(
            Vector3.Lerp(from.position, to.position, weight),
            Quaternion.Slerp(from.rotation, to.rotation, weight),
            Vector3.Lerp(from.scale, to.scale, weight));
    }

    // 只保存武器几何和本步姿态；窗口、阵营、去重与伤害由战斗流程负责
    [DisallowMultipleComponent]
    public sealed class Hitbox : MonoBehaviour
    {
        public int id;
        public HitboxShape shape;
        public Vector3 center;
        [Min(.01f)] public float radius = .12f;
        [Min(0f), Tooltip("胶囊两端球心之间的距离，不含端部半径")]
        public float length = 1f;
        public HitboxAxis axis = HitboxAxis.Y;

        public HitboxPose StartPose { get; private set; }
        public HitboxPose EndPose { get; private set; }

        public HitboxPose ReadPose() => new HitboxPose(transform.position, transform.rotation, transform.lossyScale);

        public void CaptureStart()
        {
            StartPose = ReadPose();
            EndPose = StartPose;
        }

        public void CaptureEnd() => EndPose = ReadPose();

        public void GetGeometry(HitboxPose pose, out Vector3 pointA, out Vector3 pointB, out float worldRadius)
        {
            Vector3 direction = axis switch
            {
                HitboxAxis.X => Vector3.right,
                HitboxAxis.Y => Vector3.up,
                _ => Vector3.forward
            };
            Vector3 half = shape == HitboxShape.Capsule ? direction * (length * .5f) : Vector3.zero;
            pointA = pose.TransformPoint(center - half);
            pointB = pose.TransformPoint(center + half);
            worldRadius = radius * MaxScale(pose.scale);
        }

        public int GetSweepSegments(HitboxPose from, HitboxPose to)
        {
            float reach = center.magnitude + (shape == HitboxShape.Capsule ? length * .5f : 0f);
            float scale = Mathf.Max(MaxScale(from.scale), MaxScale(to.scale));
            float travel = Vector3.Distance(from.position, to.position);
            travel += Quaternion.Angle(from.rotation, to.rotation) * Mathf.Deg2Rad * reach * scale;
            travel += Vector3.Distance(from.scale, to.scale) * (reach + radius);
            float spacing = radius * Mathf.Min(MaxScale(from.scale), MaxScale(to.scale)) * .75f;
            // 每个战斗步最多插入 32 段近似查询，不把姿态插值当作动画连续碰撞
            return Mathf.Clamp(Mathf.CeilToInt(travel / spacing), 1, 32);
        }

        private static float MaxScale(Vector3 scale) => Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

        private void OnDrawGizmosSelected()
        {
            GetGeometry(ReadPose(), out Vector3 pointA, out Vector3 pointB, out float worldRadius);
            Gizmos.color = new Color(1f, .45f, .15f);
            Gizmos.DrawWireSphere(pointA, worldRadius);
            if (shape == HitboxShape.Sphere)
                return;

            Gizmos.DrawWireSphere(pointB, worldRadius);
            Vector3 direction = (pointB - pointA).normalized;
            Vector3 side = Vector3.Cross(direction, Mathf.Abs(direction.y) < .9f ? Vector3.up : Vector3.right).normalized * worldRadius;
            Vector3 up = Vector3.Cross(direction, side);
            Gizmos.DrawLine(pointA + side, pointB + side);
            Gizmos.DrawLine(pointA - side, pointB - side);
            Gizmos.DrawLine(pointA + up, pointB + up);
            Gizmos.DrawLine(pointA - up, pointB - up);
        }
    }
}
