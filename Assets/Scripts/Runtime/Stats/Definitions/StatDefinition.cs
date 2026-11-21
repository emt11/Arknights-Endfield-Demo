using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Endfield.Stats
{
    public enum StatCategory { Primary, BaseCombat, AttackBonus, Combat, ResourceMaximum, Critical, Resistance, Special }
    public enum UnitType { Point, Ratio, Multiplier, ResistancePoint, MetersPerSecond }
    public enum StatValueKind { Direct, Derived } // 基础值的来源：直接赋值 或 派生赋值（按公式推导）
    public enum FormulaKind { WeightedSum, Attack } // 公式类型：加权和、攻击自定义公式

    [Flags]
    public enum ModifierOperation
    {
        None = 0, Flat = 1, AdditivePercent = 2, Multiplicative = 4, Override = 8,
        All = Flat | AdditivePercent | Multiplicative | Override
    }

    [Serializable]
    public struct FormulaInput
    {
        [SerializeField] private string slot;
        [SerializeField] private string statId;
        [SerializeField] private UnitType unit;

        public string Slot => slot;
        public string StatId => statId;
        public UnitType Unit => unit;

        public FormulaInput(string slot, string statId, UnitType unit)
        {
            this.slot = slot;
            this.statId = statId;
            this.unit = unit;
        }
    }

    [Serializable]
    public sealed class FormulaDefinition
    {
        [SerializeField] private string id;
        [SerializeField] private string output;
        [SerializeField] private FormulaKind kind;
        [SerializeField] private FormulaInput[] inputs;
        [SerializeField] private double[] coefficients;

        public string Id => id;
        public string Output => output;
        public FormulaKind Kind => kind;
        public IReadOnlyList<FormulaInput> Inputs => Array.AsReadOnly(inputs);
        public IReadOnlyList<double> Coefficients => Array.AsReadOnly(coefficients);

        public FormulaDefinition(string id, string output, FormulaKind kind, FormulaInput[] inputs, params double[] coefficients)
        {
            this.id = id;
            this.output = output;
            this.kind = kind;
            this.inputs = inputs?.ToArray() ?? throw new ArgumentNullException(nameof(inputs));
            this.coefficients = coefficients?.ToArray() ?? throw new ArgumentNullException(nameof(coefficients));
        }

        internal FormulaDefinition Copy()
        {
            return new FormulaDefinition(id, output, kind, inputs, coefficients);
        }
    }

    public readonly struct AttributeBinding
    {
        public const string MainSlot = "$Main";
        public const string SecondarySlot = "$Secondary";

        public string Main { get; }
        public string Secondary { get; }

        public AttributeBinding(string main, string secondary)
        {
            Main = main;
            Secondary = secondary;
        }

        internal string Resolve(string id)
        {
            if (id == MainSlot) return Main;
            if (id == SecondarySlot) return Secondary;
            return id;
        }
    }

    [Serializable]
    public sealed class StatDefinition
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private StatCategory category;
        [SerializeField] private UnitType unit;
        [SerializeField] private StatValueKind valueKind;
        [SerializeField] private bool requiresInitialValue;
        [SerializeField] private double defaultValue;
        [SerializeField] private bool hasMinimum, hasMaximum;
        [SerializeField] private double minimum, maximum;
        [SerializeField] private bool roundDown;
        [SerializeField] private ModifierOperation allowed;
        [SerializeField] private FormulaDefinition formula;

        public string Id => id;
        public string DisplayName => displayName;
        public StatCategory Category => category;
        public UnitType Unit => unit;
        public StatValueKind ValueKind => valueKind;
        public bool RequiresInitialValue => requiresInitialValue;
        public double DefaultValue => defaultValue;
        public double? Minimum => hasMinimum ? minimum : (double?)null;
        public double? Maximum => hasMaximum ? maximum : (double?)null;
        public bool RoundDown => roundDown;
        public ModifierOperation Allowed => allowed;
        public FormulaDefinition Formula => valueKind == StatValueKind.Derived ? formula : null;

        public StatDefinition(string id, UnitType unit, StatCategory category = StatCategory.BaseCombat,
            double defaultValue = 0, bool required = false, double? minimum = null, double? maximum = null,
            bool roundDown = false, ModifierOperation allowed = ModifierOperation.All,
            FormulaDefinition formula = null, string displayName = null)
        {
            this.id = id;
            this.displayName = displayName ?? id;
            this.unit = unit;
            this.category = category;
            this.defaultValue = defaultValue;
            requiresInitialValue = required;
            hasMinimum = minimum.HasValue;
            hasMaximum = maximum.HasValue;
            this.minimum = minimum.GetValueOrDefault();
            this.maximum = maximum.GetValueOrDefault();
            this.roundDown = roundDown;
            this.allowed = allowed;
            this.formula = formula;
            valueKind = formula == null ? StatValueKind.Direct : StatValueKind.Derived;
        }

        internal StatDefinition Copy()
        {
            var copy = (StatDefinition)MemberwiseClone();
            copy.formula = Formula?.Copy();
            return copy;
        }
    }
}
