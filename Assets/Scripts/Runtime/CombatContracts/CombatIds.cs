using System;

namespace Endfield.CombatContracts
{
    // 	标识本场战斗中的一个角色实例
    public readonly struct ActorId : IEquatable<ActorId>
    {
        public uint Value { get; }

        public ActorId(uint value)
        {
            Value = value;
        }

        public bool Equals(ActorId other) => Value.Equals(other.Value);

        public override bool Equals(object obj) => obj is ActorId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();
    }

    // 标识某个角色发起的某一次攻击
    public readonly struct AttackId : IEquatable<AttackId>
    {
        public ActorId Attacker { get; }
        public uint Sequence { get; }

        public AttackId(ActorId attacker, uint sequence)
        {
            Attacker = attacker;
            Sequence = sequence;
        }

        public bool Equals(AttackId other) => Attacker.Equals(other.Attacker) && Sequence.Equals(other.Sequence);

        public override bool Equals(object obj) => obj is AttackId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Attacker, Sequence);
    }

    // 	标识一次攻击内的某一段命中窗口
    public readonly struct HitWindowId : IEquatable<HitWindowId>
    {
        public uint Value { get; }

        public HitWindowId(uint value)
        {
            Value = value;
        }

        public bool Equals(HitWindowId other) => Value.Equals(other.Value);

        public override bool Equals(object obj) => obj is HitWindowId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();
    }
}
