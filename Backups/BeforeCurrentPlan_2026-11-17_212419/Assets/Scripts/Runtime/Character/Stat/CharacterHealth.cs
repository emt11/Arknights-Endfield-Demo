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
        private Action<Action> deferNotification;

        public float Current { get; private set; }
        public float Max => stats.MaxHP;
        public bool IsDead => Current <= 0f;

        public event Action<HealthChange> Changed;
        public event Action Died;
        internal event Action DiedInternally;

        public CharacterHealth(CharacterStats stats)
        {
            this.stats = stats;
            Current = Max;
            stats.ChangedInternally += OnStatChanged;
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
            if (died)
                DiedInternally?.Invoke();

            Notify(new HealthChange(previous, Current, Max, HealthChangeReason.Damage));
            if (died)
            {
                if (deferNotification == null)
                    Died?.Invoke();
                else
                    deferNotification(() => Died?.Invoke());
            }

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
                Notify(new HealthChange(previous, Current, Max, HealthChangeReason.Healing));

            return applied;
        }

        internal void SetNotificationQueue(Action<Action> defer) => deferNotification = defer;

        private void Notify(HealthChange change)
        {
            if (deferNotification == null)
                Changed?.Invoke(change);
            else
                deferNotification(() => Changed?.Invoke(change));
        }

        private void OnStatChanged(StatChange change)
        {
            if (change.Stat != StatId.MaxHP)
                return;

            float previous = Current;
            Current = Math.Min(Current, Max);
            Notify(new HealthChange(previous, Current, Max, HealthChangeReason.MaxHPChanged));
        }

        public void Dispose() => stats.ChangedInternally -= OnStatChanged;
    }
}
