using System;
using System.Collections.Generic;

namespace Endfield.Character
{
    public sealed class Stat
    {
        private readonly List<StatModifier> modifiers = new List<StatModifier>();

        public float BaseValue { get; }
        public float Minimum { get; }
        public float Value => CalculateValue();
        public int ModifierCount => modifiers.Count;
        public event Action<float, float> Changed;

        public Stat(float baseValue, float minimum)
        {
            Minimum = Math.Max(0f, minimum);
            BaseValue = Math.Max(Minimum, baseValue);
        }

        public StatModifier AddModifier(ModifierOperation operation, float value, object source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (operation != ModifierOperation.Flat && operation != ModifierOperation.PercentAdd)
                throw new ArgumentOutOfRangeException(nameof(operation));

            float previous = Value;
            var modifier = new StatModifier(operation, value, source);
            modifiers.Add(modifier);
            NotifyChange(previous);
            return modifier;
        }

        public bool RemoveModifier(StatModifier modifier)
        {
            if (modifier == null) return false;
            float previous = Value;
            if (!modifiers.Remove(modifier)) return false;
            NotifyChange(previous);
            return true;
        }

        private float CalculateValue()
        {
            float flat = 0f;
            float percent = 0f;
            foreach (StatModifier modifier in modifiers)
            {
                if (modifier.Operation == ModifierOperation.Flat)
                    flat += modifier.Value;
                else
                    percent += modifier.Value;
            }

            float value = Math.Max(0f, BaseValue + flat) * Math.Max(0f, 1f + percent);
            return Math.Max(Minimum, value);
        }

        private void NotifyChange(float previous)
        {
            float current = Value;
            if (current != previous)
                Changed?.Invoke(previous, current);
        }
    }
}