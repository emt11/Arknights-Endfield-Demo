using System;
using System.Collections.Generic;
using Endfield.Stats;

namespace Endfield.Resources
{
    public sealed class ResourceContainer : IReadOnlyCharacterResources, IDisposable
    {
        private readonly double maxUltimateEnergy;
        private readonly double staggerThreshold;
        private readonly ResourceChanges<ResourceSnapshot> changes = new();
        private readonly IDisposable statsSubscription;
        private double currentHP;
        private double maxHP;
        private double ultimateEnergy;
        private double stagger;

        public ResourceSnapshot Snapshot => new(currentHP, maxHP, ultimateEnergy, maxUltimateEnergy, stagger, staggerThreshold);

        public ResourceContainer(IReadOnlyStatSet stats, double maxUltimateEnergy, double initialUltimateEnergy,
            double staggerThreshold)
        {
            if (stats == null) throw new ArgumentNullException(nameof(stats));
            ResourcePoints.Validate(maxUltimateEnergy, nameof(maxUltimateEnergy));
            ResourcePoints.Validate(initialUltimateEnergy, nameof(initialUltimateEnergy), maxUltimateEnergy);
            ResourcePoints.Validate(staggerThreshold, nameof(staggerThreshold));

            this.maxUltimateEnergy = maxUltimateEnergy;
            this.staggerThreshold = staggerThreshold;
            ultimateEnergy = initialUltimateEnergy;
            maxHP = stats.GetValue(StatId.MaxHP);
            currentHP = maxHP;
            statsSubscription = stats.Subscribe(OnStatsChanged);
        }

        public IDisposable Subscribe(Action<ResourceSnapshot, ResourceSnapshot> listener) => changes.Subscribe(listener);

        public void Dispose() => statsSubscription.Dispose();

        public ResourceDamageResult ApplyDamage(double finalDamage)
        {
            ResourcePoints.Validate(finalDamage, nameof(finalDamage));
            var previous = Snapshot;
            double hpLost = Math.Min(currentHP, finalDamage);
            currentHP -= hpLost;
            if (currentHP != previous.CurrentHP) changes.Publish(previous, Snapshot);
            return new ResourceDamageResult(hpLost, finalDamage - hpLost);
        }

        public double RestoreHP(double finalHealing)
        {
            ResourcePoints.Validate(finalHealing, nameof(finalHealing));
            var previous = Snapshot;
            currentHP = ResourcePoints.AfterGain(currentHP, finalHealing, maxHP);
            double restored = currentHP - previous.CurrentHP;
            if (restored > 0) changes.Publish(previous, Snapshot);
            return restored;
        }

        public bool TryConsumeUltimate(double cost)
        {
            ResourcePoints.Validate(cost, nameof(cost));
            if (cost > ultimateEnergy) return false;
            var previous = Snapshot;
            ultimateEnergy -= cost;
            if (ultimateEnergy != previous.UltimateEnergy) changes.Publish(previous, Snapshot);
            return true;
        }

        public double RestoreUltimate(double amount)
        {
            ResourcePoints.Validate(amount, nameof(amount));
            var previous = Snapshot;
            ultimateEnergy = ResourcePoints.AfterGain(ultimateEnergy, amount, maxUltimateEnergy);
            double restored = ultimateEnergy - previous.UltimateEnergy;
            if (restored > 0) changes.Publish(previous, Snapshot);
            return restored;
        }

        public bool AddStagger(double amount)
        {
            ResourcePoints.Validate(amount, nameof(amount));
            var previous = Snapshot;
            stagger = ResourcePoints.AfterGain(stagger, amount, staggerThreshold);
            if (stagger == previous.Stagger) return false;
            bool reachedThreshold = stagger == staggerThreshold;
            changes.Publish(previous, Snapshot);
            return reachedThreshold;
        }

        public void ResetStagger()
        {
            if (stagger == 0) return;
            var previous = Snapshot;
            stagger = 0;
            changes.Publish(previous, Snapshot);
        }

        private void OnStatsChanged(IReadOnlyList<StatChange> statChanges)
        {
            foreach (var change in statChanges)
            {
                if (change.StatId != StatId.MaxHP) continue;
                var previous = Snapshot;
                maxHP = change.NewValue;
                currentHP = Math.Min(currentHP, maxHP);
                changes.Publish(previous, Snapshot);
                return;
            }
        }
    }
}
