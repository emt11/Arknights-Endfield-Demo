using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Endfield.Stats
{
    public interface IReadOnlyStatSet
    {
        long Revision { get; }
        double GetValue(string statId);
        bool TryGetValue(string statId, out double value);
        StatSnapshot Snapshot(params string[] statIds);
        IReadOnlyList<StatModifier> GetModifiers(string statId);
        StatBreakdown GetBreakdown(string statId);
        IDisposable Subscribe(Action<StatChangeSet> listener);
    }

    public sealed class StatSnapshot
    {
        public Guid StatSetId { get; }
        public long Revision { get; }
        public IReadOnlyDictionary<string, double> Values { get; }

        internal StatSnapshot(Guid id, long revision, Dictionary<string, double> values)
        {
            StatSetId = id;
            Revision = revision;
            Values = new ReadOnlyDictionary<string, double>(values);
        }
    }

    public readonly struct StatChange
    {
        public string StatId { get; }
        public double OldValue { get; }
        public double NewValue { get; }

        internal StatChange(string statId, double oldValue, double newValue)
        {
            StatId = statId;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }

    public sealed class StatChangeSet
    {
        public Guid StatSetId { get; }
        public long Revision { get; }
        public IReadOnlyList<StatChange> Changes { get; }

        internal StatChangeSet(Guid id, long revision, List<StatChange> changes)
        {
            StatSetId = id;
            Revision = revision;
            Changes = changes.AsReadOnly();
        }
    }

    public readonly struct StatStep
    {
        public string Stage { get; }
        public double Value { get; }

        internal StatStep(string stage, double value)
        {
            Stage = stage;
            Value = value;
        }
    }

    public sealed class StatBreakdown
    {
        public Guid StatSetId { get; }
        public long Revision { get; }
        public string StatId { get; }
        public UnitType Unit { get; }
        public string FormulaId { get; }
        public IReadOnlyDictionary<string, StatBreakdown> Inputs { get; }
        public IReadOnlyList<StatModifier> Modifiers { get; }
        public IReadOnlyList<StatStep> Steps { get; }
        public ModifierKey? WinningOverride { get; }
        public double Value => Steps[Steps.Count - 1].Value;

        internal StatBreakdown(Guid id, long revision, StatDefinition definition, Dictionary<string, StatBreakdown> inputs,
            IReadOnlyList<StatModifier> modifiers, List<StatStep> steps, ModifierKey? winner)
        {
            StatSetId = id;
            Revision = revision;
            StatId = definition.Id;
            Unit = definition.Unit;
            FormulaId = definition.Formula?.Id;
            Inputs = new ReadOnlyDictionary<string, StatBreakdown>(inputs);
            Modifiers = modifiers;
            Steps = steps.AsReadOnly();
            WinningOverride = winner;
        }
    }
}
