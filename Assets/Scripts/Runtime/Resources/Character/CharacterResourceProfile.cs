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

        public CharacterResourceContainer CreateContainer(IReadOnlyStatSet stats)
        {
            return new CharacterResourceContainer(stats, maxUltimateEnergy, initialUltimateEnergy, staggerThreshold);
        }
    }
}
