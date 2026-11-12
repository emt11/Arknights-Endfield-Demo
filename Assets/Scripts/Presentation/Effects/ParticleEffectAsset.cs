using UnityEngine;

namespace Endfield.Presentation
{
    [CreateAssetMenu(fileName = "ParticleEffectAsset", menuName = "Endfield/Presentation/Particle Effect Asset")]
    public sealed class ParticleEffectAsset : ScriptableObject
    {
        [SerializeField] private PooledParticleEffect prefab;
        [SerializeField, Min(0.01f)] private float scale = 1f;

        public PooledParticleEffect Prefab => prefab;
        public float Scale => scale;
    }
}
