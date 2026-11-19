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
        public Avatar avatar;
        public GameObject modelPrefab;
        public Vector3 modelScale = Vector3.one;
        public float sampleRate = 60f;
        public string dependencySignature;
        public RootMotionSample[] samples = Array.Empty<RootMotionSample>();

        public RootMotionSample Sample(float time)
        {
            time = Mathf.Clamp(time, 0f, clip.length);
            int index = Mathf.Min(Mathf.FloorToInt(time * sampleRate), samples.Length - 2);
            RootMotionSample from = samples[index];
            RootMotionSample to = samples[index + 1];
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
