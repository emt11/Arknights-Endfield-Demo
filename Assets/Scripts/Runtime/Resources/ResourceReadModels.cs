using System;

namespace Endfield.Resources
{
    public interface IReadOnlyCharacterResources
    {
        ResourceSnapshot Snapshot { get; }
        IDisposable Subscribe(Action<ResourceSnapshot, ResourceSnapshot> listener);
    }

    public readonly struct ResourceSnapshot
    {
        public double CurrentHP { get; }
        public double MaxHP { get; }
        public double UltimateEnergy { get; }
        public double MaxUltimateEnergy { get; }
        public double Stagger { get; }
        public double StaggerThreshold { get; }

        internal ResourceSnapshot(double currentHP, double maxHP, double ultimateEnergy, double maxUltimateEnergy,
            double stagger, double staggerThreshold)
        {
            CurrentHP = currentHP;
            MaxHP = maxHP;
            UltimateEnergy = ultimateEnergy;
            MaxUltimateEnergy = maxUltimateEnergy;
            Stagger = stagger;
            StaggerThreshold = staggerThreshold;
        }
    }

    public readonly struct ResourceDamageResult
    {
        public double HPLost { get; }
        public double Overkill { get; }

        internal ResourceDamageResult(double hpLost, double overkill)
        {
            HPLost = hpLost;
            Overkill = overkill;
        }
    }

    public interface IReadOnlySkillPoints
    {
        SkillPointSnapshot Snapshot { get; }
        IDisposable Subscribe(Action<SkillPointSnapshot, SkillPointSnapshot> listener);
    }

    public readonly struct SkillPointSnapshot
    {
        public double CurrentSP { get; }
        public double MaxSP { get; }

        internal SkillPointSnapshot(double currentSP, double maxSP)
        {
            CurrentSP = currentSP;
            MaxSP = maxSP;
        }
    }
}
