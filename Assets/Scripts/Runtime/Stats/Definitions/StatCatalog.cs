using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Endfield.Stats
{
    public readonly struct StatCoefficients
    {
        public const double DefaultMainBonus = 0.005;
        public const double DefaultSecondaryBonus = 0.002;
        public const double DefaultHealthPerStrength = 5;

        public static StatCoefficients Default => new StatCoefficients( DefaultMainBonus, DefaultSecondaryBonus, DefaultHealthPerStrength);

        public double MainBonus { get; }
        public double SecondaryBonus { get; }
        public double HealthPerStrength { get; }

        public StatCoefficients(double mainBonus, double secondaryBonus, double healthPerStrength)
        {
            if (!double.IsFinite(mainBonus) || !double.IsFinite(secondaryBonus) || !double.IsFinite(healthPerStrength))
                throw new ArgumentException("Stat coefficients must be finite");
            MainBonus = mainBonus;
            SecondaryBonus = secondaryBonus;
            HealthPerStrength = healthPerStrength;
        }
    }

    [CreateAssetMenu(menuName = "Endfield/Stats/Stat Catalog")]
    public sealed class StatCatalog : ScriptableObject
    {
        public static IReadOnlyList<StatGroup> Groups { get; } = Array.AsReadOnly((StatGroup[])Enum.GetValues(typeof(StatGroup)));

        internal static readonly StatDefinition[] Definitions =
        {
            new(StatId.STR, StatGroup.PrimaryAttributes, "力量", required: true, minimum: 0),
            new(StatId.AGI, StatGroup.PrimaryAttributes, "敏捷", required: true, minimum: 0),
            new(StatId.INT, StatGroup.PrimaryAttributes, "智识", required: true, minimum: 0),
            new(StatId.WIL, StatGroup.PrimaryAttributes, "意志", required: true, minimum: 0),
            new(StatId.BaseATK, StatGroup.BaseCombat, "基础攻击", required: true, minimum: 0),
            new(StatId.BaseDEF, StatGroup.BaseCombat, "基础防御", required: true, minimum: 0),
            new(StatId.BaseHP, StatGroup.BaseCombat, "基础生命", required: true, minimum: 0),
            new(StatId.WeaponATK, StatGroup.BaseCombat, "武器攻击"),
            new(StatId.FixedATK, StatGroup.BaseCombat, "固定攻击"),
            new(StatId.ATKPercentBonus, StatGroup.BaseCombat, "面板攻击力百分比加成", StatUnit.Ratio),
            new(StatId.CritRate, StatGroup.DerivedCombat, "暴击率", StatUnit.Ratio, minimum: 0, maximum: 1),
            new(StatId.CritDamage, StatGroup.DerivedCombat, "暴击伤害", StatUnit.Ratio, defaultValue: 1.5, minimum: 0),
            new(StatId.AttributeATKBonus, StatGroup.DerivedCombat, "主副属性攻击加成", StatUnit.Ratio, derived: true),
            new(StatId.ATK, StatGroup.DerivedCombat, "最终攻击", derived: true, minimum: 0),
            new(StatId.DEF, StatGroup.DerivedCombat, "最终防御", derived: true, minimum: 0),
            new(StatId.MaxHP, StatGroup.DerivedCombat, "最大生命", derived: true, minimum: 0),
            new(StatId.PhysicalResistance, StatGroup.Resistance, "物理抗性", derived: true),
            new(StatId.ArtsResistance, StatGroup.Resistance, "法术抗性", derived: true),
            new(StatId.HealingReceivedBonus, StatGroup.Special, "受治疗效率", StatUnit.Ratio, derived: true)
        };

        private static readonly ILookup<StatGroup, StatDefinition> ByGroup = Definitions.ToLookup(item => item.Group);

        [SerializeField] private double mainBonus = StatCoefficients.DefaultMainBonus;
        [SerializeField] private double secondaryBonus = StatCoefficients.DefaultSecondaryBonus;
        [SerializeField] private double healthPerStrength = StatCoefficients.DefaultHealthPerStrength;

        public static StatDefinition Get(StatId id)
        {
            if ((uint)id >= Definitions.Length)
                throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown stat");
            return Definitions[(int)id];
        }

        public static IEnumerable<StatDefinition> GetByGroup(StatGroup group) => ByGroup[group];

        public static string GetGroupName(StatGroup group) => group switch
        {
            StatGroup.PrimaryAttributes => "基础属性",
            StatGroup.BaseCombat => "基础战斗",
            StatGroup.DerivedCombat => "战斗面板",
            StatGroup.DamageBonus => "伤害加成",
            StatGroup.Resistance => "抗性",
            StatGroup.Special => "特殊属性",
            _ => throw new ArgumentOutOfRangeException(nameof(group))
        };

        public StatSet CreateSet(IReadOnlyDictionary<StatId, double> initialValues, StatId main, StatId secondary)
        {
            var coefficients = new StatCoefficients(mainBonus, secondaryBonus, healthPerStrength);
            return StatSet.Create(initialValues, main, secondary, coefficients);
        }
    }
}
