using System;
using Endfield.Common;
using Endfield.Stats;
using UnityEngine;

namespace Endfield.Effects
{
    public enum PulseKind { None, RestoreUltimate }

    [CreateAssetMenu(menuName = "Endfield/Effects/Effect Profile")]
    public sealed class EffectProfile : ScriptableObject
    {
        [SerializeField] private double durationSeconds = 3;
        [SerializeField] private StatModifier[] modifiers = Array.Empty<StatModifier>();
        [SerializeField] private double outgoingDamageBonus;
        [SerializeField] private PulseKind pulse;
        [SerializeField] private double intervalSeconds = 1;
        [SerializeField] private double amount;

        internal double DurationSeconds => durationSeconds;
        internal StatModifier[] Modifiers => modifiers;
        internal double OutgoingDamageBonus => outgoingDamageBonus; // 出伤加成比例
        internal PulseKind Pulse => pulse; // 周期动作
        internal double IntervalSeconds => intervalSeconds; // 周期间隔
        internal double Amount => amount; // 周期动作数值
        internal bool IsTimerOnly => modifiers.Length == 0 && outgoingDamageBonus == 0 && pulse == PulseKind.None;

        internal string ConfigurationError()
        {
            if (!NumericValidation.IsFiniteInRange(durationSeconds, double.Epsilon, double.MaxValue))
                return $"{name} 的持续时间必须是正的有限秒数";
            if (!double.IsFinite(outgoingDamageBonus)) return $"{name} 的出伤加成必须是有限数";
            if (pulse == PulseKind.None) return null;
            if (pulse != PulseKind.RestoreUltimate) return $"{name} 的周期动作无效";
            if (!NumericValidation.IsFiniteInRange(intervalSeconds, double.Epsilon, double.MaxValue))
                return $"{name} 的周期间隔必须是正的有限秒数";
            if (!NumericValidation.IsFiniteInRange(amount, double.Epsilon, double.MaxValue))
                return $"{name} 的回能量必须是正的有限数";
            return null;
        }
    }
}
