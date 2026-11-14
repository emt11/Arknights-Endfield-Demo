using System;
using UnityEngine;

namespace Endfield.Character
{
    [DisallowMultipleComponent]
    public sealed class Combatant : MonoBehaviour
    {
        [SerializeField] private CharacterAsset characterAsset;

        public CharacterStats Stats { get; private set; }
        public CharacterHealth Health { get; private set; }
        public string DisplayName => characterAsset.DisplayName;

        private void Awake()
        {
            if (characterAsset == null)
                throw new InvalidOperationException($"{name} requires a CharacterAsset");

            Stats = new CharacterStats(characterAsset.Attack, characterAsset.MaxHP, characterAsset.Defense);
            Health = new CharacterHealth(Stats);
        }

        private void OnDestroy() => Health?.Dispose();
    }
}
