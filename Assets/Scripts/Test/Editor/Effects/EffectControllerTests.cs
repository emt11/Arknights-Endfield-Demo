using System;
using System.Collections.Generic;
using Endfield.CharacterBuild;
using Endfield.Stats;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Endfield.Effects.Tests
{
    public sealed class EffectControllerTests
    {
        private StatSet stats;
        private EffectController effects;
        private readonly List<EffectProfile> copies = new();

        [SetUp]
        public void CreateTarget()
        {
            stats = AssetDatabase.LoadAssetAtPath<CharacterStatProfile>("Assets/ScriptObjects/Stats/Phoebe.asset").CreateSet();
            effects = new EffectController(stats);
        }

        [TearDown]
        public void ReleaseTarget()
        {
            effects.Dispose();
            foreach (var copy in copies) Object.DestroyImmediate(copy);
            copies.Clear();
        }

        private EffectProfile CreateProfile(StatModifier? modifier = null, double duration = 3,
            double bonus = 0, double interval = 0)
        {
            var profile = ScriptableObject.CreateInstance<EffectProfile>();
            copies.Add(profile);
            var data = new SerializedObject(profile);
            data.FindProperty("durationSeconds").doubleValue = duration;
            data.FindProperty("outgoingDamageBonus").doubleValue = bonus;
            if (modifier.HasValue)
            {
                var entries = data.FindProperty("modifiers");
                entries.arraySize = 1;
                var entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("statId").intValue = (int)modifier.Value.StatId;
                entry.FindPropertyRelative("operation").intValue = (int)modifier.Value.Operation;
                entry.FindPropertyRelative("value").doubleValue = modifier.Value.Value;
                entry.FindPropertyRelative("priority").intValue = modifier.Value.Priority;
            }
            if (interval > 0)
            {
                data.FindProperty("pulse").intValue = (int)PulseKind.RestoreUltimate;
                data.FindProperty("intervalSeconds").doubleValue = interval;
                data.FindProperty("amount").doubleValue = 5;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        private EffectHandle Apply(EffectProfile profile)
        {
            Assert.That(effects.TryApply(profile, stats, out var handle, out var error), Is.True, error);
            return handle;
        }

        [Test]
        public void TwoInstancesRefreshAndExpireWithoutRemovingEquipment()
        {
            const string folder = "Assets/ScriptObjects/CharacterBuild/";
            var build = CharacterBuildState.Create(stats, AssetDatabase.LoadAssetAtPath<CharacterBuildProfile>(folder + "Phoebe.asset"));
            Assert.That(build.TryEquip(BuildSlot.Weapon, AssetDatabase.LoadAssetAtPath<BuildModifierProfile>(
                folder + "WoodenSword.asset"), out var error), Is.True, error);
            void CheckAttack(double value) => Assert.That(stats.GetValue(StatId.ATK), Is.EqualTo(value).Within(1e-8));
            CheckAttack(710.24);
            var buff = CreateProfile(new StatModifier(StatId.ATK, ModifierOperation.Flat, 20));
            var first = Apply(buff);
            var second = Apply(buff);
            CheckAttack(750.24);
            using var other = new EffectController(AssetDatabase.LoadAssetAtPath<CharacterStatProfile>(
                "Assets/ScriptObjects/Stats/Si.asset").CreateSet());
            Assert.That(other.TryRemove(first, out _), Is.False);
            Assert.That(other.TryRefresh(second), Is.False);
            effects.Advance(2);
            Assert.That(effects.TryRefresh(second), Is.True);
            effects.Advance(1);
            CheckAttack(730.24);
            Assert.That(effects.TryRemove(first, out _), Is.False);
            Assert.That(effects.IsActive(second), Is.True);
            effects.Advance(2);
            CheckAttack(710.24);
            Assert.That(effects.IsActive(second), Is.False);
        }

        [Test]
        public void SimultaneousPulsesPrecedeExpiryAndRefreshPreservesPulseCadence()
        {
            var profile = CreateProfile(duration: .3, interval: .1);
            var a = Apply(profile);
            var b = Apply(profile);
            var events = new List<(EffectHandle, string)>();
            var sources = new List<IReadOnlyStatSet>();
            using var pulse = effects.SubscribePulse(p =>
            {
                events.Add((p.Handle, "周期"));
                sources.Add(p.Instigator);
            });
            using var ended = effects.SubscribeEnded(h => events.Add((h, "结束")));
            effects.Advance(.05);
            effects.Advance(0);
            Assert.That(effects.TryRefresh(b), Is.True);
            effects.Advance(.25);
            Assert.That(events, Is.EqualTo(new[] { (a, "周期"), (b, "周期"), (a, "周期"), (b, "周期"),
                (a, "周期"), (b, "周期"), (a, "结束") }));
            Assert.That(sources, Is.All.SameAs(stats));
            effects.Advance(.05);
            Assert.That(events[7], Is.EqualTo((b, "结束")));
        }

        [Test]
        public void ConflictDoesNotInstallDamageBonusAndRemovalFailureCanRetry()
        {
            var source = new SourceID("Equipment", 1);
            var replace = new StatModifier(StatId.DEF, ModifierOperation.Override, 140, 5);
            Assert.That(stats.ReplaceSource(source, replace).Success, Is.True);
            var conflict = CreateProfile(new StatModifier(StatId.DEF, ModifierOperation.Override, 200, 5), bonus: .25);
            Assert.That(effects.TryApply(conflict, stats, out var rejected, out var error), Is.False);
            Assert.That(error, Does.Contain("DEF"));
            Assert.That(effects.IsActive(rejected), Is.False);
            Assert.That(stats.GetValue(StatId.DEF), Is.EqualTo(140));
            Assert.That(effects.OutgoingDamageBonus, Is.Zero);
            var boost = Apply(AssetDatabase.LoadAssetAtPath<EffectProfile>("Assets/ScriptObjects/Effects/OutgoingDamageBoost.asset"));
            Assert.That(effects.OutgoingDamageBonus, Is.EqualTo(.25));
            Assert.That(effects.TryRemove(boost, out _), Is.True);

            var attack = stats.GetValue(StatId.ATK);
            var retry = Apply(CreateProfile(new StatModifier(StatId.ATK, ModifierOperation.Flat, 20), bonus: .25, interval: 1));
            int pulses = 0;
            using var pulse = effects.SubscribePulse(_ => pulses++);
            Exception failure = null;
            using (stats.Subscribe(_ =>
            {
                try { effects.Advance(3); }
                catch (InvalidOperationException ex) { failure = ex; }
            }))
            {
                stats.SetBase(StatId.BaseHP, 120);
            }
            Assert.That(failure, Is.Not.Null);
            Assert.That(effects.IsActive(retry), Is.True);
            Assert.That(effects.OutgoingDamageBonus, Is.EqualTo(.25));
            Assert.That(stats.GetValue(StatId.ATK), Is.EqualTo(attack + 20).Within(1e-8));
            effects.Advance(.01);
            Assert.That(effects.IsActive(retry), Is.False);
            Assert.That(effects.OutgoingDamageBonus, Is.Zero);
            Assert.That(stats.GetValue(StatId.ATK), Is.EqualTo(attack).Within(1e-8));
            Assert.That(pulses, Is.EqualTo(3));
        }
    }
}
