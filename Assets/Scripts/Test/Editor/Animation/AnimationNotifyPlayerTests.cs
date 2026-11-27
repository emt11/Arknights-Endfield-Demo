using System.Collections.Generic;
using System.Linq;
using Endfield.CombatContracts;
using Endfield.Editor.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Endfield.Animation.Tests
{
    public sealed class AnimationNotifyPlayerTests
    {
        private AnimationDefination asset;
        [SetUp]
        public void CreateClip()
        {
            asset = Object.Instantiate(AnimationDefinationTests.Asset("Phoebe"));
            var data = new SerializedObject(asset);
            var points = data.FindProperty("pointNotifies");
            points.arraySize = 2;
            for (int i = 0; i < points.arraySize; i++)
                points.GetArrayElementAtIndex(i).managedReferenceValue = new AnimationPointNotify("测试", 3);
            var windows = data.FindProperty("windowNotifies");
            windows.arraySize = 1;
            windows.GetArrayElementAtIndex(0).managedReferenceValue = new AnimationWindowNotify("测试", 3, 6);
            AnimationDefinationEditor.AddNotify(windows, 4, asset.LastFrame);
            windows.GetArrayElementAtIndex(1).FindPropertyRelative("endFrame").intValue = 7;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void DestroyClip() => Object.DestroyImmediate(asset);

        [Test]
        public void IndependentActorsDispatchIdenticalNoticesAcrossLargeAndSmallSteps()
        {
            List<string> Collect(uint id, bool split)
            {
                var player = new AnimationNotifyPlayer();
                var notices = new List<AnimationNotice>();
                var play = player.Begin(asset, new ActorId(id));
                using var subscription = player.Subscribe(notices.Add);
                if (split)
                    for (int i = 1; i <= 7; i++) player.AdvanceTo(play, asset.FrameTime(i));
                else player.AdvanceTo(play, asset.FrameTime(7));
                player.AdvanceTo(play, asset.FrameTime(7));
                player.AdvanceTo(play, asset.FrameTime(4));
                player.End(play, NotifyEndReason.Natural);
                Assert.That(notices.Select(n => n.ActorId.Value), Is.All.EqualTo(id));
                Assert.That(notices.Select(n => n.PlayId), Is.All.EqualTo(play));
                return notices.Select(n => $"{n.Frame}/{n.Phase}/{n.RuntimeIndex}").ToList();
            }
            var expected = new[]
            {
                "3/Point/0", "3/Point/1", "3/Enter/2",
                "4/Enter/3", "4/Tick/2",
                "5/Tick/2", "5/Tick/3",
                "6/Tick/3", "6/Exit/2",
                "7/Exit/3"
            };
            CollectionAssert.AreEqual(expected, Collect(1, true));
            CollectionAssert.AreEqual(expected, Collect(2, false));
        }

        [Test]
        public void ReplayingInsideEnterCleansOnlyTheOldWindow()
        {
            var player = new AnimationNotifyPlayer();
            long old = player.Begin(asset, new ActorId(1)), next = 0;
            var events = new List<AnimationNotice>();
            using var subscription = player.Subscribe(n =>
            {
                events.Add(n);
                if (n.PlayId == old && n.Phase == NotifyPhase.Enter) next = player.Begin(asset, n.ActorId);
            });
            player.AdvanceTo(old, asset.Duration);
            Assert.That(events.Last().Reason, Is.EqualTo(NotifyEndReason.Interrupted));
            Assert.That(events.Last().Frame, Is.EqualTo(3));
            Assert.That(events.Count, Is.EqualTo(4));
            player.End(old, NotifyEndReason.Interrupted);
            Assert.That(player.IsPlaying(next), Is.True);
            player.AdvanceTo(next, asset.Duration);
            Assert.That(events.Count(n => n.PlayId == next), Is.EqualTo(10));
        }

        [Test]
        public void InterruptedWindowReportsTheScannedFrameAndNaturalEndConsumesTheLastFrame()
        {
            using var data = new SerializedObject(asset);
            var points = data.FindProperty("pointNotifies");
            points.arraySize = 1;
            points.GetArrayElementAtIndex(0).managedReferenceValue = new AnimationPointNotify("末帧", asset.LastFrame);
            var windows = data.FindProperty("windowNotifies");
            windows.arraySize = 1;
            windows.GetArrayElementAtIndex(0).FindPropertyRelative("endFrame").intValue = asset.LastFrame;
            data.ApplyModifiedPropertiesWithoutUndo();

            var player = new AnimationNotifyPlayer();
            var notices = new List<AnimationNotice>();
            using var subscription = player.Subscribe(notices.Add);
            long play = player.Begin(asset, default);
            player.AdvanceTo(play, -1);
            Assert.That(notices, Is.Empty);
            player.AdvanceTo(play, asset.FrameTime(7));
            player.AdvanceTo(play, asset.FrameTime(4));
            player.End(play, NotifyEndReason.Interrupted);
            player.End(play, NotifyEndReason.Interrupted);
            Assert.That(notices.Select(n => (n.Frame, n.Phase, n.Reason)), Is.EqualTo(new[]
            {
                (3, NotifyPhase.Enter, NotifyEndReason.Natural),
                (4, NotifyPhase.Tick, NotifyEndReason.Natural),
                (5, NotifyPhase.Tick, NotifyEndReason.Natural),
                (6, NotifyPhase.Tick, NotifyEndReason.Natural),
                (7, NotifyPhase.Tick, NotifyEndReason.Natural),
                (7, NotifyPhase.Exit, NotifyEndReason.Interrupted)
            }));

            notices.Clear();
            long next = player.Begin(asset, default);
            player.AdvanceTo(play, asset.Duration);
            Assert.That(notices, Is.Empty);
            player.AdvanceTo(next, asset.Duration - 1e-12);
            var throughPenultimateFrame = new[] { (3, NotifyPhase.Enter) }
                .Concat(Enumerable.Range(4, asset.LastFrame - 4).Select(frame => (frame, NotifyPhase.Tick)));
            Assert.That(notices.Select(n => (n.Frame, n.Phase)), Is.EqualTo(throughPenultimateFrame));
            player.End(next, NotifyEndReason.Natural);
            Assert.That(notices.Select(n => (n.Frame, n.Phase)), Is.EqualTo(throughPenultimateFrame.Concat(new[]
            {
                (asset.LastFrame, NotifyPhase.Point),
                (asset.LastFrame, NotifyPhase.Exit)
            })));
        }
    }
}
