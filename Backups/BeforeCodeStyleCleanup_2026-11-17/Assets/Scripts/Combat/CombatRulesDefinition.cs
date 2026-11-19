using UnityEngine;

namespace Endfield.Combat
{
    [CreateAssetMenu(menuName = "Endfield/Combat Rules Definition", fileName = "CombatRulesDefinition")]
    public sealed class CombatRulesDefinition : ScriptableObject
    {
        [Min(0.001f)] public float MaxCombatStep = 1f / 60f;
        [Min(1)] public int MaxCombatStepsPerFrame = 4;
        [Min(0f)] public float InputBufferDuration = 0.2f;
        [Min(0f)] public float BattleRate = 1f;
        [Min(1f)] public float DefenseConstant = 100f;
        public LayerMask HurtboxMask;
        public LayerMask WorldMask = 1;
    }
}
