using Endfield.Animation;
using Endfield.Animation.Tests;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Endfield.MotionWarping.Tests
{
    public sealed class MotionWarpingTests
    {
        private MotionWarpingAsset asset;

        [SetUp]
        public void SetUp()
        {
            asset = ScriptableObject.CreateInstance<MotionWarpingAsset>();
            var data = new SerializedObject(asset);
            data.FindProperty("clip").objectReferenceValue = AnimationDefinationTests.Asset("Phoebe").Clip;
            data.FindProperty("sampleModel").objectReferenceValue = AnimationDefinationTests.Model("Phoebe");
            var windows = data.FindProperty("warpWindows");
            windows.arraySize = 1;
            var window = windows.GetArrayElementAtIndex(0);
            window.FindPropertyRelative("startFrame").intValue = 6;
            window.FindPropertyRelative("endFrame").intValue = 18;
            window.FindPropertyRelative("alignYaw").boolValue = true;
            window.FindPropertyRelative("targetOffsetXZ").vector2Value = new Vector2(0, .5f);
            data.ApplyModifiedPropertiesWithoutUndo();
            asset.StoreSummary(
                new[] { new WarpWindowMotion { startFrame = 6, endFrame = 18, displacement = Vector3.forward } });
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(asset);

        [Test]
        public void OffsetUsesTargetYawAndSnapshotSurvivesLossWithoutRepayingBlockedMotion()
        {
            var warp = new MotionWarping();
            var target = new GameObject("扭曲目标");
            try
            {
                target.transform.SetPositionAndRotation(new Vector3(2, 0, 3), Quaternion.Euler(0, 90, 0));
                Assert.That(warp.BeginWarp(asset, new[] { new WarpTarget(target.transform) }), Is.True);
                var origin = new Pose(new Vector3(2.5f, 0, 1.4f), Quaternion.identity);
                var first = warp.UpdateWarp(.2, .4, origin, Quaternion.identity, new PlanarMotion(Vector3.forward * .5f, Quaternion.identity));
                Assert.That(Vector3.Distance(first.Displacement, Vector3.forward * .8f), Is.LessThan(.0001));
                Assert.That(first.Rotation.eulerAngles.y, Is.EqualTo(45).Within(.001));
                Object.DestroyImmediate(target);
                var blocked = new Pose(origin.position + Vector3.forward * .4f, first.Rotation);
                var second = warp.UpdateWarp(.4, .6, blocked, Quaternion.identity, new PlanarMotion(Vector3.forward * .5f, Quaternion.identity));
                Assert.That(Vector3.Distance(second.Displacement, Vector3.forward * .8f), Is.LessThan(.0001));
                Assert.That(second.Rotation.eulerAngles.y, Is.EqualTo(90).Within(.001));
                warp.EndWarp();
            }
            finally { if (target) Object.DestroyImmediate(target); }
        }

        [Test]
        public void TargetMissingBeforeEntryKeepsOriginalAndMismatchedCountRejectsWarp()
        {
            var warp = new MotionWarping();
            Assert.That(warp.BeginWarp(asset, System.Array.Empty<WarpTarget>()), Is.False);
            Assert.That(warp.BeginWarp(asset, new[] { new WarpTarget(null) }), Is.True);
            Assert.That(warp.UpdateWarp(.2, .6, Pose.identity, Quaternion.identity, new PlanarMotion(Vector3.forward, Quaternion.identity)).Displacement.z, Is.EqualTo(1).Within(.001));
            warp.EndWarp();
            warp.EndWarp();
        }
    }
}
