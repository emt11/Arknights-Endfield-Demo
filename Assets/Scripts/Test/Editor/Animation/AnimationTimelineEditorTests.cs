using System.Linq;
using Endfield.Actors;
using Endfield.Editor.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Endfield.Animation.Tests
{
    public sealed class AnimationTimelineEditorTests
    {
        [TestCase("Phoebe", 1.2327f)]
        [TestCase("Si", 2.2753f)]
        public void ScenePreviewScrubsTheActorAndRestoresItsPoseAndPrefabOverrides(string member, float distance)
        {
            var asset = AnimationDefinationTests.Asset(member);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var actor = (GameObject)PrefabUtility.InstantiatePrefab(AnimationDefinationTests.Model(member), scene);
                var startPosition = new Vector3(3, 1, 2);
                var startRotation = Quaternion.Euler(0, 90, 0);
                actor.transform.SetPositionAndRotation(startPosition, startRotation);
                PrefabUtility.RecordPrefabInstancePropertyModifications(actor.transform);

                var animator = actor.GetComponent<Combatant>().ModelAnimator;
                var bone = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                var startPose = bone.localRotation;
                bool fireEvents = animator.fireEvents;
                bool dirty = scene.isDirty;
                int sceneCount = EditorSceneManager.previewSceneCount;
                var overrides = GetOverrides(actor);

                using (var preview = new AnimationTimelinePreview(asset, null, animator, actor.transform))
                {
                    preview.Seek(asset.FrameTime(3));
                    Assert.That(preview.ScenePreviewActive, Is.True);
                    var expected = startPosition + startRotation * Vector3.forward * distance;
                    Assert.That(Vector3.Distance(actor.transform.position, expected), Is.LessThan(.01f));
                    Assert.That(Quaternion.Angle(bone.localRotation, startPose), Is.GreaterThan(1f));
                    var framePose = bone.localRotation;

                    preview.Seek(asset.FrameTime(24));
                    preview.Seek(0);
                    Assert.That(Vector3.Distance(actor.transform.position, startPosition), Is.LessThan(.0001f));
                    preview.Seek(asset.FrameTime(3));
                    Assert.That(Vector3.Distance(actor.transform.position, expected), Is.LessThan(.01f));
                    Assert.That(Quaternion.Angle(bone.localRotation, framePose), Is.LessThan(.1f));
                }

                Assert.That(AnimationMode.InAnimationMode(), Is.False);
                Assert.That(Vector3.Distance(actor.transform.position, startPosition), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(actor.transform.rotation, startRotation), Is.LessThan(.1f));
                Assert.That(Quaternion.Angle(bone.localRotation, startPose), Is.LessThan(.1f));
                Assert.That(animator.enabled, Is.True);
                Assert.That(animator.fireEvents, Is.EqualTo(fireEvents));
                Assert.That(GetOverrides(actor), Is.EquivalentTo(overrides));
                Assert.That(scene.rootCount, Is.EqualTo(1));
                Assert.That(scene.isDirty, Is.EqualTo(dirty));
                Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(sceneCount));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static (Object target, string path, string value, Object reference)[] GetOverrides(GameObject actor) =>
            PrefabUtility.GetPropertyModifications(actor)
                .Select(property => (property.target, property.propertyPath, property.value, property.objectReference))
                .ToArray();

        [Test]
        public void MovingOneMarkerCanBeUndoneRedoneAndSaved()
        {
            var asset = Object.Instantiate(AnimationDefinationTests.Asset("Phoebe"));
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/ScriptObjects/Animation/测试时间线.asset");
            AssetDatabase.CreateAsset(asset, path);
            try
            {
                using var data = new SerializedObject(asset);
                var list = data.FindProperty("pointNotifies");
                list.arraySize = 1;
                list.GetArrayElementAtIndex(0).managedReferenceValue = new AnimationPointNotify("", 3);
                data.ApplyModifiedPropertiesWithoutUndo();
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                AnimationTimelineEditor.MoveBoundary(list, 0, "pointFrame", 4, asset.LastFrame);
                data.ApplyModifiedProperties();
                AnimationTimelineEditor.MoveBoundary(list, 0, "pointFrame", 5, asset.LastFrame);
                data.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                Assert.That(asset.PointNotifies[0].PointFrame, Is.EqualTo(5));
                Undo.PerformUndo();
                Assert.That(asset.PointNotifies[0].PointFrame, Is.EqualTo(3));
                Undo.PerformRedo();
                Assert.That(asset.PointNotifies[0].PointFrame, Is.EqualTo(5));
                AssetDatabase.SaveAssetIfDirty(asset);
                Assert.That(System.IO.File.ReadAllText(path), Does.Contain("pointFrame: 5"));
            }
            finally
            {
                Undo.ClearUndo(asset);
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void NewWindowKeepsItsBoundsThroughEditingReorderAndReload()
        {
            var asset = Object.Instantiate(AnimationDefinationTests.Asset("Phoebe"));
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/ScriptObjects/Animation/测试通知窗口.asset");
            AssetDatabase.CreateAsset(asset, path);
            try
            {
                using var data = new SerializedObject(asset);
                var windows = data.FindProperty("windowNotifies");
                windows.arraySize = 1;
                windows.GetArrayElementAtIndex(0).managedReferenceValue = new AnimationWindowNotify("旧窗口", 3, 6);
                data.ApplyModifiedPropertiesWithoutUndo();
                AnimationDefinationEditor.AddNotify(windows, 3, asset.LastFrame);
                data.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(asset.WindowNotifies.Select(window => (window.StartFrame, window.EndFrame)),
                    Is.EqualTo(new[] { (3, 6), (3, 4) }));

                Undo.IncrementCurrentGroup();
                AnimationTimelineEditor.MoveBoundary(windows, 1, "endFrame", 5, asset.LastFrame);
                data.ApplyModifiedProperties();
                Undo.FlushUndoRecordObjects();
                Assert.That(asset.WindowNotifies[1].EndFrame, Is.EqualTo(5));
                Undo.PerformUndo();
                Assert.That(asset.WindowNotifies[1].EndFrame, Is.EqualTo(4));
                Undo.PerformRedo();
                Assert.That(asset.WindowNotifies[1].EndFrame, Is.EqualTo(5));

                data.Update();
                windows.MoveArrayElement(1, 0);
                data.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(asset);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var reopened = AssetDatabase.LoadAssetAtPath<AnimationDefination>(path);
                Assert.That(reopened.WindowNotifies.Select(window => (window.StartFrame, window.EndFrame)),
                    Is.EqualTo(new[] { (3, 5), (3, 6) }));
            }
            finally
            {
                Undo.ClearUndo(asset);
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void FadeTimesUseClipSecondsAndCurvesOnlyAffectPoseWeight()
        {
            var asset = AnimationDefinationTests.Asset("Phoebe");
            var settings = new AnimationPreviewSettings
            {
                speed = 2,
                fadeInDuration = .1f,
                fadeOutDuration = .2f
            };
            Assert.That(settings.EvaluateWeight(.0125 * settings.speed, asset.Duration), Is.EqualTo(.25).Within(.0001));
            Assert.That(settings.EvaluateWeight(.9, asset.Duration), Is.EqualTo(.5).Within(.0001));
            settings.fadeInCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
            Assert.That(settings.EvaluateWeight(.025, asset.Duration), Is.EqualTo(.15625).Within(.0001));
            Assert.That(AnimationDefinationEditor.SampleClip(asset.Clip, AnimationDefinationTests.Model("Phoebe"))[3].position.z,
                Is.EqualTo(1.2327).Within(.01));
        }
    }
}
