using System;
using System.Collections.Generic;
using Endfield.Stats;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Endfield.Resources.Tests
{
    public sealed class ResourceTests
    {
        private static StatSet Character()
        {
            return AssetDatabase.LoadAssetAtPath<CharacterStatProfile>("Assets/ScriptObjects/Stats/Phoebe.asset").CreateSet();
        }

        [Test]
        public void HealthTracksMaxHPWithoutHealingOnEquipmentChanges()
        {
            var stats = Character();
            using var resources = new CharacterResourceContainer(stats, 100, 90, 100);
            var observed = new List<(double, double, double, double)>();
            using var subscription = resources.Subscribe((previous, current) =>
                observed.Add((previous.CurrentHP, previous.MaxHP, resources.Snapshot.CurrentHP, current.MaxHP)));

            var sword = new SourceID("Equipment", 17);
            resources.ApplyDamage(40);
            Assert.That(stats.ReplaceSource(sword, new StatModifier(StatId.STR, ModifierOperation.Flat, 3),
                new StatModifier(StatId.WeaponATK, ModifierOperation.Flat, 3)).Success, Is.True);
            Assert.That(resources.RestoreHP(42), Is.EqualTo(42));
            Assert.That(stats.RemoveSource(sword).Success, Is.True);
            Assert.That(resources.RestoreHP(42), Is.Zero);
            Assert.That(stats.SetBase(StatId.BaseATK, 200).Success, Is.True);

            var zero = new SourceID("Effect", 1);
            Assert.That(stats.ReplaceSource(zero, new StatModifier(StatId.MaxHP, ModifierOperation.Override, 0)).Success, Is.True);
            Assert.That(stats.RemoveSource(zero).Success, Is.True);
            Assert.That(observed, Is.EqualTo(new (double, double, double, double)[]
            {
                (150, 150, 110, 150), (110, 150, 110, 165), (110, 165, 152, 165),
                (152, 165, 150, 150), (150, 150, 0, 0), (0, 0, 0, 150)
            }));
        }

        [Test]
        public void DamageReportsHealthLostAndOverkill()
        {
            using var resources = new CharacterResourceContainer(Character(), 0, 0, 0);
            resources.ApplyDamage(60);
            var hpChanges = new List<(double, double)>();
            using var subscription = resources.Subscribe((before, after) => hpChanges.Add((before.CurrentHP, after.CurrentHP)));

            var hit = resources.ApplyDamage(32);
            Assert.That((hit.HPLost, hit.Overkill), Is.EqualTo((32d, 0d)));
            hit = resources.ApplyDamage(100);
            Assert.That((hit.HPLost, hit.Overkill), Is.EqualTo((58d, 42d)));
            hit = resources.ApplyDamage(0);
            Assert.That((hit.HPLost, hit.Overkill), Is.EqualTo((0d, 0d)));
            Assert.That(hpChanges, Is.EqualTo(new[] { (90d, 58d), (58d, 0d) }));
        }

        [Test]
        public void TeamConsumptionIsAtomicAndUltimateEnergyIsPerCharacter()
        {
            var team = new SkillPointPool(100, 60);
            IReadOnlySkillPoints view = team;
            var balances = new List<(double, double)>();
            using var subscription = view.Subscribe((before, after) => balances.Add((before.CurrentSP, after.CurrentSP)));
            Assert.That(team.TryConsume(40), Is.True);
            Assert.That(team.TryConsume(40), Is.False);
            Assert.That(team.TryConsume(0), Is.True);
            Assert.That(view.Snapshot.CurrentSP, Is.EqualTo(20));
            Assert.That(team.Restore(90), Is.EqualTo(80));
            Assert.That(team.Restore(20), Is.Zero);
            Assert.That(balances, Is.EqualTo(new[] { (60d, 20d), (20d, 100d) }));
            Assert.That(view.Snapshot.MaxSP, Is.EqualTo(100));

            var largePool = new SkillPointPool(double.MaxValue, double.MaxValue / 2);
            Assert.That(largePool.Restore(double.MaxValue), Is.EqualTo(double.MaxValue / 2));
            Assert.That(largePool.Snapshot.CurrentSP, Is.EqualTo(double.MaxValue));

            using var phoebe = new CharacterResourceContainer(Character(), 100, 90, 0);
            using var si = new CharacterResourceContainer(Character(), 80, 30, 0);
            var energy = new List<double>();
            using var energySubscription = phoebe.Subscribe((_, current) => energy.Add(current.UltimateEnergy));
            Assert.That(phoebe.RestoreUltimate(25), Is.EqualTo(10));
            Assert.That(phoebe.TryConsumeUltimate(100), Is.True);
            Assert.That(phoebe.TryConsumeUltimate(1), Is.False);
            Assert.That(phoebe.TryConsumeUltimate(0), Is.True);
            Assert.That(energy, Is.EqualTo(new[] { 100d, 0d }));
            Assert.That(si.Snapshot.UltimateEnergy, Is.EqualTo(30));
        }

        [Test]
        public void ThrowingListenerDoesNotInterruptSpendingOrOtherSubscriptions()
        {
            var team = new SkillPointPool(20, 20);
            var calls = new List<string>();
            Action<SkillPointSnapshot, SkillPointSnapshot> record = (_, current) => calls.Add($"余额 {current.CurrentSP}");
            Action<SkillPointSnapshot, SkillPointSnapshot> group = (_, _) => throw new InvalidOperationException("测试监听器异常");
            group += (before, after) => calls.Add($"扣费 {before.CurrentSP - after.CurrentSP}");

            using var first = team.Subscribe(record);
            using var middle = team.Subscribe(group);
            using var last = team.Subscribe(record);
            first.Dispose();
            first.Dispose();

            LogAssert.Expect(LogType.Exception, "InvalidOperationException: 测试监听器异常");
            Assert.That(team.TryConsume(3), Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "扣费 3", "余额 17" }));

            middle.Dispose();
            Assert.That(team.Restore(1), Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(new[] { "扣费 3", "余额 17", "余额 18" }));
        }

        [Test]
        public void StaggerOnlyTriggersWhenFillingAndInvalidPointsPreserveState()
        {
            var stats = Character();
            using var resources = new CharacterResourceContainer(stats, 100, 0, 100);
            var changes = new List<double>();
            using var subscription = resources.Subscribe((_, next) => changes.Add(next.Stagger));
            Assert.That(resources.AddStagger(80), Is.False);
            Assert.That(resources.AddStagger(35), Is.True);
            Assert.That(resources.AddStagger(10), Is.False);
            resources.ResetStagger();
            Assert.That(resources.AddStagger(100), Is.True);

            foreach (double amount in new[] { -1d, double.NaN, double.PositiveInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => resources.ApplyDamage(amount));
            Assert.That(resources.Snapshot.CurrentHP, Is.EqualTo(150));
            Assert.That(changes, Is.EqualTo(new[] { 80d, 100d, 0d, 100d }));

            using var disabled = new CharacterResourceContainer(stats, 0, 0, 0);
            Assert.That(disabled.AddStagger(100), Is.False);
            Assert.That(disabled.Snapshot.Stagger, Is.Zero);
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterResourceContainer(stats, 80, 81, 0));
            Assert.That(error.ParamName, Is.EqualTo("initialUltimateEnergy"));
            Assert.That(error.ActualValue, Is.EqualTo(81d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SkillPointPool(double.NaN, 0));
        }
    }
}
