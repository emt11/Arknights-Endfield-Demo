using System;

namespace Endfield.Resources
{
    public sealed class SkillPointPool : IReadOnlySkillPoints
    {
        private readonly double maxSP;
        private readonly ResourceChanges<SkillPointSnapshot> changes = new();
        private double currentSP;

        public SkillPointSnapshot Snapshot => new(currentSP, maxSP);

        public SkillPointPool(double maxSP, double initialSP)
        {
            ResourcePoints.Validate(maxSP, nameof(maxSP));
            ResourcePoints.Validate(initialSP, nameof(initialSP), maxSP);
            this.maxSP = maxSP;
            currentSP = initialSP;
        }

        public IDisposable Subscribe(Action<SkillPointSnapshot, SkillPointSnapshot> listener) => changes.Subscribe(listener);

        public bool TryConsume(double cost)
        {
            ResourcePoints.Validate(cost, nameof(cost));
            if (cost > currentSP) return false;
            var previous = Snapshot;
            currentSP -= cost;
            if (currentSP != previous.CurrentSP) changes.Publish(previous, Snapshot);
            return true;
        }

        public double Restore(double amount)
        {
            ResourcePoints.Validate(amount, nameof(amount));
            var previous = Snapshot;
            currentSP = ResourcePoints.AfterGain(currentSP, amount, maxSP);
            double restored = currentSP - previous.CurrentSP;
            if (restored > 0) changes.Publish(previous, Snapshot);
            return restored;
        }
    }
}
