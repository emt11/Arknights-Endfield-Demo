using System;

namespace Endfield.Character
{
    public enum StatId { Attack, MaxHP, Defense }

    public readonly struct StatChange
    {
        public StatId Stat { get; }
        public float Previous { get; }
        public float Current { get; }

        public StatChange(StatId stat, float previous, float current)
        {
            Stat = stat;
            Previous = previous;
            Current = current;
        }
    }

    public sealed class CharacterStats
    {
        private readonly Stat attack;
        private readonly Stat maxHP;
        private readonly Stat defense;

        public float Attack => attack.Value;
        public float MaxHP => maxHP.Value;
        public float Defense => defense.Value;
        public event Action<StatChange> Changed;

        public CharacterStats(float attack, float maxHP, float defense)
        {
            this.attack = new Stat(attack, 0f);
            this.maxHP = new Stat(maxHP, 1f);
            this.defense = new Stat(defense, 0f);

            this.attack.Changed += (previous, current) => NotifyChange(StatId.Attack, previous, current);
            this.maxHP.Changed += (previous, current) => NotifyChange(StatId.MaxHP, previous, current);
            this.defense.Changed += (previous, current) => NotifyChange(StatId.Defense, previous, current);
        }

        public Stat GetStat(StatId stat)
        {
            switch (stat)
            {
                case StatId.Attack: return attack;
                case StatId.MaxHP: return maxHP;
                case StatId.Defense: return defense;
                default: throw new ArgumentOutOfRangeException(nameof(stat));
            }
        }

        public float GetBaseValue(StatId stat) => GetStat(stat).BaseValue;
        public float GetValue(StatId stat) => GetStat(stat).Value;

        private void NotifyChange(StatId stat, float previous, float current) => Changed?.Invoke(new StatChange(stat, previous, current));
    }
}
