using System;
using System.Collections.Generic;
using Endfield.Common;
using Endfield.Stats;

namespace Endfield.Resources
{
    public sealed class CharacterResourceContainer : IReadOnlyCharacterResources, IDisposable
    {
        private readonly double maxUltimateEnergy;
        private readonly double staggerThreshold;
        private readonly ChangeNotifier<(CharacterResourceSnapshot Previous, CharacterResourceSnapshot Current)> changes = new();
        private readonly IDisposable statsSubscription;
        private double currentHP;
        private double maxHP;
        private double ultimateEnergy;
        private double stagger;

        public CharacterResourceSnapshot Snapshot => new(currentHP, maxHP, ultimateEnergy, maxUltimateEnergy, stagger, staggerThreshold);

        public CharacterResourceContainer(IReadOnlyStatSet stats, double maxUltimateEnergy, double initialUltimateEnergy,
            double staggerThreshold)
        {
            if (stats == null) throw new ArgumentNullException(nameof(stats));
            NumericValidation.RequireFiniteInRange(maxUltimateEnergy, 0d, double.MaxValue, nameof(maxUltimateEnergy));
            NumericValidation.RequireFiniteInRange(initialUltimateEnergy, 0d, maxUltimateEnergy, nameof(initialUltimateEnergy));
            NumericValidation.RequireFiniteInRange(staggerThreshold, 0d, double.MaxValue, nameof(staggerThreshold));

            this.maxUltimateEnergy = maxUltimateEnergy;
            this.staggerThreshold = staggerThreshold;
            ultimateEnergy = initialUltimateEnergy;
            maxHP = stats.GetValue(StatId.MaxHP);
            currentHP = maxHP;
            statsSubscription = stats.Subscribe(OnStatsChanged);
        }

        public IDisposable Subscribe(Action<CharacterResourceSnapshot, CharacterResourceSnapshot> listener)
        {
            return changes.Subscribe(listener, static (callback, change) => callback(change.Previous, change.Current));
        }

        public void Dispose() => statsSubscription.Dispose();

        public HealthLossResult ApplyDamage(double finalDamage)
        {
            NumericValidation.RequireFiniteInRange(finalDamage, 0d, double.MaxValue, nameof(finalDamage));
            var previous = Snapshot;
            double hpLost = Math.Min(currentHP, finalDamage);
            currentHP -= hpLost;
            if (currentHP != previous.CurrentHP) changes.Publish((previous, Snapshot));
            return new HealthLossResult(hpLost, finalDamage - hpLost);
        }

        public double RestoreHP(double finalHealing)
        {
            NumericValidation.RequireFiniteInRange(finalHealing, 0d, double.MaxValue, nameof(finalHealing));
            var previous = Snapshot;
            currentHP = Math.Clamp(currentHP + finalHealing, 0d, maxHP);
            double restored = currentHP - previous.CurrentHP;
            if (restored > 0) changes.Publish((previous, Snapshot));
            return restored;
        }

        public bool TryConsumeUltimate(double cost)
        {
            NumericValidation.RequireFiniteInRange(cost, 0d, double.MaxValue, nameof(cost));
            if (cost > ultimateEnergy) return false;
            var previous = Snapshot;
            ultimateEnergy -= cost;
            if (ultimateEnergy != previous.UltimateEnergy) changes.Publish((previous, Snapshot));
            return true;
        }

        public double RestoreUltimate(double amount)
        {
            NumericValidation.RequireFiniteInRange(amount, 0d, double.MaxValue, nameof(amount));
            var previous = Snapshot;
            ultimateEnergy = Math.Clamp(ultimateEnergy + amount, 0d, maxUltimateEnergy);
            double restored = ultimateEnergy - previous.UltimateEnergy;
            if (restored > 0) changes.Publish((previous, Snapshot));
            return restored;
        }

        public bool AddStagger(double amount)
        {
            NumericValidation.RequireFiniteInRange(amount, 0d, double.MaxValue, nameof(amount));
            var previous = Snapshot;
            stagger = Math.Clamp(stagger + amount, 0d, staggerThreshold);
            if (stagger == previous.Stagger) return false;
            bool reachedThreshold = stagger == staggerThreshold;
            changes.Publish((previous, Snapshot));
            return reachedThreshold;
        }

        public void ResetStagger()
        {
            if (stagger == 0) return;
            var previous = Snapshot;
            stagger = 0;
            changes.Publish((previous, Snapshot));
        }

        private void OnStatsChanged(IReadOnlyList<StatChange> statChanges)
        {
            foreach (var change in statChanges)
            {
                if (change.StatId != StatId.MaxHP) continue;
                var previous = Snapshot;
                maxHP = change.NewValue;
                currentHP = Math.Min(currentHP, maxHP);
                changes.Publish((previous, Snapshot));
                return;
            }
        }
    }
}
