using UnityEngine;

namespace Endfield.Combat
{
    [CreateAssetMenu(menuName = "Endfield/Combat Rules Definition", fileName = "CombatRulesDefinition")]
    public sealed class CombatRulesDefinition : ScriptableObject
    {
        [Min(0.001f)] public float maxCombatStep = 1f / 60f;
        [Min(1)] public int maxCombatStepsPerFrame = 4;
        [Min(0f)] public float inputBufferDuration = 0.2f;
        [Min(0f)] public float battleRate = 1f;
        [Min(1f)] public float defenseConstant = 100f;
        public LayerMask hurtboxMask;
        public LayerMask worldMask = 1;
    }
}
