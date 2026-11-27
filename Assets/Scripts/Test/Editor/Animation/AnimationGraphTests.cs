using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Endfield.Animation.Tests
{
    public sealed class AnimationGraphTests
    {
        [TestCase("Phoebe")]
        [TestCase("Si")]
        public void ControllerParametersMatchReferenceBonePoses(string member)
        {
            var data = AnimationDefinationTests.Asset(member);
            var controller = AnimationDefinationTests.Locomotion(member);
            var scene = EditorSceneManager.NewPreviewScene();
            PlayableGraph reference = default;
            PlayableGraph actualGraph = default;
            try
            {
                Animator Model()
                {
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(AnimationDefinationTests.Model(member), scene);
                    var animator = root.GetComponentInChildren<Animator>();
                    animator.runtimeAnimatorController = null;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    return animator;
                }
                var actual = Model();
                var expected = Model();
                var source = AnimationDefinationTests.CreatePreviewSource(actual, controller);
                var actualController = ActorAnimationSceneTests.Controller(actual);
                actualGraph = actualController.GetGraph();
                reference = PlayableGraph.Create("步态与前闪参考姿态");
                reference.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var referenceController = AnimatorControllerPlayable.Create(reference, controller);
                AnimationPlayableOutput.Create(reference, "参考姿态", expected).SetSourcePlayable(referenceController);
                reference.Play();
                void Compare(float dt)
                {
                    reference.Evaluate(dt);
                    source.Advance(dt);
                    var a = actual.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                    var b = expected.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                    Assert.That(Quaternion.Angle(a.localRotation, b.localRotation), Is.LessThan(.5f));
                }
                Compare(0);
                foreach (float amount in new[] { 0f, 1f, 1.637522f, 2f })
                {
                    referenceController.SetFloat("MoveAmount", amount);
                    actualController.SetFloat("MoveAmount", amount);
                    Compare(.1f);
                }
                int baseline = actualGraph.GetPlayableCount();
                for (int i = 0; i < 10; i++)
                {
                    referenceController.SetBool("IsDodging", true);
                    actualController.SetBool("IsDodging", true);
                    Compare(0);
                    Assert.That(actualController.GetNextAnimatorClipInfo(0)[0].clip, Is.SameAs(data.Clip));
                    Compare(.02f);
                    Assert.That(actualController.GetAnimatorTransitionInfo(0).normalizedTime, Is.InRange(.39f, .41f));
                    Compare(.18f);
                    Assert.That(actualController.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(.2).Within(1e-6));
                    Compare(.8f);
                    referenceController.SetBool("IsDodging", false);
                    actualController.SetBool("IsDodging", false);
                    Compare(0);
                    Assert.That(actualController.GetNextAnimatorStateInfo(0).IsName("Base Layer.Locomotion"), Is.True);
                    Compare(.04f);
                    Assert.That(actualController.GetAnimatorTransitionInfo(0).normalizedTime, Is.InRange(.49f, .51f));
                    Compare(.05f);
                    Assert.That(actualController.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Locomotion"), Is.True);
                    Assert.That(actualGraph.GetPlayableCount(), Is.EqualTo(baseline));
                }
            }
            finally
            {
                if (reference.IsValid()) reference.Destroy();
                EditorSceneManager.ClosePreviewScene(scene);
                if (actualGraph.IsValid()) actualGraph.Destroy();
            }
        }
    }
}
