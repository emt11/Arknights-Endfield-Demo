using System.Collections.Generic;
using UnityEngine;

namespace Endfield.Stats
{
    [CreateAssetMenu(menuName = "Endfield/Stats/Stat Catalog")]
    public sealed class StatCatalog : ScriptableObject
    {
        [SerializeField] private StatDefinition[] definitions = CreateCombatDefinitions();

        public StatSet CreateSet(IReadOnlyDictionary<string, double> initialValues = null, AttributeBinding binding = default)
        {
            return StatSet.Create(definitions, initialValues, binding);
        }

        public static StatDefinition[] CreateCombatDefinitions()
        {
            const UnitType point = UnitType.Point;
            const UnitType ratio = UnitType.Ratio;
            var stats = new List<StatDefinition>();
            foreach (string id in new[] { "STR", "AGI", "INT", "WIL" })
                stats.Add(new StatDefinition(id, point, StatCategory.Primary, required: true, minimum: 0));
            foreach (string id in new[] { "BaseATK", "BaseDEF", "BaseHP" })
                stats.Add(new StatDefinition(id, point, required: true, minimum: 0));

            stats.Add(new StatDefinition("WeaponATK", point));
            stats.Add(new StatDefinition("FixedATK", point));
            stats.Add(new StatDefinition("ATKPercentBonus", ratio, StatCategory.AttackBonus, allowed: ModifierOperation.Flat));
            stats.Add(new StatDefinition("CritRate", ratio, StatCategory.Critical, minimum: 0, maximum: 1));
            stats.Add(new StatDefinition("DEF", point, StatCategory.Combat, minimum: 0, formula:
                new FormulaDefinition("Defense", "DEF", FormulaKind.WeightedSum,
                    new[] { new FormulaInput("Base", "BaseDEF", point) }, 1)));
            stats.Add(new StatDefinition("MaxHP", point, StatCategory.ResourceMaximum, minimum: 0, formula:
                new FormulaDefinition("MaximumHealth", "MaxHP", FormulaKind.WeightedSum,
                    new[] { new FormulaInput("Base", "BaseHP", point), new FormulaInput("Strength", "STR", point) }, 1, 5)));
            stats.Add(new StatDefinition("AttributeATKBonus", ratio, StatCategory.AttackBonus, allowed: ModifierOperation.None,
                formula: new FormulaDefinition("AttributeBonus", "AttributeATKBonus", FormulaKind.WeightedSum,
                    new[] { new FormulaInput("Main", AttributeBinding.MainSlot, point),
                            new FormulaInput("Secondary", AttributeBinding.SecondarySlot, point) }, 0.005, 0.002)));
            stats.Add(new StatDefinition("ATK", point, StatCategory.Combat, minimum: 0, allowed: ModifierOperation.Override,
                formula: new FormulaDefinition("Attack", "ATK", FormulaKind.Attack, new[]
                {
                    new FormulaInput("BaseATK", "BaseATK", point), new FormulaInput("WeaponATK", "WeaponATK", point),
                    new FormulaInput("ATKPercentBonus", "ATKPercentBonus", ratio), new FormulaInput("FixedATK", "FixedATK", point),
                    new FormulaInput("AttributeATKBonus", "AttributeATKBonus", ratio)
                })));
            return stats.ToArray();
        }
    }
}
