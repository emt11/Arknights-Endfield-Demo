using System;

namespace Endfield.Character
{
    public enum HealthChangeReason
    {
        Damage,
        Healing,
        MaxHPChanged
    }

    public readonly struct HealthChange
    {
        public float Previous { get; }
        public float Current { get; }
        public float Max { get; }
        public HealthChangeReason Reason { get; }

        public HealthChange(float previous, float current, float max, HealthChangeReason reason)
        {
            Previous = previous;
            Current = current;
            Max = max;
            Reason = reason;
        }
    }

    public sealed class CharacterHealth : IDisposable
    {
        private readonly CharacterStats stats;

        public float Current { get; private set; }
        public float Max => stats.MaxHP;
        public bool IsDead => Current <= 0f;

        public event Action<HealthChange> Changed;
        public event Action Died;

        public CharacterHealth(CharacterStats stats)
        {
            this.stats = stats;
            Current = Max;
            stats.Changed += OnStatChanged;
        }

        public float ApplyDamage(float amount)
        {
            if (amount <= 0f || IsDead)
                return 0f;

            float previous = Current;
            Current = Math.Max(0f, previous - amount);
            float applied = previous - Current;
            if (applied == 0f)
                return 0f;

            bool died = IsDead;
            Changed?.Invoke(new HealthChange(previous, Current, Max, HealthChangeReason.Damage));
            if (died)
                Died?.Invoke();

            return applied;
        }

        public float Heal(float amount)
        {
            if (amount <= 0f || IsDead)
                return 0f;

            float previous = Current;
            Current = Math.Min(Max, previous + amount);
            float applied = Current - previous;
            if (applied > 0f)
                Changed?.Invoke(new HealthChange(previous, Current, Max, HealthChangeReason.Healing));

            return applied;
        }

        private void OnStatChanged(StatChange change)
        {
            if (change.Stat != StatId.MaxHP)
                return;

            float previous = Current;
            Current = Math.Min(Current, Max);
            Changed?.Invoke(new HealthChange(previous, Current, Max, HealthChangeReason.MaxHPChanged));
        }

        public void Dispose() => stats.Changed -= OnStatChanged;
    }
}
