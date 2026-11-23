using System;
using Endfield.Stats;

namespace Endfield.Effects
{
    public readonly struct EffectHandle : IEquatable<EffectHandle>
    {
        internal EffectController Owner { get; }
        public ulong Sequence { get; }

        internal EffectHandle(EffectController owner, ulong sequence)
        {
            Owner = owner;
            Sequence = sequence;
        }

        public bool Equals(EffectHandle other) => Owner == other.Owner && Sequence == other.Sequence;

        public override bool Equals(object obj) => obj is EffectHandle other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Owner, Sequence);
    }

    public readonly struct EffectPulse
    {
        public EffectHandle Handle { get; }
        public IReadOnlyStatSet Instigator { get; }
        public PulseKind Kind { get; }
        public double Amount { get; }

        internal EffectPulse(EffectHandle handle, IReadOnlyStatSet instigator, PulseKind kind, double amount)
        {
            Handle = handle;
            Instigator = instigator;
            Kind = kind;
            Amount = amount;
        }
    }
}
