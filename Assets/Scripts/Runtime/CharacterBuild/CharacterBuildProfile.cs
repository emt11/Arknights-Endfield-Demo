using System;
using System.Collections.Generic;
using Endfield.Stats;
using UnityEngine;

namespace Endfield.CharacterBuild
{
    [CreateAssetMenu(menuName = "Endfield/Character Build/Character Build Profile")]
    public sealed class CharacterBuildProfile : ScriptableObject
    {
        [Serializable]
        private struct GrowthEntry
        {
            public StatId statId;
            public double perLevel;
        }

        [SerializeField, Min(1)] private int initialLevel = 1;
        [SerializeField, Min(1)] private int maxLevel = 90;
        [SerializeField] private GrowthEntry[] growthPerLevel = Array.Empty<GrowthEntry>();
        [SerializeField] private BuildModifierProfile initialWeapon;
        [SerializeField] private BuildModifierProfile initialGear;
        [SerializeField] private BuildModifierProfile[] initialTalents = Array.Empty<BuildModifierProfile>();

        internal int InitialLevel => initialLevel;
        internal int MaxLevel => maxLevel;
        internal BuildModifierProfile InitialWeapon => initialWeapon;
        internal BuildModifierProfile InitialGear => initialGear;
        internal IReadOnlyList<BuildModifierProfile> InitialTalents => initialTalents;

        internal void ValidateGrowth()
        {
            foreach (var entry in growthPerLevel)
            {
                StatCatalog.Get(entry.statId);
                if (!double.IsFinite(entry.perLevel))
                    throw new ArgumentException($"{entry.statId} 的每级增量必须是有限数");
            }
        }

        internal void AppendGrowth(int level, List<StatModifier> modifiers)
        {
            if (level == 1) return;
            foreach (var entry in growthPerLevel)
                modifiers.Add(new StatModifier(entry.statId, ModifierOperation.Flat, (level - 1) * entry.perLevel));
        }
    }
}
