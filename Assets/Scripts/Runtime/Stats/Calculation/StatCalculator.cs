using System;
using System.Collections.Generic;

namespace Endfield.Stats
{
    internal sealed class StatInstance
    {
        internal double BaseValue;
        internal readonly SortedDictionary<ModifierKey, StatModifier> Modifiers;

        internal StatInstance(double baseValue)
        {
            BaseValue = baseValue;
            Modifiers = new SortedDictionary<ModifierKey, StatModifier>();
        }

        internal StatInstance(StatInstance original)
        {
            BaseValue = original.BaseValue;
            Modifiers = new SortedDictionary<ModifierKey, StatModifier>(original.Modifiers);
        }
    }

    internal static class StatCalculator
    {
        internal static double Finite(double value, string context)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException($"Non-finite value in {context}");
            return value;
        }

        internal static double Evaluate(StatNode node, StatInstance state, double[] values,
            List<StatStep> steps, out ModifierKey? winningOverride)
        {
            var definition = node.Definition;
            string id = definition.Id;
            double value = state.BaseValue;
            if (definition.Formula != null) value = FormulaValue(node, values, steps);
            steps?.Add(new StatStep("Base", value));

            double flat = 0, percent = 0, factor = 1;
            StatModifier? winner = null;
            Dictionary<int, ModifierKey> priorities = null;
            foreach (var modifier in state.Modifiers.Values)
            {
                switch (modifier.Operation)
                {
                    case ModifierOperation.Flat:
                        flat = Finite(flat + modifier.Value, id);
                        break;
                    case ModifierOperation.AdditivePercent:
                        percent = Finite(percent + modifier.Value, id);
                        break;
                    case ModifierOperation.Multiplicative:
                        factor = Finite(factor * modifier.Value, id);
                        break;
                    case ModifierOperation.Override:
                        priorities ??= new Dictionary<int, ModifierKey>();
                        if (priorities.TryGetValue(modifier.Priority, out var conflict))
                            throw new ArgumentException($"Override conflict: {id}, priority {modifier.Priority}, {conflict} and {modifier.Key}");
                        priorities.Add(modifier.Priority, modifier.Key);
                        if (!winner.HasValue || modifier.Priority > winner.Value.Priority) winner = modifier;
                        break;
                }
            }
            value = Finite(value + flat, id);
            steps?.Add(new StatStep("Flat", value));
            value = Finite(value * (1 + percent), id);
            steps?.Add(new StatStep("AdditivePercent", value));
            value = Finite(value * factor, id);
            steps?.Add(new StatStep("Multiplicative", value));
            winningOverride = winner?.Key;
            if (winner.HasValue) value = winner.Value.Value;
            steps?.Add(new StatStep("Override", value));
            if (definition.RoundDown) value = Math.Floor(value);
            steps?.Add(new StatStep("Round", value));
            if (definition.Minimum.HasValue) value = Math.Max(value, definition.Minimum.Value);
            if (definition.Maximum.HasValue) value = Math.Min(value, definition.Maximum.Value);
            steps?.Add(new StatStep("Clamp", value));
            return value;
        }

        private static double FormulaValue(StatNode node, double[] values, List<StatStep> steps)
        {
            var inputs = node.Inputs;
            string id = node.Definition.Id;
            if (node.Definition.Formula.Kind == FormulaKind.WeightedSum)
            {
                double sum = 0;
                for (int i = 0; i < inputs.Length; i++)
                    sum = Finite(sum + values[inputs[i]] * node.Weights[i], id);
                return sum;
            }

            // The graph binds the five named attack slots to this order once at creation.
            double attack = Finite(values[inputs[0]] + values[inputs[1]], id);
            steps?.Add(new StatStep("BaseAndWeapon", attack));
            attack = Finite(attack * (1 + values[inputs[2]]), id);
            steps?.Add(new StatStep("AttackPercent", attack));
            attack = Finite(attack + values[inputs[3]], id);
            steps?.Add(new StatStep("FixedAttack", attack));
            attack = Finite(attack * (1 + values[inputs[4]]), id);
            steps?.Add(new StatStep("AttributeBonus", attack));
            return attack;
        }
    }
}
