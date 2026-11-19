using UnityEngine;

namespace Endfield.Presentation
{
    [CreateAssetMenu(fileName = "ParticleEffectDefinition", menuName = "Endfield/Presentation/Particle Effect Definition")]
    public sealed class ParticleEffectDefinition : ScriptableObject
    {
        [SerializeField] private PooledParticleEffect prefab;
        [SerializeField, Min(0.01f)] private float scale = 1f;

        public PooledParticleEffect Prefab => prefab;
        public float Scale => scale;
    }
}
