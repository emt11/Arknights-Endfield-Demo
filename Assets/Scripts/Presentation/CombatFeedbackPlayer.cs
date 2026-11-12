using Endfield.Pooling;
using UnityEngine;

namespace Endfield.Presentation
{
    public sealed class CombatFeedbackPlayer : MonoBehaviour
    {
        [SerializeField] private GameObjectPool pool;

        public PoolHandle PlayAudio(AudioAsset asset, Vector3 position)
        {
            if (asset == null)
                return default;

            AudioClip clip = asset.GetRandomClip();
            if (clip == null)
                return default;

            PooledAudioSource audio = pool.Rent(asset.Prefab, position, Quaternion.identity);
            return audio.Play(clip, asset.Volume, asset.GetRandomPitch(), asset.Loop);
        }

        public PoolHandle PlayEffect(ParticleEffectAsset asset, Vector3 position,
            Quaternion rotation, Transform followTarget = null)
        {
            if (asset == null)
                return default;

            PooledParticleEffect effect = pool.Rent(asset.Prefab, position, rotation);
            effect.transform.localScale *= asset.Scale;
            return effect.Play(followTarget);
        }

        public void PlayHit(AudioAsset audio, ParticleEffectAsset effect,
            Vector3 position, Quaternion rotation)
        {
            PlayAudio(audio, position);
            PlayEffect(effect, position, rotation);
        }
    }
}
