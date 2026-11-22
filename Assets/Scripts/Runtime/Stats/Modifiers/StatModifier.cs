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

    public enum ModifierOperation
    {
        Flat = 1, AdditivePercent = 2, Multiplicative = 4, Override = 8
    }

    public readonly struct StatModifier
    {
        public StatId StatId { get; }
        public ModifierOperation Operation { get; }
        public double Value { get; }
        public int Priority { get; }

        public StatModifier(StatId statId, ModifierOperation operation, double value, int priority = 0)
        {
            StatId = statId;
            Operation = operation;
            Value = value;
            Priority = priority;
        }

        internal void Validate()
        {
            StatCatalog.Get(StatId);
            if (Operation != ModifierOperation.Flat && Operation != ModifierOperation.AdditivePercent &&
                Operation != ModifierOperation.Multiplicative && Operation != ModifierOperation.Override)
                throw new ArgumentException($"Invalid modifier operation: {Operation}");
            if (!double.IsFinite(Value) || (Operation == ModifierOperation.Multiplicative && Value < 0))
                throw new ArgumentException($"Invalid {Operation} value for {StatId}: {Value}");
        }
    }
}
