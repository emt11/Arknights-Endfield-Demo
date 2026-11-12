using System;
using UnityEngine;

namespace Endfield.Presentation
{
    [CreateAssetMenu(fileName = "AudioAsset", menuName = "Endfield/Presentation/Audio Asset")]
    public sealed class AudioAsset : ScriptableObject
    {
        [SerializeField] private PooledAudioSource prefab;
        [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [SerializeField] private Vector2 pitchRange = Vector2.one;
        [SerializeField] private bool loop;

        public PooledAudioSource Prefab => prefab;
        public float Volume => volume;
        public bool Loop => loop;

        public AudioClip GetRandomClip()
        {
            return clips.Length == 0 ? null : clips[UnityEngine.Random.Range(0, clips.Length)];
        }

        public float GetRandomPitch() => UnityEngine.Random.Range(pitchRange.x, pitchRange.y);

        private void OnValidate()
        {
            pitchRange.x = Mathf.Clamp(pitchRange.x, 0.01f, 3f);
            pitchRange.y = Mathf.Clamp(pitchRange.y, pitchRange.x, 3f);
        }
    }
}
