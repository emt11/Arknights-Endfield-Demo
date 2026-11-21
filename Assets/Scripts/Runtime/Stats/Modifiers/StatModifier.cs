using System;

namespace Endfield.Stats
{
    public readonly struct SourceID : IComparable<SourceID>, IEquatable<SourceID>
    {
        public string Domain { get; }
        public ulong Instance { get; }

        public SourceID(string domain, ulong instance)
        {
            if (string.IsNullOrWhiteSpace(domain)) throw new ArgumentException("Source domain is required");
            Domain = domain;
            Instance = instance;
        }

        public int CompareTo(SourceID other)
        {
            int domainOrder = string.CompareOrdinal(Domain, other.Domain);
            return domainOrder != 0 ? domainOrder : Instance.CompareTo(other.Instance);
        }

        public bool Equals(SourceID other) => Domain == other.Domain && Instance == other.Instance;

        public override bool Equals(object obj) => obj is SourceID other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Domain, Instance);

        public override string ToString() => $"{Domain}/{Instance}";
    }

    public readonly struct ModifierKey : IComparable<ModifierKey>, IEquatable<ModifierKey>
    {
        public SourceID Source { get; }
        public ulong Entry { get; }

        public ModifierKey(SourceID source, ulong entry)
        {
            Source = source;
            Entry = entry;
        }

        public int CompareTo(ModifierKey other)
        {
            int sourceOrder = Source.CompareTo(other.Source);
            return sourceOrder != 0 ? sourceOrder : Entry.CompareTo(other.Entry);
        }

        public bool Equals(ModifierKey other) => Source.Equals(other.Source) && Entry == other.Entry;

        public override bool Equals(object obj) => obj is ModifierKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Source, Entry);

        public override string ToString() => $"{Source}#{Entry}";
    }

    public readonly struct StatModifier
    {
        public ModifierKey Key { get; }
        public string StatId { get; }
        public ModifierOperation Operation { get; }
        public double Value { get; }
        public int Priority { get; }
        public UnitType? Unit { get; }

        public StatModifier(ModifierKey key, string statId, ModifierOperation operation, double value,
            int priority = 0, UnitType? unit = null)
        {
            Key = key;
            StatId = statId;
            Operation = operation;
            Value = value;
            Priority = priority;
            Unit = unit;
        }
    }
}
