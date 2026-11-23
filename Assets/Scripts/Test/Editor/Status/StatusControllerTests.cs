using System.Collections.Generic;
using Endfield.Effects;
using Endfield.Resources;
using Endfield.Stats;
using NUnit.Framework;
using UnityEditor;

namespace Endfield.Status.Tests
{
    public sealed class StatusControllerTests
    {
        private StatSet stats;
        private StatusRuleProfile rules;
        private CharacterResourceContainer resources;
        private EffectController effects;
        private StatusController status;

        [SetUp]
        public void CreateTarget()
        {
            stats = AssetDatabase.LoadAssetAtPath<CharacterStatProfile>("Assets/ScriptObjects/Stats/Phoebe.asset").CreateSet();
            rules = AssetDatabase.LoadAssetAtPath<StatusRuleProfile>("Assets/ScriptObjects/Status/CombatStatusRules.asset");
            resources = new CharacterResourceContainer(stats, 100, 0, 100);
            effects = new EffectController(stats);
            status = new StatusController(rules, resources, effects, stats);
        }

        [TearDown]
        public void ReleaseTarget()
        {
            status.Dispose();
            effects.Dispose();
            resources.Dispose();
        }

        [Test]
        public void VulnerabilityCapsConsumesAndExpiresFromTheRefresh()
        {
            var events = new List<IReadOnlyList<StatusChange>>();
            using var subscription = status.Subscribe(events.Add);
            status.ApplyVulnerable(2);
            effects.Advance(2);
            status.ApplyVulnerable(2);
            Assert.That(status.GetStacks(StatusType.Vulnerable), Is.EqualTo(3));
            Assert.That(status.PhysicalDamageTakenMultiplier, Is.EqualTo(1.3).Within(1e-9));
            Assert.That(status.TryConsume(StatusType.Vulnerable, 4), Is.False);
            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(status.TryConsume(StatusType.Vulnerable, 2), Is.True);
            effects.Advance(3);
            Assert.That(status.PhysicalDamageTakenMultiplier, Is.EqualTo(1.1).Within(1e-9));
            effects.Advance(2);
            Assert.That(status.PhysicalDamageTakenMultiplier, Is.EqualTo(1));
            Assert.That(events.Count, Is.EqualTo(4));
            Assert.That((events[3][0].PreviousStacks, events[3][0].CurrentStacks), Is.EqualTo((1, 0)));
        }

        [Test]
        public void HeatThenCryoPublishesOneCommittedReactionAndCancelsOldTimer()
        {
            status.ApplyAttachment(AttachmentElement.Heat, 2);
            effects.Advance(4);
            var snapshots = new List<(int Heat, int Cryo, bool Frozen)>();
            using var subscription = status.Subscribe(_ => snapshots.Add((status.GetStacks(StatusType.Heat),
                status.GetStacks(StatusType.Cryo), status.IsActive(StatusType.Frozen))));
            var reaction = status.ApplyAttachment(AttachmentElement.Cryo, 1);
            Assert.That(snapshots, Is.EqualTo(new[] { (0, 0, true) }));
            Assert.That(reaction.Count, Is.EqualTo(2));
            Assert.That((reaction[0].Type, reaction[0].PreviousStacks, reaction[0].CurrentStacks),
                Is.EqualTo((StatusType.Heat, 2, 0)));
            Assert.That(reaction[1].Type, Is.EqualTo(StatusType.Frozen));
            effects.Advance(1);
            Assert.That(snapshots.Count, Is.EqualTo(1));
            effects.Advance(1);
            Assert.That(snapshots[1], Is.EqualTo((0, 0, false)));

            status.ApplyAttachment(AttachmentElement.Cryo, 1);
            Assert.That(status.GetStacks(StatusType.Cryo), Is.EqualTo(1));
            status.ApplyAttachment(AttachmentElement.Heat, 1);
            Assert.That(status.IsActive(StatusType.Cryo), Is.False);
            Assert.That(status.IsActive(StatusType.Frozen), Is.False);
            status.ApplyAttachment(AttachmentElement.Heat, 3);
            Assert.That(status.GetStacks(StatusType.Heat), Is.EqualTo(2));
            Assert.That(status.TryConsume(StatusType.Heat, 2), Is.True);
            Assert.That(status.IsActive(StatusType.Heat), Is.False);
        }

        [Test]
        public void StaggerExitsAtItsOwnDeadlineResetsGaugeAndCanReenter()
        {
            var changes = new List<(int Stacks, double Gauge)>();
            using var subscription = status.Subscribe(batch =>
            {
                foreach (var change in batch)
                    if (change.Type == StatusType.Staggered)
                        changes.Add((change.CurrentStacks, resources.Snapshot.Stagger));
            });
            Assert.That(status.ApplyStagger(80), Is.False);
            Assert.That(status.ApplyStagger(35), Is.True);
            effects.Advance(1);
            Assert.That(status.ApplyStagger(10), Is.False);
            effects.Advance(0);
            Assert.That(resources.Snapshot.Stagger, Is.EqualTo(100));
            status.ApplyAttachment(AttachmentElement.Heat, 1);
            status.ApplyAttachment(AttachmentElement.Cryo, 1);
            effects.Advance(1);
            Assert.That(changes, Is.EqualTo(new[] { (1, 100d), (0, 0d) }));
            Assert.That(status.IsActive(StatusType.Frozen), Is.True);
            Assert.That(status.ApplyStagger(100), Is.True);
            effects.Advance(1);
            Assert.That(status.IsActive(StatusType.Staggered), Is.True);
            Assert.That(status.IsActive(StatusType.Frozen), Is.False);

            using var noGauge = new CharacterResourceContainer(stats, 0, 0, 0);
            using var noEffects = new EffectController(stats);
            using var noStatus = new StatusController(rules, noGauge, noEffects, stats);
            int notifications = 0;
            using var listener = noStatus.Subscribe(_ => notifications++);
            Assert.That(noStatus.ApplyStagger(100), Is.False);
            Assert.That((noGauge.Snapshot.Stagger, notifications), Is.EqualTo((0d, 0)));
        }
    }
}
