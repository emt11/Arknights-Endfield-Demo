using Endfield.Stats;
using UnityEngine;

namespace Endfield.Resources
{
    [CreateAssetMenu(menuName = "Endfield/Resources/Character Resource Profile")]
    public sealed class CharacterResourceProfile : ScriptableObject
    {
        [SerializeField] private double maxUltimateEnergy;
        [SerializeField] private double initialUltimateEnergy;
        [SerializeField] private double staggerThreshold;

        public ResourceContainer CreateContainer(IReadOnlyStatSet stats)
        {
            return new ResourceContainer(stats, maxUltimateEnergy, initialUltimateEnergy, staggerThreshold);
        }
    }
}
