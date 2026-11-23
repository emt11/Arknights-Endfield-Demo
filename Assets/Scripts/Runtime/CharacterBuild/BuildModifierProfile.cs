using System;
using System.Collections.Generic;
using Endfield.Stats;
using UnityEngine;

namespace Endfield.CharacterBuild
{
    public enum BuildModifierKind { Weapon, Gear, Talent }

    [CreateAssetMenu(menuName = "Endfield/Character Build/Modifier Profile")]
    public sealed class BuildModifierProfile : ScriptableObject
    {
        [SerializeField] private BuildModifierKind kind;
        [SerializeField] private StatModifier[] entries = Array.Empty<StatModifier>();

        public BuildModifierKind Kind => kind;

        internal void AppendModifiers(List<StatModifier> modifiers)
        {
            modifiers.AddRange(entries);
        }
    }
}
