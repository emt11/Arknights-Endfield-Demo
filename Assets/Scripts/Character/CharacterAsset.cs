using UnityEngine;

namespace Endfield.Character
{
    [CreateAssetMenu(fileName = "CharacterAsset", menuName = "Endfield/Character/Character Asset")]
    public sealed class CharacterAsset : ScriptableObject
    {
        [SerializeField] private string displayName;
        [SerializeField, Min(0f)] private float attack = 100f;
        [SerializeField, Min(1f)] private float maxHP = 1000f;
        [SerializeField, Min(0f)] private float defense = 20f;

        public float Attack => attack;
        public float MaxHP => maxHP;
        public float Defense => defense;
        public string DisplayName => displayName;

        private void OnValidate()
        {
            attack = Mathf.Max(0f, attack);
            maxHP = Mathf.Max(1f, maxHP);
            defense = Mathf.Max(0f, defense);
        }
    }    
}
