using System;
using Endfield.Common;
using Endfield.Effects;
using UnityEngine;

namespace Endfield.Status
{
    [CreateAssetMenu(menuName = "Endfield/Status/Status Rule Profile")]
    public sealed class StatusRuleProfile : ScriptableObject
    {
        [SerializeField, Min(1)] private int maxVulnerableStacks = 3; // 易伤最大层数
        [SerializeField] private double physicalBonusPerStack = 0.1; // 易伤物理承伤
        [SerializeField, Min(1)] private int maxAttachmentStacks = 2; // 最大元素附着层数
        [SerializeField] private EffectProfile vulnerableTimer;
        [SerializeField] private EffectProfile attachmentTimer;
        [SerializeField] private EffectProfile frozenTimer;
        [SerializeField] private EffectProfile staggeredTimer;

        internal int MaxVulnerableStacks => maxVulnerableStacks;
        internal int MaxAttachmentStacks => maxAttachmentStacks;
        internal double PhysicalBonusPerStack => physicalBonusPerStack;

        internal EffectProfile TimerFor(StatusType type) => type switch
        {
            StatusType.Vulnerable => vulnerableTimer,
            StatusType.Heat or StatusType.Cryo => attachmentTimer,
            StatusType.Frozen => frozenTimer,
            StatusType.Staggered => staggeredTimer,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        internal void Validate()
        {
            NumericValidation.RequireFiniteInRange(maxVulnerableStacks, 1, int.MaxValue, nameof(maxVulnerableStacks));
            NumericValidation.RequireFiniteInRange(maxAttachmentStacks, 1, int.MaxValue, nameof(maxAttachmentStacks));
            NumericValidation.RequireFiniteInRange(physicalBonusPerStack, 0, double.MaxValue, nameof(physicalBonusPerStack));
            foreach (var timer in new[] { vulnerableTimer, attachmentTimer, frozenTimer, staggeredTimer })
            {
                if (!timer || !timer.IsTimerOnly) throw new InvalidOperationException($"{name} 需要纯计时效果模板");
                var error = timer.ConfigurationError();
                if (error != null) throw new InvalidOperationException(error);
            }
        }
    }
}
