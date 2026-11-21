using System;
using System.Collections.Generic;
using System.Linq;

namespace Endfield.Stats
{
    // Only touched stats and source buckets are copied. Discarding this object rolls back the whole batch.
    internal sealed class StatTransaction
    {
        private readonly StatDependencyGraph graph;
        private readonly StatInstance[] original;
        private readonly HashSet<int> touched = new HashSet<int>();
        private readonly HashSet<SourceID> copiedSources = new HashSet<SourceID>();
        internal readonly StatInstance[] States;
        internal readonly Dictionary<SourceID, Dictionary<ulong, int>> Sources;

        internal StatTransaction(StatDependencyGraph graph, StatInstance[] original,
            Dictionary<SourceID, Dictionary<ulong, int>> sources)
        {
            this.graph = graph;
            this.original = original;
            States = (StatInstance[])original.Clone();
            Sources = new Dictionary<SourceID, Dictionary<ulong, int>>(sources);
        }

        internal StatCommandResult Apply(StatCommand command)
        {
            if (command == null) throw new ArgumentException("Null stat command");
            switch (command.Kind)
            {
                case StatCommand.CommandKind.SetBase:
                    int index = graph.Index(command.Id);
                    if (graph.Nodes[index].Definition.Formula != null)
                        throw new ArgumentException($"Cannot set derived stat: {command.Id}");
                    Edit(index).BaseValue = StatCalculator.Finite(command.Value, command.Id);
                    return new StatCommandResult(1);
                case StatCommand.CommandKind.Add:
                    Add(command.Modifier);
                    return new StatCommandResult(1, command.Modifier.Key);
                case StatCommand.CommandKind.Remove:
                    return new StatCommandResult(Remove(command.Key), command.Key);
                case StatCommand.CommandKind.RemoveSource:
                    return new StatCommandResult(RemoveSource(command.Source));
                case StatCommand.CommandKind.ReplaceSource:
                    RemoveSource(command.Source);
                    if (command.Entries == null) throw new ArgumentException($"Missing entries for {command.Source}");
                    foreach (var entry in command.Entries)
                    {
                        if (!entry.Key.Source.Equals(command.Source))
                            throw new ArgumentException($"Wrong source {entry.Key} for replacement {command.Source}");
                        Add(entry);
                    }
                    return new StatCommandResult(command.Entries.Length);
                default:
                    throw new ArgumentException($"Unknown stat command: {command.Kind}");
            }
        }

        internal bool[] FindChanges()
        {
            var dirty = new bool[States.Length];
            foreach (int i in touched)
            {
                var before = original[i];
                var after = States[i];
                dirty[i] = before.BaseValue != after.BaseValue || before.Modifiers.Count != after.Modifiers.Count ||
                    !before.Modifiers.SequenceEqual(after.Modifiers);
            }
            return dirty;
        }

        private StatInstance Edit(int index)
        {
            if (touched.Add(index)) States[index] = new StatInstance(original[index]);
            return States[index];
        }

        private Dictionary<ulong, int> EditSource(SourceID source)
        {
            if (!Sources.TryGetValue(source, out var entries))
            {
                entries = new Dictionary<ulong, int>();
                copiedSources.Add(source);
                Sources.Add(source, entries);
            }
            else if (copiedSources.Add(source))
            {
                entries = new Dictionary<ulong, int>(entries);
                Sources[source] = entries;
            }
            return entries;
        }

        private void Add(StatModifier modifier)
        {
            var key = modifier.Key;
            ValidateSource(key.Source);
            int index = graph.Index(modifier.StatId);
            var definition = graph.Nodes[index].Definition;
            UnitType unit;
            switch (modifier.Operation)
            {
                case ModifierOperation.Flat:
                case ModifierOperation.Override:
                    unit = definition.Unit;
                    break;
                case ModifierOperation.AdditivePercent:
                    unit = UnitType.Ratio;
                    break;
                case ModifierOperation.Multiplicative:
                    if (modifier.Value < 0) throw new ArgumentException($"Negative multiplier: {key}");
                    unit = UnitType.Multiplier;
                    break;
                default:
                    throw new ArgumentException($"Invalid operation {modifier.Operation} for {key}");
            }
            if ((definition.Allowed & modifier.Operation) == 0)
                throw new ArgumentException($"{modifier.Operation} is disabled for {definition.Id}, {key}");
            if (modifier.Unit.HasValue && modifier.Unit.Value != unit)
                throw new ArgumentException($"Unit mismatch for {definition.Id}, {key}: expected {unit}, got {modifier.Unit}");
            StatCalculator.Finite(modifier.Value, modifier.StatId);
            if (!EditSource(key.Source).TryAdd(key.Entry, index)) throw new ArgumentException($"Duplicate modifier: {key}");
            Edit(index).Modifiers.Add(key, new StatModifier(key, modifier.StatId, modifier.Operation, modifier.Value, modifier.Priority, unit));
        }

        private int Remove(ModifierKey key)
        {
            ValidateSource(key.Source);
            if (!Sources.TryGetValue(key.Source, out var entries) || !entries.TryGetValue(key.Entry, out int index)) return 0;
            Edit(index).Modifiers.Remove(key);
            var edited = EditSource(key.Source);
            edited.Remove(key.Entry);
            if (edited.Count == 0) Sources.Remove(key.Source);
            return 1;
        }

        private int RemoveSource(SourceID source)
        {
            ValidateSource(source);
            if (!Sources.TryGetValue(source, out var entries)) return 0;
            foreach (var entry in entries)
                Edit(entry.Value).Modifiers.Remove(new ModifierKey(source, entry.Key));
            Sources.Remove(source);
            return entries.Count;
        }

        private static void ValidateSource(SourceID source)
        {
            if (string.IsNullOrWhiteSpace(source.Domain)) throw new ArgumentException("Source domain is required");
        }
    }
}
