using System;
using UnityEngine;

namespace Endfield.Combat
{
    [Serializable]
    public struct RootMotionSample
    {
        public float Time;
        public Vector3 Position;
        public Quaternion Rotation;
    }

    [CreateAssetMenu(menuName = "Endfield/Root Motion Definition", fileName = "RootMotionDefinition")]
    public sealed class RootMotionDefinition : ScriptableObject
    {
        public AnimationClip Clip;
        public Avatar Avatar;
        public GameObject ModelPrefab;
        public Vector3 ModelScale = Vector3.one;
        public float SampleRate = 60f;
        public string DependencySignature;
        public RootMotionSample[] Samples = Array.Empty<RootMotionSample>();

        public RootMotionSample Sample(float time)
        {
            time = Mathf.Clamp(time, 0f, Clip.length);
            int index = Mathf.Min(Mathf.FloorToInt(time * SampleRate), Samples.Length - 2);
            RootMotionSample from = Samples[index];
            RootMotionSample to = Samples[index + 1];
            float weight = Mathf.InverseLerp(from.Time, to.Time, time);
            return new RootMotionSample
            {
                Time = time,
                Position = Vector3.Lerp(from.Position, to.Position, weight),
                Rotation = Quaternion.Slerp(from.Rotation, to.Rotation, weight)
            };
        }

        public void Validate(AnimationClip clip)
        {
            if (Clip != clip || Avatar == null || ModelPrefab == null || SampleRate <= 0f || Samples.Length < 2)
                throw new InvalidOperationException(name + " requires compatible baked root motion");
            if (Mathf.Abs(Samples[Samples.Length - 1].Time - Clip.length) > .0001f)
                throw new InvalidOperationException(name + " has an outdated clip duration, rebake root motion");
#if UNITY_EDITOR
            if (DependencySignature != CurrentSignature())
                throw new InvalidOperationException(name + " 源动画或模型已变化，请重新烘焙根轨迹");
#endif
        }

#if UNITY_EDITOR
        public string CurrentSignature() => UnityEditor.AssetDatabase.GetAssetDependencyHash(UnityEditor.AssetDatabase.GetAssetPath(Clip)) + ":" + UnityEditor.AssetDatabase.GetAssetDependencyHash(UnityEditor.AssetDatabase.GetAssetPath(ModelPrefab)) + ":" + ModelScale.ToString("R");
#endif
    }
}
