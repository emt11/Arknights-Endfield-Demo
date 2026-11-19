using System;
using System.Collections.Generic;
using Endfield.Combat;
using UnityEngine;

namespace Endfield.Character
{
    [CreateAssetMenu(fileName = "CharacterDefinition", menuName = "Endfield/Character/Character Definition")]
    public sealed class CharacterDefinition : ScriptableObject
    {
        [SerializeField] private string displayName;
        [SerializeField, Min(0f)] private float attack = 100f;
        [SerializeField, Min(1f)] private float maxHP = 1000f;
        [SerializeField, Min(0f)] private float defense = 20f;
        [SerializeField] private ActionDefinition[] basicAttacks = Array.Empty<ActionDefinition>();

        public string DisplayName => displayName;
        public float Attack => attack;
        public float MaxHP => maxHP;
        public float Defense => defense;
        public IReadOnlyList<ActionDefinition> BasicAttacks => basicAttacks;

        internal int IndexOfBasicAttack(ActionDefinition action) => Array.IndexOf(basicAttacks, action);
    }
}
