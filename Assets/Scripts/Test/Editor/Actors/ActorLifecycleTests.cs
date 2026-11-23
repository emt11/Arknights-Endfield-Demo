using System.Collections.Generic;
using Endfield.CombatContracts;
using Endfield.Resources;
using Endfield.Stats;
using NUnit.Framework;
using UnityEditor;

namespace Endfield.Actors.Tests
{
    public sealed class ActorLifecycleTests
    {
        private static StatSet CreateStats() =>
            AssetDatabase.LoadAssetAtPath<CharacterStatProfile>("Assets/ScriptObjects/Stats/Phoebe.asset").CreateSet();

        [Test]
        public void LethalDamagePublishesCommittedStateOnce()
        {
            using var resources = new CharacterResourceContainer(CreateStats(), 100, 90, 100);
            var id = new ActorId(1);
            using var life = new ActorLifecycle(id, resources);
            var observations = new List<(ActorId, ActorLifeState, double)>();
            using var listener = life.SubscribeDied(dead => observations.Add((dead, life.State, resources.Snapshot.CurrentHP)));
            resources.ApplyDamage(40);
            Assert.That(life.RestoreHP(10), Is.EqualTo(10));
            Assert.That(resources.Snapshot.CurrentHP, Is.EqualTo(120));
            var hit = resources.ApplyDamage(150);
            Assert.That((hit.HPLost, hit.Overkill), Is.EqualTo((120d, 30d)));
            resources.ApplyDamage(1);
            resources.RestoreUltimate(5);
            Assert.That(life.RestoreHP(20), Is.Zero);
            Assert.That(observations, Is.EqualTo(new[] { (id, ActorLifeState.Dead, 0d) }));
        }

        [Test]
        public void ZeroMaxHealthKillsOnceAndStartingAtZeroDoesNotReplayDeath()
        {
            var stats = CreateStats();
            using var resources = new CharacterResourceContainer(stats, 100, 90, 100);
            using var life = new ActorLifecycle(new ActorId(1), resources);
            int deaths = 0;
            using var listener = life.SubscribeDied(_ => deaths++);
            var source = new SourceID("Test", 1);
            Assert.That(stats.ReplaceSource(source, new StatModifier(StatId.MaxHP, ModifierOperation.Override, 0)).Success, Is.True);
            using var atZero = new ActorLifecycle(new ActorId(2), resources);
            int replayed = 0;
            using var otherListener = atZero.SubscribeDied(_ => replayed++);
            Assert.That(stats.RemoveSource(source).Success, Is.True);
            resources.RestoreUltimate(5);
            Assert.That((deaths, replayed), Is.EqualTo((1, 0)));
            Assert.That(atZero.State, Is.EqualTo(ActorLifeState.Dead));
            Assert.That(life.RestoreHP(10), Is.Zero);
        }

        [Test]
        public void RemovalEndsHealingAndDeathNotifications()
        {
            using var resources = new CharacterResourceContainer(CreateStats(), 100, 90, 100);
            var life = new ActorLifecycle(new ActorId(1), resources);
            int deaths = 0;
            using var listener = life.SubscribeDied(_ => deaths++);
            life.Dispose();
            resources.ApplyDamage(150);
            Assert.That(life.State, Is.EqualTo(ActorLifeState.Removed));
            Assert.That(life.RestoreHP(20), Is.Zero);
            Assert.That(deaths, Is.Zero);
        }
    }
}
