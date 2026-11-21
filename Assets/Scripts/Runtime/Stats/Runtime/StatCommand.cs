using System;
using System.Collections.Generic;
using System.Linq;

namespace Endfield.Stats
{
    public sealed class StatCommand
    {
        internal enum CommandKind { SetBase, Add, Remove, ReplaceSource, RemoveSource }
        internal CommandKind Kind { get; }
        internal string Id { get; private set; }
        internal double Value { get; private set; }
        internal StatModifier Modifier { get; private set; }
        internal ModifierKey Key { get; private set; }
        internal SourceID Source { get; private set; }
        internal StatModifier[] Entries { get; private set; }

        private StatCommand(CommandKind kind)
        {
            Kind = kind;
        }

        public static StatCommand SetBase(string statId, double value) =>
            new StatCommand(CommandKind.SetBase) { Id = statId, Value = value };

        public static StatCommand AddModifier(StatModifier modifier) =>
            new StatCommand(CommandKind.Add) { Modifier = modifier };

        public static StatCommand RemoveModifier(ModifierKey key) =>
            new StatCommand(CommandKind.Remove) { Key = key };

        public static StatCommand ReplaceSource(SourceID source, params StatModifier[] entries) =>
            new StatCommand(CommandKind.ReplaceSource) { Source = source, Entries = entries?.ToArray() };

        public static StatCommand RemoveSource(SourceID source) =>
            new StatCommand(CommandKind.RemoveSource) { Source = source };
    }

    public readonly struct StatCommandResult
    {
        public int Count { get; }
        public ModifierKey? Key { get; }

        internal StatCommandResult(int count, ModifierKey? key = null)
        {
            Count = count;
            Key = key;
        }
    }

    public sealed class StatCommitResult
    {
        public bool Success => Error == null;
        public string Error { get; }
        public long Revision { get; }
        public IReadOnlyList<StatCommandResult> Results { get; }

        internal StatCommitResult(long revision, StatCommandResult[] results, string error = null)
        {
            Revision = revision;
            Error = error;
            Results = Array.AsReadOnly(results);
        }
    }
}
