using Endfield.Pooling;
using UnityEngine;

namespace Endfield.Presentation
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class PooledAudioSource : PooledObject
    {
        private AudioSource source;
        private bool started;

        protected override void OnCreated()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.ignoreListenerPause = false;
        }

        protected override void OnRent() => started = false;

        public PoolHandle Play(AudioClip clip, float volume = 1f, float pitch = 1f, bool loop = false)
        {
            if (clip == null)
            {
                ReturnToPool();
                return default;
            }

            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, 0.01f, 3f);
            source.loop = loop;
            PoolHandle handle = Activate();
            source.Play();
            started = true;
            return handle;
        }

        private void Update()
        {
            if (!started || AudioListener.pause)
                return;

            if (!source.isPlaying)
                ReturnToPool();
        }

        protected override void OnReturn()
        {
            started = false;
            source.Stop();
            source.clip = null;
            source.loop = false;
            source.volume = 1f;
            source.pitch = 1f;
        }
    }
}
