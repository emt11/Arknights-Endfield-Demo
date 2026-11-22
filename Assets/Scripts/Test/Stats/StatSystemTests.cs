using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static Endfield.Stats.ModifierOperation;

namespace Endfield.Stats.Tests
{
    public sealed class StatSystemTests
    {
        private static readonly SourceID Weapon = new SourceID("Equipment", 17);
        private static readonly SourceID Buff = new SourceID("Effect", 42);
        private static readonly SourceID OtherBuff = new SourceID("Effect", 43);

        private static Dictionary<StatId, double> InitialValues()
        {
            return new Dictionary<StatId, double>
            {
                { StatId.STR, 10 }, { StatId.AGI, 0 }, { StatId.INT, 100 }, { StatId.WIL, 50 },
                { StatId.BaseATK, 100 }, { StatId.BaseDEF, 100 }, { StatId.BaseHP, 100 }
            };
        }

        private static StatSet Character()
        {
            return StatSet.Create(InitialValues(), StatId.INT, StatId.WIL);
        }

        private static void Success(StatWriteResult result)
        {
            Assert.That(result.Success, Is.True, result.Error);
        }

        private static void Removed(StatWriteResult result, int count)
        {
            Success(result);
            Assert.That(result.RemovedEntryCount, Is.EqualTo(count));
        }

        private static void HasValue(IReadOnlyStatSet stats, StatId id, double expected)
        {
            Assert.That(stats.GetValue(id), Is.EqualTo(expected).Within(0.000001), id.ToString());
        }

