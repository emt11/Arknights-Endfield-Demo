using System;
using Endfield.Common;
using UnityEngine;

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

    [Serializable]
    public struct StatModifier
    {
        [SerializeField] private StatId statId;
        [SerializeField] private ModifierOperation operation;
        [SerializeField] private double value;
        [SerializeField] private int priority;

        public StatId StatId => statId;
        public ModifierOperation Operation => operation;
        public double Value => value;
        public int Priority => priority;

        public StatModifier(StatId statId, ModifierOperation operation, double value, int priority = 0)
        {
            this.statId = statId;
            this.operation = operation;
            this.value = value;
            this.priority = priority;
        }

        internal void Validate()
        {
            StatCatalog.Get(StatId);
            if (Operation != ModifierOperation.Flat && Operation != ModifierOperation.AdditivePercent &&
                Operation != ModifierOperation.Multiplicative && Operation != ModifierOperation.Override)
                throw new ArgumentException($"Invalid modifier operation: {Operation}");
            bool validValue = Operation == ModifierOperation.Multiplicative
                ? NumericValidation.IsFiniteInRange(Value, 0, double.MaxValue)
                : double.IsFinite(Value);
            if (!validValue)
                throw new ArgumentException($"Invalid {Operation} value for {StatId}: {Value}");
        }
    }
}
