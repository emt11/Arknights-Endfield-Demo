using System.Collections.Generic;
using Endfield.Common;
using Endfield.Resources;
using Endfield.Stats;
using Endfield.Timing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Endfield.CombatContracts.Tests
{
    public sealed class CombatContractsTests
    {
        [Test]
        public void HitKeyDistinguishesAttackWindowAndTarget()
        {
            var seen = new HashSet<(AttackId, HitWindowId, ActorId)>();
            bool Record(uint source, uint sequence, uint window, uint target) =>
                seen.Add((new AttackId(new ActorId(source), sequence), new HitWindowId(window), new ActorId(target)));

            Assert.That(Record(1, 7, 1, 2), Is.True);
            Assert.That(Record(1, 7, 1, 2), Is.False);
            Assert.That(Record(1, 7, 2, 2), Is.True);
            Assert.That(Record(1, 7, 1, 3), Is.True);
            Assert.That(Record(3, 7, 1, 2), Is.True);
            Assert.That(Record(1, 8, 1, 2), Is.True);
        }

        [Test]
        public void AppliedHealthLossIdentifiesOnlyTheActorToFreeze()
        {
            var stats = AssetDatabase.LoadAssetAtPath<CharacterStatProfile>("Assets/ScriptObjects/Stats/Phoebe.asset").CreateSet();
            using var resources = AssetDatabase.LoadAssetAtPath<CharacterResourceProfile>(
                "Assets/ScriptObjects/CombatResources/Phoebe.asset").CreateContainer(stats);
            var clock = new BattleClock();
            var source = new ActorId(1);
            var target = new ActorId(2);
            clock.RegisterActor(source);
            clock.RegisterActor(target);
            clock.Advance(1);
            var hit = new HitContext(new AttackId(source, 7), new HitWindowId(1), target, Vector3.up, clock.Battle.Elapsed);
            var publisher = new ChangeNotifier<DamageApplied>();
            var facts = new List<DamageApplied>();
            double observedHP = -1;
            using var subscription = publisher.Subscribe(fact =>
            {
                observedHP = resources.Snapshot.CurrentHP;
                facts.Add(fact);
                clock.TryRequestHitStop(fact.Target, 0.030);
            });

            void Apply(double damage)
            {
                var loss = resources.ApplyDamage(damage);
                if (loss.HPLost > 0)
                    publisher.Publish(new DamageApplied(hit.Attack.Attacker, hit.Target, clock.Battle.Elapsed, loss.HPLost, loss.Overkill));
            }

            Apply(190);
            clock.Advance(0.020);
            Apply(20);
            Assert.That(facts.Count, Is.EqualTo(1));
            var fact = facts[0];
            Assert.That((fact.Source, fact.Target, fact.BattleTime, fact.HPLost, fact.Overkill),
                Is.EqualTo((source, target, 1d, 150d, 40d)));
            Assert.That((hit.WorldPoint, hit.BattleTime, observedHP), Is.EqualTo((Vector3.up, 1d, 0d)));
            Assert.That(clock.TryGetActionTime(target, out var victim), Is.True);
            Assert.That(clock.TryGetActionTime(source, out var attacker), Is.True);
            Assert.That(victim.Delta, Is.Zero);
            Assert.That(attacker.Delta, Is.EqualTo(0.020));
        }
    }
}
