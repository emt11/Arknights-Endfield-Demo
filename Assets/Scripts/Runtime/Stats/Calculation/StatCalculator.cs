using System;
using System.Collections.Generic;

namespace Endfield.Stats
{
    internal static class StatCalculator
    {
        private const double HealingReceivedPerWill = 0.001;

        internal static double[] Calculate(double[] bases, SortedDictionary<SourceID, StatModifier[]> sources,
            StatId main, StatId secondary, StatCoefficients coefficients, bool ignoreZeroMultipliers)
        {
            var panel = new double[bases.Length];
            foreach (var definition in StatCatalog.Definitions)
            {
                if (!definition.Derived) Apply(definition.Id, bases[(int)definition.Id]);
            }

            double attributeBonus = CalculateAttributeBonus(WholePoints(main), WholePoints(secondary), coefficients);
            Apply(StatId.AttributeATKBonus, attributeBonus);
            Apply(StatId.ATK, CalculateAttack(panel, panel[(int)StatId.AttributeATKBonus]));
            Apply(StatId.DEF, panel[(int)StatId.BaseDEF]);
            double health = CalculateMaxHealth(panel[(int)StatId.BaseHP], WholePoints(StatId.STR), coefficients);
            Apply(StatId.MaxHP, health);
            Apply(StatId.PhysicalResistance, CalculateResistance(WholePoints(StatId.AGI)));
            Apply(StatId.ArtsResistance, CalculateResistance(WholePoints(StatId.INT)));
            Apply(StatId.HealingReceivedBonus, WholePoints(StatId.WIL) * HealingReceivedPerWill);
            return panel;

            double WholePoints(StatId attribute) => Math.Truncate(panel[(int)attribute]); // 去除小数结果

            void Apply(StatId id, double value)
            {
                panel[(int)id] = ApplyModifiers(id, Finite(value, id), sources, ignoreZeroMultipliers);
            }
        }

        private static double CalculateAttributeBonus(double main, double secondary, StatCoefficients coefficients)
        {
            double mainBonus = Finite(main * coefficients.MainBonus, StatId.AttributeATKBonus);
            double secondaryBonus = Finite(secondary * coefficients.SecondaryBonus, StatId.AttributeATKBonus);
            return mainBonus + secondaryBonus;
        }

        private static double CalculateAttack(double[] panel, double attributeBonus)
        {
            double baseAttack = Finite(panel[(int)StatId.BaseATK] + panel[(int)StatId.WeaponATK], StatId.ATK);
            double scaledAttack = Finite(baseAttack * (1 + panel[(int)StatId.ATKPercentBonus]), StatId.ATK);
            double withFixed = Finite(scaledAttack + panel[(int)StatId.FixedATK], StatId.ATK);
            return withFixed * (1 + attributeBonus);
        }

        private static double CalculateMaxHealth(double baseHealth, double strength, StatCoefficients coefficients)
        {
            return baseHealth + Finite(strength * coefficients.HealthPerStrength, StatId.MaxHP);
        }

        private static double CalculateResistance(double attributePoints)
        {
            return 100 * (1 - 1 / (1 + 0.001 * attributePoints));
        }

        private static double ApplyModifiers(StatId id, double value, SortedDictionary<SourceID, StatModifier[]> sources,
            bool ignoreZeroMultipliers)
        {
            double flat = 0;
            double percent = 0;
            double multiplier = 1;
            HashSet<int> priorities = null;
            StatModifier? winner = null;

            foreach (var source in sources)
            {
                foreach (var modifier in source.Value)
                {
                    if (modifier.StatId != id) continue;
                    switch (modifier.Operation)
                    {
                        case ModifierOperation.Flat:
                            flat = Finite(flat + modifier.Value, id);
                            break;
                        case ModifierOperation.AdditivePercent:
                            percent = Finite(percent + modifier.Value, id);
                            break;
                        case ModifierOperation.Multiplicative:
                            if (!ignoreZeroMultipliers || modifier.Value != 0)
                                multiplier = Finite(multiplier * modifier.Value, id);
                            break;
                        case ModifierOperation.Override:
                            priorities ??= new HashSet<int>();
                            if (!priorities.Add(modifier.Priority))
                                throw new ArgumentException($"Override conflict on {id}, priority {modifier.Priority} ({source.Key})");
                            if (!winner.HasValue || modifier.Priority > winner.Value.Priority)
                                winner = modifier;
                            break;
                    }
                }
            }

            double withFlat = Finite(value + flat, id);
            double withPercent = Finite(withFlat * (1 + percent), id);
            double result = Finite(withPercent * multiplier, id);
            var definition = StatCatalog.Get(id);
            return Math.Clamp(winner?.Value ?? result, definition.Minimum, definition.Maximum);
        }

        private static double Finite(double value, StatId id)
        {
            if (!double.IsFinite(value)) throw new ArithmeticException($"Non-finite result for {id}");
            return value;
        }
    }
}
