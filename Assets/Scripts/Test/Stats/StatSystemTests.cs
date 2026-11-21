using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Endfield.Stats.Tests
{
    public sealed class StatSystemTests
    {
        private static readonly SourceID Weapon = new SourceID("Equipment", 17);
        private static readonly SourceID OtherWeapon = new SourceID("Equipment", 18);
        private static readonly SourceID Effect = new SourceID("Effect", 42);
        private const ModifierOperation Flat = ModifierOperation.Flat;
        private const ModifierOperation Override = ModifierOperation.Override;
        private const double Tolerance = 1e-6;

        private static Dictionary<string, double> CharacterValues() => new Dictionary<string, double>
        {
            ["STR"] = 10, ["AGI"] = 10, ["INT"] = 100, ["WIL"] = 50,
            ["BaseHP"] = 100, ["BaseDEF"] = 35, ["BaseATK"] = 100, ["FixedATK"] = 50
        };

        private static StatSet Character(string main = "INT") => StatSet.Create(
            StatCatalog.CreateCombatDefinitions(), CharacterValues(), new AttributeBinding(main, "WIL"));

        private static StatModifier Mod(SourceID source, ulong entry, string stat, double value,
            ModifierOperation operation = Flat, int priority = 0, UnitType? unit = null) =>
            new StatModifier(new ModifierKey(source, entry), stat, operation, value, priority, unit);

        private static StatCommandResult Ok(StatCommitResult commit)
        {
            Assert.That(commit.Success, Is.True, commit.Error);
            return commit.Results[commit.Results.Count - 1];
        }

        private static void Rejected(StatCommitResult commit, params string[] details)
        {
            Assert.That(commit.Success, Is.False);
            Assert.That(commit.Results, Is.Empty);
            foreach (string detail in details) StringAssert.Contains(detail, commit.Error);
        }

        private static void Equip(StatSet set)
        {
            Ok(set.ReplaceSource(Weapon, Mod(Weapon, 1, "WeaponATK", 200), Mod(Weapon, 2, "ATKPercentBonus", 0.30)));
        }

        private static StatDefinition Derived(string id, params string[] dependencies)
        {
            var slots = dependencies.Select(d => new FormulaInput(d, d, UnitType.Point)).ToArray();
            return new StatDefinition(id, UnitType.Point, formula: new FormulaDefinition(id, id,
                FormulaKind.WeightedSum, slots, dependencies.Select(_ => 1d).ToArray()));
        }

        [Test]
        public void T01_SharedCatalogCreatesIsolatedCharacterInstances()
        {
            var catalog = ScriptableObject.CreateInstance<StatCatalog>();
            var restoredCatalog = ScriptableObject.CreateInstance<StatCatalog>();
            try
            {
                using var a = catalog.CreateSet(CharacterValues(), new AttributeBinding("INT", "WIL"));
                using var b = catalog.CreateSet(CharacterValues(), new AttributeBinding("INT", "WIL"));
                Ok(a.AddModifier(Mod(Effect, 0, "STR", 2)));
                Assert.That(a.GetValue("MaxHP"), Is.EqualTo(160));
                Assert.That(b.GetValue("MaxHP"), Is.EqualTo(150));
                Assert.That(b.Revision, Is.Zero);
                Assert.That(b.GetModifiers("STR"), Is.Empty);

                // Exercise the same serialized formula fields an author edits in the Inspector.
                using var serialized = new SerializedObject(catalog);
                var definitions = serialized.FindProperty("definitions");
                var maxHp = Enumerable.Range(0, definitions.arraySize).Select(definitions.GetArrayElementAtIndex)
                    .Single(property => property.FindPropertyRelative("id").stringValue == "MaxHP");
                var weights = maxHp.FindPropertyRelative("formula").FindPropertyRelative("coefficients");
                weights.GetArrayElementAtIndex(1).doubleValue = 6;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(catalog), restoredCatalog);
                using var restored = restoredCatalog.CreateSet(CharacterValues(), new AttributeBinding("INT", "WIL"));
                Assert.That(restored.GetValue("MaxHP"), Is.EqualTo(160));
                Ok(b.SetBase("STR", 11));
                Assert.That(b.GetValue("MaxHP"), Is.EqualTo(155));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(restoredCatalog);
            }
        }

        [Test]
        public void T02_ModifierStagesAreStableAndRespectUnits()
        {
            var modifiers = new[]
            {
                Mod(Effect, 1, "Test", 20), Mod(Effect, 2, "Test", 0.25, ModifierOperation.AdditivePercent),
                Mod(Effect, 3, "Test", -0.05, ModifierOperation.AdditivePercent),
                Mod(Effect, 4, "Test", 1.5, ModifierOperation.Multiplicative),
                Mod(Effect, 5, "Test", 0.5, ModifierOperation.Multiplicative)
            };
            foreach (var entries in new[] { modifiers, modifiers.Reverse().ToArray() })
            {
                using var set = StatSet.Create(new[]
                {
                    new StatDefinition("Test", UnitType.Point, defaultValue: 100, maximum: 120),
                    new StatDefinition("CritRate", UnitType.Ratio, defaultValue: 0.2)
                });
                Ok(set.ReplaceSource(Effect, entries));
                Assert.That(set.GetValue("Test"), Is.EqualTo(108).Within(Tolerance));
                var key = Ok(set.AddModifier(Mod(Weapon, 1, "Test", 150, Override))).Key.Value;
                Assert.That(set.GetValue("Test"), Is.EqualTo(120));
                Ok(set.RemoveModifier(key));
                Assert.That(set.GetValue("Test"), Is.EqualTo(108).Within(Tolerance));
                Ok(set.ReplaceSource(Weapon, Mod(Weapon, 2, "CritRate", 0.1)));
                Assert.That(set.GetValue("CritRate"), Is.EqualTo(0.30).Within(Tolerance));
                Ok(set.ReplaceSource(Weapon, Mod(Weapon, 2, "CritRate", 1.1, ModifierOperation.Multiplicative)));
                Assert.That(set.GetValue("CritRate"), Is.EqualTo(0.22).Within(Tolerance));
            }
        }

        [Test]
        public void T03_FinalOverrideSetControlsConflictAndSwap()
        {
            using var set = Character();
            var a = Mod(Weapon, 1, "ATK", 80, Override, 10);
            var b = Mod(OtherWeapon, 2, "ATK", 90, Override, 10);
            Ok(set.AddModifier(a));
            var events = new List<StatChangeSet>();
            using var sub = set.Subscribe(events.Add);
            Rejected(set.AddModifier(b), "ATK", "10", a.Key.ToString(), b.Key.ToString());
            Assert.That(set.GetValue("ATK"), Is.EqualTo(80));
            Assert.That(set.GetModifiers("ATK").Single().Key, Is.EqualTo(a.Key));
            Assert.That(set.Revision, Is.EqualTo(1));
            Assert.That(events, Is.Empty);
            Ok(set.ApplyBatch(StatCommand.AddModifier(b), StatCommand.RemoveSource(Weapon)));
            var change = events.Single().Changes.Single();
            Assert.That(change.OldValue, Is.EqualTo(80));
            Assert.That(change.NewValue, Is.EqualTo(90));
            Ok(set.AddModifier(Mod(Effect, 1, "ATK", 70, Override, 20)));
            Assert.That(set.GetValue("ATK"), Is.EqualTo(70));
            Rejected(set.AddModifier(a), a.Key.ToString(), b.Key.ToString());
            Ok(set.RemoveSource(Effect));
            Assert.That(set.GetValue("ATK"), Is.EqualTo(90));
        }

        [Test]
        public void T04_AttackGoldenValuesFollowSourceLifetime()
        {
            using var set = Character();
            Equip(set);
            Assert.That(set.GetValue("ATK"), Is.EqualTo(704).Within(Tolerance));
            Ok(set.AddModifier(Mod(Effect, 1, "ATKPercentBonus", 0.2)));
            Assert.That(set.GetValue("ATK"), Is.EqualTo(800).Within(Tolerance));
            Assert.That(Ok(set.RemoveSource(Effect)).Count, Is.EqualTo(1));
            Assert.That(set.GetValue("ATK"), Is.EqualTo(704).Within(Tolerance));
            Assert.That(set.GetValue("FixedATK"), Is.EqualTo(50));
        }

        [Test]
        public void T05_RemoveSourceAffectsOnlyThatInstanceAndIsIdempotent()
        {
            using var set = Character();
            Ok(set.ReplaceSource(Weapon, Mod(Weapon, 1, "STR", 2), Mod(Weapon, 2, "BaseHP", 10)));
            Ok(set.AddModifier(Mod(OtherWeapon, 1, "STR", 3)));
            Assert.That(Ok(set.RemoveSource(Weapon)).Count, Is.EqualTo(2));
            Assert.That(set.GetValue("STR"), Is.EqualTo(13));
            Assert.That(set.GetValue("MaxHP"), Is.EqualTo(165));
            long revision = set.Revision;
            Assert.That(Ok(set.RemoveSource(Weapon)).Count, Is.Zero);
            Assert.That(Ok(set.RemoveModifier(new ModifierKey(Weapon, 1))).Count, Is.Zero);
            Assert.That(set.Revision, Is.EqualTo(revision));
        }

        [Test]
        public void T06_ReplaceSourceIsAtomicAndReplayIsANoOp()
        {
            using var set = Character();
            Equip(set);
            var flat = Mod(Weapon, 2, "STR", 2);
            Ok(set.ReplaceSource(Weapon, flat));
            long revision = set.Revision;
            Assert.That(set.GetModifiers("WeaponATK"), Is.Empty);
            Assert.That(set.GetValue("ATKPercentBonus"), Is.Zero);
            Assert.That(set.GetValue("STR"), Is.EqualTo(12));
            Ok(set.ReplaceSource(Weapon, flat));
            Rejected(set.ReplaceSource(Weapon, flat, Mod(Weapon, 2, "BaseHP", 10)), "Duplicate");
            Assert.That(set.Revision, Is.EqualTo(revision));
            Assert.That(set.GetModifiers("STR").Single().Key, Is.EqualTo(flat.Key));
            Assert.That(Ok(set.RemoveSource(Weapon)).Count, Is.EqualTo(1));
        }

        [Test]
        public void T07_DiamondIsEvaluatedOnceAndCacheReadsAreFree()
        {
            using var set = StatSet.Create(new[] { new StatDefinition("A", UnitType.Point), Derived("B", "A"),
                Derived("C", "A"), Derived("D", "B", "C"), new StatDefinition("E", UnitType.Point) });
            var events = new List<StatChangeSet>();
            using var sub = set.Subscribe(events.Add);
            Ok(set.ApplyBatch(StatCommand.SetBase("A", 1), StatCommand.SetBase("A", 3)));
            for (int i = 0; i < 100; i++) Assert.That(set.GetValue("D"), Is.EqualTo(6));
            CollectionAssert.AreEqual(new[] { "A", "B", "C", "D" }, events.Single().Changes.Select(c => c.StatId));
            foreach (string id in new[] { "A", "B", "C", "D" }) Assert.That(set.EvaluationCount(id), Is.EqualTo(2), id);
            Assert.That(set.EvaluationCount("E"), Is.EqualTo(1));
        }

        [Test]
        public void T08_FormulaBindingAndInvalidGraphs()
        {
            using var intellect = Character();
            using var strength = Character("STR");
            var oldAttack = strength.GetValue("ATK");
            int before = strength.EvaluationCount("ATK");
            Ok(intellect.SetBase("INT", 120));
            Ok(strength.SetBase("INT", 120));
            Assert.That(intellect.GetValue("AttributeATKBonus"), Is.EqualTo(0.7).Within(Tolerance));
            Assert.That(intellect.GetValue("ATK"), Is.EqualTo(255).Within(Tolerance));
            Assert.That(strength.GetValue("ATK"), Is.EqualTo(oldAttack));
            Assert.That(strength.EvaluationCount("ATK"), Is.EqualTo(before));

            Assert.Throws<ArgumentException>(() => Character("WIL"));
            var missing = Assert.Throws<ArgumentException>(() => StatSet.Create(new[] { Derived("A", "Missing") }));
            StringAssert.Contains("Missing", missing.Message);
            StringAssert.Contains("A -> A", Assert.Throws<ArgumentException>(() => StatSet.Create(new[] { Derived("A", "A") })).Message);
            StringAssert.Contains("A -> B -> C -> A", Assert.Throws<ArgumentException>(() =>
                StatSet.Create(new[] { Derived("A", "B"), Derived("B", "C"), Derived("C", "A") })).Message);
            var input = new[] { new FormulaInput("Value", "A", UnitType.Point) };
            var invalid = new[]
            {
                new FormulaDefinition("F", "B", FormulaKind.WeightedSum, input),
                new FormulaDefinition("F", "B", (FormulaKind)99, input, 1),
                new FormulaDefinition("F", "B", FormulaKind.WeightedSum, new[] { new FormulaInput("", "A", UnitType.Point) }, 1)
            };
            foreach (var formula in invalid)
                StringAssert.Contains("F", Assert.Throws<ArgumentException>(() => StatSet.Create(new[] {
                    new StatDefinition("A", UnitType.Point), new StatDefinition("B", UnitType.Point, formula: formula) })).Message);
        }

        [Test]
        public void T09_ListenerErrorsAreIsolatedAndReentrantWritesAreRejected()
        {
            using var set = Character();
            var seen = new List<StatChangeSet>();
            StatCommitResult nested = null;
            double observed = 0;
            long observedRevision = -1;
            var first = set.Subscribe(change =>
            {
                observed = set.GetValue("MaxHP");
                observedRevision = set.Snapshot("MaxHP").Revision;
                nested = set.SetBase("STR", 30);
                throw new InvalidOperationException("listener-test");
            });
            var second = set.Subscribe(seen.Add);
            LogAssert.Expect(LogType.Error, new Regex($"StatSet {set.Id} revision 1:.*listener-test"));
            Ok(set.ApplyBatch(StatCommand.SetBase("STR", 12), StatCommand.SetBase("BaseHP", 120)));
            var hp = seen.Single().Changes.Single(c => c.StatId == "MaxHP");
            Assert.That(hp.OldValue, Is.EqualTo(150));
            Assert.That(hp.NewValue, Is.EqualTo(180));
            Assert.That(observed, Is.EqualTo(180));
            Assert.That(observedRevision, Is.EqualTo(1));
            Rejected(nested, "notification");
            Assert.That(set.Revision, Is.EqualTo(1));
            first.Dispose();
            second.Dispose();
            Ok(set.SetBase("STR", 14));
            Assert.That(seen.Count, Is.EqualTo(1));
        }

        [Test]
        public void T10_ClampedUnchangedValueAdvancesRevisionWithoutNotification()
        {
            using var set = Character();
            Ok(set.SetBase("CritRate", 0.8));
            Ok(set.AddModifier(Mod(Weapon, 1, "CritRate", 0.25)));
            var seen = new List<StatChangeSet>();
            using var sub = set.Subscribe(seen.Add);
            Ok(set.AddModifier(Mod(Effect, 1, "CritRate", 0.1)));
            Assert.That(seen, Is.Empty);
            Assert.That(set.GetModifiers("CritRate").Count, Is.EqualTo(2));
            var snapshot = set.Snapshot("CritRate", "CritRate");
            Assert.That(snapshot.Revision, Is.EqualTo(3));
            Assert.That(snapshot.Values.Count, Is.EqualTo(1));
            Ok(set.ApplyBatch(StatCommand.RemoveSource(Effect), StatCommand.RemoveSource(Weapon)));
            Assert.That(seen.Single().Changes.Single().NewValue, Is.EqualTo(0.8).Within(Tolerance));
            Assert.That(snapshot.Values["CritRate"], Is.EqualTo(1));
        }

        [Test]
        public void T11_InvalidBatchRollsBackValuesEntriesAndRevision()
        {
            using var set = StatSet.Create(StatCatalog.CreateCombatDefinitions().Concat(new[] {
                new StatDefinition("Armor", UnitType.ResistancePoint) }), CharacterValues(), new AttributeBinding("INT", "WIL"));
            var original = Mod(Weapon, 1, "BaseHP", 1);
            Ok(set.AddModifier(original));
            var invalid = new[]
            {
                StatCommand.SetBase("Missing", 1), StatCommand.SetBase("ATK", 1),
                StatCommand.AddModifier(Mod(Effect, 1, "ATK", 10)), StatCommand.AddModifier(original),
                StatCommand.SetBase("BaseATK", double.NaN),
                StatCommand.AddModifier(Mod(Effect, 1, "BaseHP", double.MaxValue, ModifierOperation.Multiplicative)),
                StatCommand.AddModifier(Mod(Effect, 1, "Armor", 0.2, unit: UnitType.Ratio))
            };
            int calls = 0;
            using var sub = set.Subscribe(_ => calls++);
            foreach (var command in invalid)
            {
                var result = set.ApplyBatch(StatCommand.SetBase("BaseHP", 150), command);
                Rejected(result);
                Assert.That(set.GetValue("BaseHP"), Is.EqualTo(101));
                Assert.That(set.GetValue("MaxHP"), Is.EqualTo(151));
                Assert.That(set.Revision, Is.EqualTo(1));
                Assert.That(set.GetModifiers("BaseHP").Single().Key, Is.EqualTo(original.Key));
            }
            Rejected(set.ApplyBatch(invalid.Last()), "ResistancePoint", "Ratio");
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void T12_ZeroMultiplierNegativesAndIndependentRounding()
        {
            using var set = StatSet.Create(new[] { new StatDefinition("A", UnitType.Point, defaultValue: 10.75, minimum: 0),
                new StatDefinition("B", UnitType.Point, defaultValue: 10.75, roundDown: true) });
            Ok(set.AddModifier(Mod(Weapon, 1, "A", 0, ModifierOperation.Multiplicative)));
            Assert.That(set.GetValue("A"), Is.Zero);
            Ok(set.RemoveSource(Weapon));
            Assert.That(set.GetValue("A"), Is.EqualTo(10.75));
            Ok(set.AddModifier(Mod(Weapon, 1, "A", -1.5, ModifierOperation.AdditivePercent)));
            Assert.That(set.GetValue("A"), Is.Zero);
            Assert.That(set.GetValue("B"), Is.EqualTo(10));
            Rejected(set.AddModifier(Mod(Effect, 1, "A", -1, ModifierOperation.Multiplicative)));
        }

        [Test]
        public void T13_ReadOnlyResourceBoundaryAndSnapshotValidation()
        {
            using var set = Character();
            IReadOnlyStatSet reader = set;
            Ok(set.SetBase("BaseHP", 950));
            var snapshot = reader.Snapshot("ATK", "MaxHP");
            Assert.That(snapshot.Values["MaxHP"], Is.EqualTo(1000));
            Assert.That(snapshot.Revision, Is.EqualTo(reader.Revision));
            Assert.Throws<ArgumentException>(() => reader.Snapshot("ATK", "CurrentHP"));
            Assert.That(reader.TryGetValue("CurrentHP", out _), Is.False);
            Rejected(set.SetBase("CurrentHP", 800));
            var seen = new List<StatChangeSet>();
            using var sub = reader.Subscribe(seen.Add);
            Ok(set.SetBase("BaseHP", 1150));
            var hp = seen.Single().Changes.Single(c => c.StatId == "MaxHP");
            Assert.That(hp.OldValue, Is.EqualTo(1000));
            Assert.That(hp.NewValue, Is.EqualTo(1200));
            CollectionAssert.AreEquivalent(new[] { "BaseHP", "MaxHP" }, seen[0].Changes.Select(c => c.StatId));
        }

        [Test]
        public void T14_SnapshotsOutliveTheSetAndWorkersCannotAccessLiveState()
        {
            using var set = Character();
            Equip(set);
            var snapshot = set.Snapshot("ATK");
            Ok(set.AddModifier(Mod(Effect, 1, "ATKPercentBonus", 0.20)));
            Assert.That(set.GetValue("ATK"), Is.EqualTo(800).Within(Tolerance));
            Task.Run(() =>
            {
                Assert.Throws<InvalidOperationException>(() => Character());
                Assert.Throws<InvalidOperationException>(() => set.GetValue("ATK"));
                Assert.Throws<InvalidOperationException>(() => set.Snapshot("ATK"));
                Assert.Throws<InvalidOperationException>(() => set.SetBase("BaseATK", 5));
                Assert.That(snapshot.Values["ATK"], Is.EqualTo(704).Within(Tolerance));
            }).GetAwaiter().GetResult();
            Assert.That(set.Revision, Is.EqualTo(2));
            set.Dispose();
            Assert.Throws<ObjectDisposedException>(() => set.GetValue("ATK"));
            Assert.That(Task.Run(() => snapshot.Values["ATK"]).Result, Is.EqualTo(704).Within(Tolerance));
            Assert.That(snapshot.Revision, Is.EqualTo(1));
        }

        [Test]
        public void T15_BreakdownExplainsUpstreamSourcesAndKeepsItsRevision()
        {
            using var set = Character();
            Equip(set);
            var first = set.GetBreakdown("ATK");
            Assert.That(first.Inputs["ATKPercentBonus"].Modifiers.Single().Key, Is.EqualTo(new ModifierKey(Weapon, 2)));
            var expected = new[] { 300d, 390d, 440d, 704d };
            for (int i = 0; i < expected.Length; i++)
                Assert.That(first.Steps[i].Value, Is.EqualTo(expected[i]).Within(Tolerance));
            Assert.That(first.Value, Is.EqualTo(set.GetValue("ATK")));
            var key = Ok(set.AddModifier(Mod(Effect, 1, "ATK", 99, Override))).Key;
            int calls = 0;
            using var sub = set.Subscribe(_ => calls++);
            var second = set.GetBreakdown("ATK");
            Assert.That(second.WinningOverride, Is.EqualTo(key));
            Assert.That(second.Value, Is.EqualTo(99));
            Assert.That(first.Value, Is.EqualTo(704).Within(Tolerance));
            Assert.That(first.Revision, Is.EqualTo(1));
            Assert.That(second.Revision, Is.EqualTo(set.Revision));
            Assert.That(calls, Is.Zero);
        }
    }
}
