using System;

namespace Endfield.Resources
{
    public interface IReadOnlyCharacterResources
    {
        CharacterResourceSnapshot Snapshot { get; }
        IDisposable Subscribe(Action<CharacterResourceSnapshot, CharacterResourceSnapshot> listener);
    }

    public readonly struct CharacterResourceSnapshot
    {
        public double CurrentHP { get; }
        public double MaxHP { get; }
        public double UltimateEnergy { get; }
        public double MaxUltimateEnergy { get; }
        public double Stagger { get; }
        public double StaggerThreshold { get; }

        internal CharacterResourceSnapshot(double currentHP, double maxHP, double ultimateEnergy, double maxUltimateEnergy,
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

    public readonly struct HealthLossResult
    {
        public double HPLost { get; }
        public double Overkill { get; }

        internal HealthLossResult(double hpLost, double overkill)
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
