using System;
using System.Collections.Generic;
using Endfield.Stats;

namespace Endfield.CharacterBuild
{
    public enum BuildSlot { Weapon, Gear }

    public sealed class CharacterBuildState
    {
        private static readonly SourceID BuildSource = new("CharacterBuild", 0);
        private readonly StatSet stats;
        private readonly CharacterBuildProfile profile;
        private BuildModifierProfile weapon;
        private BuildModifierProfile gear;
        private List<BuildModifierProfile> talents;

        public int Level { get; private set; }

        private CharacterBuildState(StatSet stats, CharacterBuildProfile profile)
        {
            this.stats = stats;
            this.profile = profile;
        }

        public static CharacterBuildState Create(StatSet stats, CharacterBuildProfile profile)
        {
            if (stats == null) throw new ArgumentNullException(nameof(stats));
            if (!profile) throw new ArgumentNullException(nameof(profile));
            try
            {
                profile.ValidateGrowth();
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException($"{profile.name} 的成长配置有误：{exception.Message}", exception);
            }

            var initialTalents = new List<BuildModifierProfile>();
            foreach (var talent in profile.InitialTalents)
            {
                if (!initialTalents.Contains(talent)) initialTalents.Add(talent);
            }

            var state = new CharacterBuildState(stats, profile);
            if (!state.TryCommit(profile.InitialLevel, profile.InitialWeapon, profile.InitialGear, initialTalents, out var error))
                throw new InvalidOperationException($"{profile.name} 的进场构筑有误：{error}");
            return state;
        }

        public BuildModifierProfile GetEquipped(BuildSlot slot) => slot switch
        {
            BuildSlot.Weapon => weapon,
            BuildSlot.Gear => gear,
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "未知的装备槽位")
        };

        public bool IsTalentEnabled(BuildModifierProfile talent) => talents.Contains(talent);

        public bool TrySetLevel(int level, out string error) => TryCommit(level, weapon, gear, talents, out error);

        public bool TryEquip(BuildSlot slot, BuildModifierProfile item, out string error)
        {
            switch (slot)
            {
                case BuildSlot.Weapon: return TryCommit(Level, item, gear, talents, out error);
                case BuildSlot.Gear: return TryCommit(Level, weapon, item, talents, out error);
                default:
                    error = $"未知的装备槽位 {slot}";
                    return false;
            }
        }

        public bool TrySetTalentEnabled(BuildModifierProfile talent, bool enabled, out string error)
        {
            error = TalentError(talent);
            if (error != null) return false;
            if (talents.Contains(talent) == enabled) return true;

            var nextTalents = new List<BuildModifierProfile>(talents);
            if (enabled) nextTalents.Add(talent);
            else nextTalents.Remove(talent);
            return TryCommit(Level, weapon, gear, nextTalents, out error);
        }

        private static string TalentError(BuildModifierProfile talent)
        {
            if (!talent) return "缺少天赋资产";
            return talent.Kind == BuildModifierKind.Talent ? null : $"{talent.name} 不是被动天赋";
        }

        private bool TryCommit(int level, BuildModifierProfile nextWeapon, BuildModifierProfile nextGear,
            List<BuildModifierProfile> nextTalents, out string error)
        {
            if (level < 1 || level > profile.MaxLevel)
            {
                error = $"等级 {level} 超出范围 [1, {profile.MaxLevel}]";
                return false;
            }
            if (nextWeapon && nextWeapon.Kind != BuildModifierKind.Weapon)
            {
                error = $"{nextWeapon.name} 无法放入武器槽";
                return false;
            }
            if (nextGear && nextGear.Kind != BuildModifierKind.Gear)
            {
                error = $"{nextGear.name} 无法放入装备槽";
                return false;
            }

            var modifiers = new List<StatModifier>();
            profile.AppendGrowth(level, modifiers);
            if (nextWeapon) nextWeapon.AppendModifiers(modifiers);
            if (nextGear) nextGear.AppendModifiers(modifiers);
            foreach (var talent in nextTalents)
            {
                error = TalentError(talent);
                if (error != null) return false;
                talent.AppendModifiers(modifiers);
            }

            // 整组替换让生命上限直接从旧构筑过渡到新构筑
            var result = stats.ReplaceSource(BuildSource, modifiers.ToArray());
            error = result.Error;
            if (!result.Success) return false;

            Level = level;
            weapon = nextWeapon;
            gear = nextGear;
            talents = nextTalents;
            return true;
        }
    }
}
