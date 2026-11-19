using System;
using UnityEngine;

namespace Endfield.Combat
{
    [Serializable]
    public struct RootMotionSample
    {
        public float time;
        public Vector3 position;
        public Quaternion rotation;
    }

    [CreateAssetMenu(menuName = "Endfield/Root Motion Definition", fileName = "RootMotionDefinition")]
    public sealed class RootMotionDefinition : ScriptableObject
    {
        public AnimationClip clip;
        [SerializeField] private Avatar avatar;
        public GameObject modelPrefab;
        public Vector3 modelScale = Vector3.one;
        public float sampleRate = 60f;
        [SerializeField] private string dependencySignature;
        [SerializeField] private RootMotionSample[] samples = Array.Empty<RootMotionSample>();

        public int SampleCount => samples.Length;
        public string DependencySignature => dependencySignature;

#if UNITY_EDITOR
        public void SetBakedData(Avatar bakedAvatar, RootMotionSample[] trajectory, string signature)
        {
            avatar = bakedAvatar;
            samples = trajectory;
            dependencySignature = signature;
        }
#endif

        public RootMotionSample Sample(float time)
        {
            if (time <= samples[0].time)
                return samples[0];
            int last = samples.Length - 1;
            if (time >= samples[last].time)
                return samples[last];

            int first = 0;
            while (last - first > 1)
            {
                int middle = first + (last - first) / 2;
                if (samples[middle].time <= time)
                    first = middle;
                else
                    last = middle;
            }

            RootMotionSample from = samples[first];
            RootMotionSample to = samples[last];
            float weight = Mathf.InverseLerp(from.time, to.time, time);
            return new RootMotionSample
            {
                time = time,
                position = Vector3.Lerp(from.position, to.position, weight),
                rotation = Quaternion.Slerp(from.rotation, to.rotation, weight)
            };
        }
    }
}
