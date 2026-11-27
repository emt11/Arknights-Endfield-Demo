using System.Linq;
using Endfield.Actors;
using Endfield.Editor.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Endfield.Animation.Tests
{
    public sealed class AnimationDefinationTests
    {
        public static AnimationDefination Asset(string member) =>
            AssetDatabase.LoadAssetAtPath<AnimationDefination>($"Assets/ScriptObjects/Animation/{member}QuickshiftF.asset");

        [TestCase("Phoebe", "GS_Quickshift_F", 1.2327f, 2.0159f)]
        [TestCase("Si", "DK_Quickshift_F", 2.2753f, 2.8072f)]
        public void LiveDeltasMatchTheCharacterAndItsForwardClip(string member, string clip, float atTenth, float end)
        {
            var source = Asset(member);
            Assert.That(source.Clip.name, Is.EqualTo(clip));
            var reference = AnimationDefinationEditor.SampleClip(source.Clip, Model(member));
            Assert.That(reference[3].position.z, Is.EqualTo(atTenth).Within(.01f));
            Assert.That(reference[source.LastFrame].position.z, Is.EqualTo(end).Within(.01f));
            CompareAnimatedRoot(source, member, reference);
        }

        internal static GameObject Model(string member) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Characters/{member}.prefab");

        internal static AnimationClip StationaryClip() =>
            AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Animations/Characters/Clips/GreatSword/Normal/GS_Evade.fbx")
                .OfType<AnimationClip>().Single(c => c.name == "GS_Evade");

        internal static RuntimeAnimatorController Locomotion(string member) =>
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Game/Animations/Characters/Controllers/" +
                (member == "Si" ? "Si_Locomotion.overrideController" : "Phoebe_Controller.controller"));

        internal static AnimationSource CreatePreviewSource(Animator animator, RuntimeAnimatorController controller)
        {
            var source = animator.gameObject.AddComponent<AnimationSource>();
            using var settings = new SerializedObject(source);
            settings.FindProperty("locomotionController").objectReferenceValue = controller;
            settings.ApplyModifiedPropertiesWithoutUndo();
            source.Initialize();
            return source;
        }

        private static void CompareAnimatedRoot(AnimationDefination trajectory, string member, Pose[] reference)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            PlayableGraph graph = default;
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(Model(member), scene);
                root.transform.rotation = Quaternion.Euler(0, 60, 0);
                var animator = root.GetComponent<Combatant>().ModelAnimator;
                Quaternion toLocal = Quaternion.Inverse(root.transform.rotation);
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var source = CreatePreviewSource(animator, Locomotion(member));
                var controller = ActorAnimationSceneTests.Controller(animator);
                graph = controller.GetGraph();
                Vector3 start = animator.transform.localPosition;
                for (int playback = 0; playback < 2; playback++)
                {
                    controller.SetBool("IsDodging", true);
                    source.Advance(0);
                    Assert.That(controller.GetNextAnimatorClipInfo(0)[0].clip, Is.SameAs(trajectory.Clip));
                    Vector3 position = Vector3.zero;
                    Vector3 positionAfterBlend = Vector3.zero;
                    int firstUnblendedFrame = 0;
                    for (int i = 1; i <= trajectory.LastFrame; i++)
                    {
                        double time = trajectory.FrameTime(i);
                        source.Advance(time - trajectory.FrameTime(i - 1));
                        position += toLocal * animator.deltaPosition;
                        Assert.That(Vector3.Distance(animator.transform.localPosition, start), Is.LessThan(.001f));
                        if (controller.IsInTransition(0)) continue;
                        if (firstUnblendedFrame == 0)
                        {
                            firstUnblendedFrame = i;
                            positionAfterBlend = position;
                        }
                        Vector3 actualDelta = position - positionAfterBlend;
                        Vector3 expectedDelta = reference[i].position - reference[firstUnblendedFrame].position;
                        Vector3 difference = actualDelta - expectedDelta;
                        Assert.That(new Vector2(difference.x, difference.z).magnitude, Is.LessThan(.01f),
                            $"{trajectory.name} 的第 {playback + 1} 次播放第 {i} 帧，实测 {actualDelta}，参考 {expectedDelta}");
                    }
                    Assert.That(firstUnblendedFrame, Is.InRange(1, trajectory.LastFrame - 1));
                    Assert.That(positionAfterBlend.z, Is.GreaterThan(0f));
                    controller.SetBool("IsDodging", false);
                    source.Advance(0);
                    source.Advance(.1);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (graph.IsValid()) graph.Destroy();
            }
        }

        [Test]
        public void StationaryClipKeepsZeroHorizontalDistance()
        {
            var samples = AnimationDefinationEditor.SampleClip(StationaryClip(), Model("Phoebe"));
            Assert.That(samples.Max(p => p.position.magnitude), Is.LessThan(.001f));
        }
    }
}