        [Test]
        public void AttackKeepsFlatAttackBetweenPercentageAndAttributeBonus()
        {
            var stats = Character();
            Success(stats.ReplaceSource(Weapon, new StatModifier(StatId.WeaponATK, Flat, 200),
                new StatModifier(StatId.FixedATK, Flat, 50), new StatModifier(StatId.ATKPercentBonus, Flat, 0.3)));
            HasValue(stats, StatId.ATK, 704);

            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.ATKPercentBonus, Flat, 0.2)));
            HasValue(stats, StatId.ATK, 800);
            HasValue(stats, StatId.FixedATK, 50);
            Removed(stats.RemoveSource(Buff), 1);
            HasValue(stats, StatId.ATK, 704);
            HasValue(stats, StatId.FixedATK, 50);
        }

        [Test]
        public void CharactersCopyInitialValuesAndUseTheirOwnPrimaryAttributesAndCoefficients()
        {
            var initial = InitialValues();
            var catalog = ScriptableObject.CreateInstance<StatCatalog>();
            try
            {
                var first = catalog.CreateSet(initial, StatId.INT, StatId.WIL);
                var second = catalog.CreateSet(initial, StatId.STR, StatId.WIL);
                initial[StatId.INT] = 1;
                var secondPanel = Enum.GetValues(typeof(StatId)).Cast<StatId>().Select(second.GetValue).ToArray();

                using var settings = new SerializedObject(catalog);
                settings.FindProperty("mainBonus").doubleValue = 0.01;
                settings.FindProperty("healthPerStrength").doubleValue = 6;
                settings.ApplyModifiedPropertiesWithoutUndo();
                Success(first.SetBase(StatId.INT, 110));
                HasValue(first, StatId.AttributeATKBonus, 0.65);
                HasValue(first, StatId.ATK, 165);
                HasValue(first, StatId.MaxHP, 150);
                HasValue(second, StatId.AttributeATKBonus, 0.15);
                CollectionAssert.AreEqual(secondPanel, Enum.GetValues(typeof(StatId)).Cast<StatId>().Select(second.GetValue));

                var third = catalog.CreateSet(initial, StatId.INT, StatId.WIL);
                HasValue(third, StatId.AttributeATKBonus, 0.11);
                HasValue(third, StatId.MaxHP, 160);
                Assert.Throws<ArgumentException>(() => catalog.CreateSet(initial, StatId.INT, StatId.INT));
                Assert.Throws<ArgumentException>(() => catalog.CreateSet(initial, StatId.BaseHP, StatId.STR));
                initial.Remove(StatId.STR);
                Assert.Throws<ArgumentException>(() => catalog.CreateSet(initial, StatId.INT, StatId.WIL));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void RemovingOneBuffPreservesOtherInstancesAndCanBeRetried()
        {
            var stats = Character();
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.STR, Flat, 2)));
            Success(stats.ReplaceSource(OtherBuff, new StatModifier(StatId.STR, Flat, 3)));
            HasValue(stats, StatId.STR, 15);
            Removed(stats.RemoveSource(Buff), 1);
            HasValue(stats, StatId.STR, 13);
            HasValue(stats, StatId.MaxHP, 165);
            Removed(stats.RemoveSource(Buff), 0);
            Success(stats.ReplaceSource(OtherBuff));
            HasValue(stats, StatId.STR, 10);
        }

        [Test]
        public void RefreshReplacesOneWholeSourceAndRejectsConflictsAndOverflow()
        {
            var stats = Character();
            Success(stats.ReplaceSource(Weapon, new StatModifier(StatId.DEF, Override, 150, 5)));
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.STR, Flat, 2),
                new StatModifier(StatId.BaseHP, Flat, 25)));
            var events = new List<IReadOnlyList<StatChange>>();
            using var subscription = stats.Subscribe(events.Add);

            var replacement = new[] { new StatModifier(StatId.STR, Flat, 4) };
            Success(stats.ReplaceSource(Buff, replacement));
            Success(stats.ReplaceSource(Buff, replacement));
            replacement[0] = new StatModifier(StatId.STR, Flat, 100);
            Assert.That(events.Count, Is.EqualTo(1));
            HasValue(stats, StatId.STR, 14);
            HasValue(stats, StatId.MaxHP, 170);
            HasValue(stats, StatId.BaseHP, 100);

            var conflict = stats.ReplaceSource(Buff, new StatModifier(StatId.DEF, Override, 120, 5),
                new StatModifier(StatId.DEF, Override, 170, 8));
            Assert.That(conflict.Success, Is.False);
            StringAssert.Contains("DEF", conflict.Error);
            StringAssert.Contains("5", conflict.Error);

            Assert.That(stats.ReplaceSource(Buff, new StatModifier(StatId.BaseHP, Multiplicative, double.MaxValue)).Success, Is.False);
            Assert.That(stats.SetBase(StatId.ATK, 5).Success, Is.False);
            Assert.That(stats.SetBase(StatId.STR, double.NaN).Success, Is.False);
            Assert.That(stats.SetBase((StatId)999, 9).Success, Is.False);
            Assert.That(stats.ReplaceSource(Buff, new StatModifier(StatId.ATKPercentBonus, (ModifierOperation)3, 0.1)).Success, Is.False);
            Assert.That(stats.ReplaceSource(Buff, new StatModifier((StatId)99, Flat, 0.1)).Success, Is.False);
            Assert.That(stats.ReplaceSource(Buff, new StatModifier(StatId.DEF, Multiplicative, -1)).Success, Is.False);
            Assert.That(events.Count, Is.EqualTo(1));

            Success(stats.SetBase(StatId.STR, 11));
            HasValue(stats, StatId.STR, 15);
            HasValue(stats, StatId.DEF, 150);
            Removed(stats.RemoveSource(Buff), 1);
            HasValue(stats, StatId.STR, 11);
        }

        [Test]
        public void ModifierOrderOverridesAndClampsMatchPanelValues()
        {
            var stats = Character();
            Success(stats.ReplaceSource(Weapon, new StatModifier(StatId.DEF, Multiplicative, 0.75),
                new StatModifier(StatId.DEF, AdditivePercent, 0.20), new StatModifier(StatId.DEF, Flat, 20)));
            HasValue(stats, StatId.DEF, 108);
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.DEF, Override, 150, 1)));
            Success(stats.ReplaceSource(OtherBuff, new StatModifier(StatId.DEF, Override, 190, 2)));
            HasValue(stats, StatId.DEF, 190);
            stats.RemoveSource(OtherBuff);
            HasValue(stats, StatId.DEF, 150);
            stats.RemoveSource(Buff);
            HasValue(stats, StatId.DEF, 108);
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.DEF, Multiplicative, 0)));
            HasValue(stats, StatId.DEF, 0);
            stats.RemoveSource(Buff);
            HasValue(stats, StatId.DEF, 108);

            Success(stats.SetBase(StatId.CritRate, 0.20));
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.CritRate, Flat, 0.10)));
            HasValue(stats, StatId.CritRate, 0.30);
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.CritRate, Multiplicative, 1.10)));
            HasValue(stats, StatId.CritRate, 0.22);
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.CritRate, Override, 1.20)));
            HasValue(stats, StatId.CritRate, 1);
            int notifications = 0;
            using var subscription = stats.Subscribe(_ => notifications++);
            Success(stats.ReplaceSource(OtherBuff, new StatModifier(StatId.CritRate, Flat, 0.05)));
            Assert.That(notifications, Is.Zero);
            stats.RemoveSource(Buff);
            HasValue(stats, StatId.CritRate, 0.25);
            Assert.That(notifications, Is.EqualTo(1));
        }

        [Test]
        public void CritDamageUsesAnOptionalTotalMultiplierAndRestoresItOnRemoval()
        {
            var stats = Character();
            Success(stats.SetBase(StatId.CritRate, 0.2));
            double attack = stats.GetValue(StatId.ATK);
            HasValue(stats, StatId.CritDamage, 1.5);

            var notifications = new List<IReadOnlyList<StatChange>>();
            using var subscription = stats.Subscribe(notifications.Add);
            Success(stats.ReplaceSource(Weapon, new StatModifier(StatId.CritDamage, Flat, 0.2)));
            HasValue(stats, StatId.CritDamage, 1.7);
            Assert.That(notifications.Single().Single().StatId, Is.EqualTo(StatId.CritDamage));
            HasValue(stats, StatId.CritRate, 0.2);
            HasValue(stats, StatId.ATK, attack);
            Success(stats.ReplaceSource(Weapon, new StatModifier(StatId.CritDamage, AdditivePercent, 0.2)));
            HasValue(stats, StatId.CritDamage, 1.8);
            Removed(stats.RemoveSource(Weapon), 1);
            HasValue(stats, StatId.CritDamage, 1.5);

            var initial = InitialValues();
            initial[StatId.CritDamage] = 0;
            var configured = StatSet.Create(initial, StatId.INT, StatId.WIL);
            HasValue(configured, StatId.CritDamage, 0);
            Success(configured.SetBase(StatId.CritDamage, 1.75));
            HasValue(configured, StatId.CritDamage, 1.75);
            Success(configured.ReplaceSource(Buff, new StatModifier(StatId.CritDamage, Flat, -2)));
            HasValue(configured, StatId.CritDamage, 0);
        }

        [Test]
        public void GroupsCoverEveryDefinitionInIdOrder()
        {
            var ids = new List<StatId>();
            foreach (var group in StatCatalog.Groups)
            {
                var definitions = StatCatalog.GetByGroup(group).ToArray();
                CollectionAssert.IsOrdered(definitions.Select(item => (int)item.Id).ToArray());
                foreach (var definition in definitions)
                {
                    Assert.That(definition.Group, Is.EqualTo(group));
                    Assert.That(definition.DisplayName, Is.Not.Empty);
                    Assert.That(StatCatalog.Get(definition.Id), Is.SameAs(definition));
                    ids.Add(definition.Id);
                }
            }
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(StatId)), ids);
            Assert.That(StatCatalog.GetByGroup(StatGroup.DamageBonus), Is.Empty);
            var survivalStats = new[] { StatId.PhysicalResistance, StatId.ArtsResistance, StatId.HealingReceivedBonus };
            var stats = Character();
            foreach (var id in survivalStats)
            {
                var definition = StatCatalog.Get(id);
                Assert.That(definition.Derived, Is.True);
                Assert.That(definition.Required, Is.False);
                Assert.That(stats.SetBase(id, 10).Success, Is.False);
            }
            Assert.That(StatCatalog.Get(StatId.CritRate).Derived, Is.False);
            Assert.That(StatCatalog.Get(StatId.ATK).Derived, Is.True);
        }

        [Test]
        public void InnateBenefitsApplyRegardlessOfMainAndSecondaryAttributes()
        {
            var initial = InitialValues();
            initial[StatId.AGI] = 100;
            initial[StatId.INT] = 200;
            initial[StatId.WIL] = 100;
            var first = StatSet.Create(initial, StatId.AGI, StatId.STR);
            var second = StatSet.Create(initial, StatId.INT, StatId.WIL);
            foreach (var stats in new[] { first, second })
            {
                HasValue(stats, StatId.MaxHP, 150);
                HasValue(stats, StatId.PhysicalResistance, 9.090909091);
                HasValue(stats, StatId.ArtsResistance, 16.666666667);
                HasValue(stats, StatId.HealingReceivedBonus, 0.10);
            }
            HasValue(first, StatId.AttributeATKBonus, 0.52);
            HasValue(second, StatId.AttributeATKBonus, 1.20);

            Success(first.ReplaceSource(Buff, new StatModifier(StatId.AGI, Flat, 10)));
            HasValue(first, StatId.PhysicalResistance, 9.909909910);
            HasValue(first, StatId.AttributeATKBonus, 0.57);
            Success(first.ReplaceSource(Buff, new StatModifier(StatId.STR, Flat, 10)));
            HasValue(first, StatId.MaxHP, 200);
            HasValue(first, StatId.AttributeATKBonus, 0.54);
            Success(first.ReplaceSource(Buff, new StatModifier(StatId.WIL, Flat, 10)));
            HasValue(first, StatId.HealingReceivedBonus, 0.11);
            HasValue(first, StatId.AttributeATKBonus, 0.52);
            Success(first.ReplaceSource(Buff, new StatModifier(StatId.HealingReceivedBonus, Flat, 0.02)));
            HasValue(first, StatId.HealingReceivedBonus, 0.12);
            HasValue(second, StatId.HealingReceivedBonus, 0.10);
            Removed(first.RemoveSource(Buff), 1);
            HasValue(first, StatId.HealingReceivedBonus, 0.10);
        }

        [Test]
        public void AbilityFormulasReadWholePointsAfterModifiersAndKeepFractionalPanelValues()
        {
            var initial = InitialValues();
            initial[StatId.STR] = initial[StatId.INT] = initial[StatId.WIL] = 0;
            var stats = StatSet.Create(initial, StatId.AGI, StatId.STR);
            HasValue(stats, StatId.MaxHP, 100);
            HasValue(stats, StatId.AttributeATKBonus, 0);
            HasValue(stats, StatId.PhysicalResistance, 0);
            HasValue(stats, StatId.ArtsResistance, 0);
            HasValue(stats, StatId.HealingReceivedBonus, 0);

            Success(stats.SetBase(StatId.AGI, 100));
            Success(stats.ReplaceSource(Weapon, new StatModifier(StatId.STR, Flat, 10.9),
                new StatModifier(StatId.INT, Flat, 200.9), new StatModifier(StatId.WIL, Flat, 100.9)));
            HasValue(stats, StatId.STR, 10.9);
            HasValue(stats, StatId.MaxHP, 150);
            HasValue(stats, StatId.ArtsResistance, 16.666666667);
            HasValue(stats, StatId.HealingReceivedBonus, 0.10);
            var events = new List<IReadOnlyList<StatChange>>();
            using var subscription = stats.Subscribe(events.Add);
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.AGI, Flat, 0.9)));
            Assert.That(events.Single().Single().StatId, Is.EqualTo(StatId.AGI));
            HasValue(stats, StatId.AGI, 100.9);
            HasValue(stats, StatId.PhysicalResistance, 9.090909091);
            HasValue(stats, StatId.AttributeATKBonus, 0.52);

            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.AGI, Flat, 1)));
            HasValue(stats, StatId.PhysicalResistance, 9.173478656);
            HasValue(stats, StatId.AttributeATKBonus, 0.525);
        }

        [Test]
        public void OneEquipmentSourceUpdatesAbilityAndResistanceInOneNotification()
        {
            var initial = InitialValues();
            initial[StatId.AGI] = 100;
            var stats = StatSet.Create(initial, StatId.AGI, StatId.STR);
            var panels = new List<(double resistance, double attackBonus)>();
            using var subscription = stats.Subscribe(_ => panels.Add((
                stats.GetValue(StatId.PhysicalResistance), stats.GetValue(StatId.AttributeATKBonus))));
            Success(stats.ReplaceSource(Weapon, new StatModifier(StatId.PhysicalResistance, Flat, 5),
                new StatModifier(StatId.AGI, Flat, 100)));
            Removed(stats.RemoveSource(Weapon), 2);
            Assert.That(panels.Count, Is.EqualTo(2));
            Assert.That(panels[0].resistance, Is.EqualTo(21.666666667).Within(0.000001));
            Assert.That(panels[0].attackBonus, Is.EqualTo(1.02).Within(0.000001));
            Assert.That(panels[1].resistance, Is.EqualTo(9.090909091).Within(0.000001));
            Assert.That(panels[1].attackBonus, Is.EqualTo(0.52).Within(0.000001));

            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.PhysicalResistance, Flat, -20)));
            HasValue(stats, StatId.PhysicalResistance, -10.909090909);
        }

        [Test]
        public void PercentagesAndDerivedModifiersApplyAtTheirOwnStage()
        {
            var stats = Character();
            Success(stats.SetBase(StatId.ATKPercentBonus, 0.3));
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.ATKPercentBonus, Flat, 0.2)));
            HasValue(stats, StatId.ATKPercentBonus, 0.5);
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.ATKPercentBonus, AdditivePercent, 0.2)));
            HasValue(stats, StatId.ATKPercentBonus, 0.36);
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.DEF, AdditivePercent, 0.2),
                new StatModifier(StatId.DEF, AdditivePercent, 0.3)));
            HasValue(stats, StatId.DEF, 150);
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.DEF, Multiplicative, 1.2),
                new StatModifier(StatId.DEF, Multiplicative, 1.3)));
            HasValue(stats, StatId.DEF, 156);
            Success(stats.SetBase(StatId.WeaponATK, 200));
            Success(stats.SetBase(StatId.FixedATK, 50));
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.AttributeATKBonus, Flat, 0.10)));
            HasValue(stats, StatId.AttributeATKBonus, 0.70);
            HasValue(stats, StatId.ATK, 748);
            Success(stats.ReplaceSource(OtherBuff, new StatModifier(StatId.ATK, Flat, 10)));
            HasValue(stats, StatId.ATK, 758);
            Removed(stats.RemoveSource(OtherBuff), 1);
            HasValue(stats, StatId.ATK, 748);
            Removed(stats.RemoveSource(Buff), 1);
            HasValue(stats, StatId.ATK, 704);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ZeroMultiplierAllowsExpiryAndCannotMaskOverflow(bool useEmptyReplacement)
        {
            var stats = Character();
            StatWriteResult Expire() => useEmptyReplacement ? stats.ReplaceSource(Buff) : stats.RemoveSource(Buff);
            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.DEF, Multiplicative, 0)));
            HasValue(stats, StatId.DEF, 0);
            int notifications = 0;
            using var subscription = stats.Subscribe(_ => notifications++);
            var excessive = new StatModifier(StatId.DEF, Multiplicative, double.MaxValue);
            void Reject(params StatModifier[] entries)
            {
                var failure = stats.ReplaceSource(OtherBuff, entries);
                Assert.That(failure.Success, Is.False);
                Assert.That(failure.RemovedEntryCount, Is.Zero);
                Assert.That(failure.Error, Is.Not.Empty);
            }

            Reject(excessive);
            Removed(stats.RemoveSource(OtherBuff), 0);
            var large = new StatModifier(StatId.DEF, Multiplicative, 1e200);
            Reject(large, large);
            Assert.That(notifications, Is.Zero);
            Removed(Expire(), 1);
            HasValue(stats, StatId.DEF, 100);

            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.DEF, Multiplicative, 0)));
            Success(stats.ReplaceSource(OtherBuff, new StatModifier(StatId.DEF, Multiplicative, 2)));
            Reject(excessive);
            Assert.That(stats.SetBase(StatId.BaseDEF, double.MaxValue).Success, Is.False);
            HasValue(stats, StatId.DEF, 0);
            Assert.That(notifications, Is.EqualTo(2));
            Removed(Expire(), 1);
            HasValue(stats, StatId.DEF, 200);
            Removed(Expire(), 0);
            Assert.That(notifications, Is.EqualTo(3));

            Success(stats.ReplaceSource(Buff, new StatModifier(StatId.STR, Multiplicative, 0)));
            var upstream = stats.ReplaceSource(OtherBuff, new StatModifier(StatId.STR, Multiplicative, double.MaxValue / 20));
            Assert.That(upstream.Success, Is.False);
            StringAssert.Contains("MaxHP", upstream.Error);
            HasValue(stats, StatId.MaxHP, 100);
            Removed(Expire(), 1);
            HasValue(stats, StatId.STR, 10);
            HasValue(stats, StatId.MaxHP, 150);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WoodenSwordUpdatesOneCharacterAndPublishesOncePerWrite(bool useEmptyReplacement)
        {
            var first = Character();
            var second = Character();
            Success(first.SetBase(StatId.WeaponATK, 200));
            Success(first.SetBase(StatId.FixedATK, 50));
            Success(first.ReplaceSource(Buff, new StatModifier(StatId.ATKPercentBonus, Flat, 0.30)));
            var panels = new List<(double attack, double hp)>();
            using var subscription = first.Subscribe(_ => panels.Add((first.GetValue(StatId.ATK), first.GetValue(StatId.MaxHP))));
            var added = first.ReplaceSource(Weapon, new StatModifier(StatId.STR, Flat, 3),
                new StatModifier(StatId.WeaponATK, Flat, 3));
            Removed(added, 0);
            Removed(useEmptyReplacement ? first.ReplaceSource(Weapon) : first.RemoveSource(Weapon), 2);
            Assert.That(panels.Count, Is.EqualTo(2));
            Assert.That(panels[0].attack, Is.EqualTo(710.24).Within(0.000001));
            Assert.That(panels[0].hp, Is.EqualTo(165));
            Assert.That(panels[1].attack, Is.EqualTo(704).Within(0.000001));
            Assert.That(panels[1].hp, Is.EqualTo(150));
            HasValue(second, StatId.ATK, 160);
            HasValue(second, StatId.MaxHP, 150);
        }

        [Test]
        public void HealthChangePublishesTheNewValueAndRepeatedReadsDoNotNotify()
        {
            var stats = Character();
            IReadOnlyStatSet reader = stats;
            var events = new List<IReadOnlyList<StatChange>>();
            double healthInCallback = 0;
            using (reader.Subscribe(changes =>
            {
                events.Add(changes);
                healthInCallback = reader.GetValue(StatId.MaxHP);
            }))
            {
                Success(stats.SetBase(StatId.STR, 12));
                Assert.That(healthInCallback, Is.EqualTo(160));
                var change = events.Single().Single(entry => entry.StatId == StatId.MaxHP);
                Assert.That(change.OldValue, Is.EqualTo(150));
                Assert.That(change.NewValue, Is.EqualTo(160));
                for (int read = 0; read < 100; read++) HasValue(reader, StatId.MaxHP, 160);
                Assert.That(events.Count, Is.EqualTo(1));
            }

            Success(stats.SetBase(StatId.STR, 10));
            Assert.That(events.Count, Is.EqualTo(1));
        }
    }
}
