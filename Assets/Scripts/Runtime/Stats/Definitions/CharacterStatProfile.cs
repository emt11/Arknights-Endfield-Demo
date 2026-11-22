using System;
using System.Collections.Generic;
using UnityEngine;

namespace Endfield.Stats
{
    [CreateAssetMenu(menuName = "Endfield/Stats/Character Stat Profile")]
    public sealed class CharacterStatProfile : ScriptableObject
    {
        [Serializable]
        private struct InitialStat
        {
            public StatId id;
            public double value;
        }

        [SerializeField] private string displayName;
        [SerializeField] private StatCatalog catalog;
        [SerializeField] private StatId main = StatId.STR;
        [SerializeField] private StatId secondary = StatId.AGI;
        [SerializeField] private InitialStat[] initialValues = Array.Empty<InitialStat>();

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        public StatSet CreateSet()
        {
            if (!catalog) throw new InvalidOperationException($"{name} 缺少属性目录");
            var values = new Dictionary<StatId, double>(initialValues.Length);
            foreach (var entry in initialValues)
                values.Add(entry.id, entry.value);
            return catalog.CreateSet(values, main, secondary);
        }
    }
}
