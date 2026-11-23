using UnityEngine;

namespace Endfield.Resources
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public sealed class TeamSkillPoints : MonoBehaviour
    {
        [SerializeField] private double maxSP;
        [SerializeField] private double initialSP;

        public SkillPointPool Pool { get; private set; }

        private void Awake()
        {
            Pool = new SkillPointPool(maxSP, initialSP);
        }
    }
}
