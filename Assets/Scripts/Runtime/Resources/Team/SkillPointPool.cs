using System;
using Endfield.Common;

namespace Endfield.Resources
{
    public sealed class SkillPointPool : IReadOnlySkillPoints
    {
        private readonly double maxSP;
        private readonly ChangeNotifier<(SkillPointSnapshot Previous, SkillPointSnapshot Current)> changes = new();
        private double currentSP;

        public SkillPointSnapshot Snapshot => new(currentSP, maxSP);

        public SkillPointPool(double maxSP, double initialSP)
        {
            NumericValidation.RequireFiniteInRange(maxSP, 0d, double.MaxValue, nameof(maxSP));
            NumericValidation.RequireFiniteInRange(initialSP, 0d, maxSP, nameof(initialSP));
            this.maxSP = maxSP;
            currentSP = initialSP;
        }

        public IDisposable Subscribe(Action<SkillPointSnapshot, SkillPointSnapshot> listener)
        {
            return changes.Subscribe(listener, static (callback, change) => callback(change.Previous, change.Current));
        }

        public bool TryConsume(double cost)
        {
            NumericValidation.RequireFiniteInRange(cost, 0d, double.MaxValue, nameof(cost));
            if (cost > currentSP) return false;
            var previous = Snapshot;
            currentSP -= cost;
            if (currentSP != previous.CurrentSP) changes.Publish((previous, Snapshot));
            return true;
        }

        public double Restore(double amount)
        {
            NumericValidation.RequireFiniteInRange(amount, 0d, double.MaxValue, nameof(amount));
            var previous = Snapshot;
            currentSP = Math.Clamp(currentSP + amount, 0d, maxSP);
            double restored = currentSP - previous.CurrentSP;
            if (restored > 0) changes.Publish((previous, Snapshot));
            return restored;
        }
    }
}
