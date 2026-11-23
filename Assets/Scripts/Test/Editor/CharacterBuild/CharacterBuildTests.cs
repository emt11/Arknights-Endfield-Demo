using System;
using System.Collections.Generic;
using Endfield.Resources;
using Endfield.Stats;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Endfield.CharacterBuild.Tests
{
    public sealed class CharacterBuildTests
    {
        private const string Folder = "Assets/ScriptObjects/CharacterBuild/";
        private CharacterBuildProfile profile;
        private BuildModifierProfile sword;
        private BuildModifierProfile charm;
        private BuildModifierProfile talent;
        private StatSet stats;
        private readonly List<Object> copies = new();

        [SetUp]
        public void LoadProfiles()
        {
            profile = AssetDatabase.LoadAssetAtPath<CharacterBuildProfile>(Folder + "Phoebe.asset");
            sword = AssetDatabase.LoadAssetAtPath<BuildModifierProfile>(Folder + "WoodenSword.asset");
            charm = AssetDatabase.LoadAssetAtPath<BuildModifierProfile>(Folder + "GuardCharm.asset");
            talent = AssetDatabase.LoadAssetAtPath<BuildModifierProfile>(Folder + "CriticalTalent.asset");
            stats = AssetDatabase.LoadAssetAtPath<CharacterStatProfile>("Assets/ScriptObjects/Stats/Phoebe.asset").CreateSet();
        }

        [TearDown]
        public void ReleaseCopies()
        {
            foreach (var copy in copies) Object.DestroyImmediate(copy);
            copies.Clear();
        }

        private T Copy<T>(T asset) where T : ScriptableObject
        {
            var copy = Object.Instantiate(asset);
            copies.Add(copy);
            return copy;
        }

        [Test]
        public void LevelEquipmentAndTalentsContributeIndependently()
        {
            var build = CharacterBuildState.Create(stats, profile);
            CheckPanel(1, 10, 100, 200, 704, 150);
            Assert.That(build.TrySetLevel(2, out var error), Is.True, error);
            CheckPanel(2, 12, 110, 200, 704, 170);
            Assert.That(build.TryEquip(BuildSlot.Weapon, sword, out error), Is.True, error);
            CheckPanel(2, 15, 110, 203, 710.24, 185);
            Assert.That(build.TrySetLevel(3, out error), Is.True, error);
            CheckPanel(3, 17, 120, 203, 710.24, 205);
            Assert.That(build.TrySetLevel(1, out error), Is.True, error);
            CheckPanel(1, 13, 100, 203, 710.24, 165);

            Assert.That(build.TryEquip(BuildSlot.Gear, charm, out error), Is.True, error);
            Assert.That(build.TrySetTalentEnabled(talent, true, out error), Is.True, error);
            Assert.That(build.TrySetTalentEnabled(talent, true, out error), Is.True, error);
            Assert.That(stats.GetValue(StatId.DEF), Is.EqualTo(108));
            Assert.That(stats.GetValue(StatId.CritDamage), Is.EqualTo(1.70).Within(1e-9));
            Assert.That(build.TryEquip(BuildSlot.Gear, null, out error), Is.True, error);
            Assert.That(build.GetEquipped(BuildSlot.Gear), Is.Null);
            Assert.That(stats.GetValue(StatId.DEF), Is.EqualTo(100));
            Assert.That(build.IsTalentEnabled(talent), Is.True);
            Assert.That(stats.GetValue(StatId.CritDamage), Is.EqualTo(1.70).Within(1e-9));
            Assert.That(build.TrySetTalentEnabled(talent, false, out error), Is.True, error);
            Assert.That(build.IsTalentEnabled(talent), Is.False);
            Assert.That(stats.GetValue(StatId.CritDamage), Is.EqualTo(1.50));

            void CheckPanel(int level, double strength, double baseHP, double weaponATK, double attack, double maxHP)
            {
                Assert.That(build.Level, Is.EqualTo(level));
                Assert.That(stats.GetValue(StatId.STR), Is.EqualTo(strength));
                Assert.That(stats.GetValue(StatId.BaseHP), Is.EqualTo(baseHP));
                Assert.That(stats.GetValue(StatId.WeaponATK), Is.EqualTo(weaponATK));
                Assert.That(stats.GetValue(StatId.ATK), Is.EqualTo(attack).Within(1e-9));
                Assert.That(stats.GetValue(StatId.MaxHP), Is.EqualTo(maxHP));
            }
        }

        [Test]
        public void SwapPublishesFinalHealthAndFailedWritesRetainThePreviousBuild()
        {
            var build = CharacterBuildState.Create(stats, profile);
            Assert.That(build.TryEquip(BuildSlot.Weapon, sword, out var error), Is.True, error);
            using var resources = new CharacterResourceContainer(stats, 100, 0, 0);
            var health = new List<(double, double, double, double)>();
            using var resourceSubscription = resources.Subscribe((old, current) =>
                health.Add((old.CurrentHP, old.MaxHP, current.CurrentHP, current.MaxHP)));

            var strongerSword = Copy(sword);
            var swordData = new SerializedObject(strongerSword);
            swordData.FindProperty("entries").GetArrayElementAtIndex(1).FindPropertyRelative("value").doubleValue = 5;
            swordData.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(build.TryEquip(BuildSlot.Weapon, strongerSword, out error), Is.True, error);
            Assert.That(health, Is.EqualTo(new[] { (165d, 165d, 165d, 175d) }));

            var effect = new SourceID("Effect", 42);
            Assert.That(stats.ReplaceSource(effect, new StatModifier(StatId.DEF, ModifierOperation.Override, 140, 5)).Success,
                Is.True);
            int panelChanges = 0;
            using var statsSubscription = stats.Subscribe(_ => panelChanges++);
            var equalSword = Copy(strongerSword);
            Assert.That(build.TryEquip(BuildSlot.Weapon, equalSword, out error), Is.True, error);
            Assert.That(build.GetEquipped(BuildSlot.Weapon), Is.SameAs(equalSword));

            var conflictingGear = Copy(charm);
            var gearData = new SerializedObject(conflictingGear);
            var entry = gearData.FindProperty("entries").GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("statId").intValue = (int)StatId.DEF;
            entry.FindPropertyRelative("operation").intValue = (int)ModifierOperation.Override;
            entry.FindPropertyRelative("priority").intValue = 5;
            gearData.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(build.TryEquip(BuildSlot.Gear, conflictingGear, out error), Is.False);
            Assert.That(error, Does.Contain("DEF"));
            Assert.That(build.TryEquip(BuildSlot.Weapon, talent, out error), Is.False);
            int maxLevel = new SerializedObject(profile).FindProperty("maxLevel").intValue;
            Assert.That(build.TrySetLevel(maxLevel + 1, out error), Is.False);
            Assert.That(build.Level, Is.EqualTo(1));
            Assert.That(build.GetEquipped(BuildSlot.Weapon), Is.SameAs(equalSword));
            Assert.That(build.GetEquipped(BuildSlot.Gear), Is.Null);
            Assert.That(stats.GetValue(StatId.DEF), Is.EqualTo(140));
            Assert.That((resources.Snapshot.CurrentHP, resources.Snapshot.MaxHP), Is.EqualTo((165d, 175d)));
            Assert.That(health.Count, Is.EqualTo(1));
            Assert.That(panelChanges, Is.Zero);
            Assert.That(build.TryEquip(BuildSlot.Gear, charm, out error), Is.True, error);
            Assert.That(build.GetEquipped(BuildSlot.Gear), Is.SameAs(charm));
        }

        [Test]
        public void InitialSelectionValidatesBeforeCommitAndDeduplicatesOnlyIdenticalTalents()
        {
            var configured = Copy(profile);
            var secondTalent = Copy(talent);
            var data = new SerializedObject(configured);
            data.FindProperty("initialLevel").intValue = 3;
            data.FindProperty("initialWeapon").objectReferenceValue = sword;
            data.FindProperty("initialGear").objectReferenceValue = charm;
            var talents = data.FindProperty("initialTalents");
            talents.arraySize = 3;
            talents.GetArrayElementAtIndex(0).objectReferenceValue = talent;
            talents.GetArrayElementAtIndex(1).objectReferenceValue = talent;
            talents.GetArrayElementAtIndex(2).objectReferenceValue = sword;
            data.ApplyModifiedPropertiesWithoutUndo();
            var failure = Assert.Throws<InvalidOperationException>(() => CharacterBuildState.Create(stats, configured));
            Assert.That(failure.Message, Does.Contain(configured.name));
            Assert.That(stats.GetValue(StatId.MaxHP), Is.EqualTo(150));

            talents.GetArrayElementAtIndex(2).objectReferenceValue = secondTalent;
            data.ApplyModifiedPropertiesWithoutUndo();
            var build = CharacterBuildState.Create(stats, configured);
            Assert.That(build.Level, Is.EqualTo(3));
            Assert.That(stats.GetValue(StatId.MaxHP), Is.EqualTo(205));
            Assert.That(stats.GetValue(StatId.DEF), Is.EqualTo(108));
            Assert.That(stats.GetValue(StatId.CritDamage), Is.EqualTo(1.90).Within(1e-9));
            Assert.That(build.TrySetTalentEnabled(talent, false, out var error), Is.True, error);
            Assert.That(build.IsTalentEnabled(secondTalent), Is.True);
            Assert.That(stats.GetValue(StatId.CritDamage), Is.EqualTo(1.70).Within(1e-9));
        }
    }
}
