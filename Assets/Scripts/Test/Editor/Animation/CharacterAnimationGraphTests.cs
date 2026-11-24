using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.TestTools;

namespace Endfield.Animation.Tests
{
    public sealed class CharacterAnimationGraphTests
    {
        private const string Controllers = "Assets/Game/Animations/Characters/Controllers/";
        private GameObject actualModel, referenceModel;
        private Transform actualBone, referenceBone;
        private CharacterAnimationGraph driver;
        private PlayableGraph reference;
        private AnimatorControllerPlayable basePose;
        private AnimationMixerPlayable mix;
        private AnimationClipPlayable referenceAttack;

        internal static AnimationClip Clip(string prefix, string name)
        {
            string group = prefix == "GS" ? "GreatSword" : "DualKatana";
            string clipName = prefix + "_" + name;
            return AssetDatabase.LoadAllAssetsAtPath($"Assets/Game/Animations/Characters/Clips/{group}/Normal/{clipName}.fbx")
                .OfType<AnimationClip>().Single(c => c.name == clipName);
        }

        [UnitySetUp]
        public IEnumerator Enter() { yield return new EnterPlayMode(); }

        [UnityTearDown]
        public IEnumerator Leave()
        {
            driver?.Dispose();
            if (reference.IsValid()) reference.Destroy();
            Object.Destroy(actualModel);
            Object.Destroy(referenceModel);
            yield return new ExitPlayMode();
        }

        private void Bind(string character, string controllerName, float walk, float run, HumanBodyBones bone)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Characters/{character}.prefab");
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Controllers + controllerName);
            actualModel = Object.Instantiate(prefab);
            referenceModel = Object.Instantiate(prefab);
            var animator = actualModel.GetComponentInChildren<Animator>();
            var other = referenceModel.GetComponentInChildren<Animator>();
            animator.cullingMode = other.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = other.applyRootMotion = false;
            actualBone = animator.GetBoneTransform(bone);
            referenceBone = other.GetBoneTransform(bone);
            driver = new CharacterAnimationGraph(animator, controller, walk, run);
            reference = PlayableGraph.Create("参考姿态");
            reference.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            basePose = AnimatorControllerPlayable.Create(reference, controller);
            mix = AnimationMixerPlayable.Create(reference, 2);
            reference.Connect(basePose, 0, mix, 0);
            mix.SetInputWeight(0, 1f);
            AnimationPlayableOutput.Create(reference, "参考", other).SetSourcePlayable(mix);
            reference.Play();
            reference.Evaluate(0);
        }

        private void CompareStep(float seconds, float attackWeight)
        {
            mix.SetInputWeight(0, 1 - attackWeight);
            mix.SetInputWeight(1, attackWeight);
            reference.Evaluate(seconds);
            driver.Advance(seconds);
            Assert.That(Quaternion.Angle(actualBone.localRotation, referenceBone.localRotation), Is.LessThan(.5f));
        }

        [Test]
        public void OverrideMapsMeasuredGroundSpeedToDualKatanaGaits()
        {
            Bind("Si", "Si_Locomotion.overrideController", 1.54665f, 6.82405f, HumanBodyBones.RightUpperLeg);
            foreach (var sample in new[] { (0f, 0f), (1.54665f, 1f), (4.91111f, 1.637522f), (6.82405f, 2f) })
            {
                driver.SetGroundSpeed(sample.Item1);
                basePose.SetFloat("MoveAmount", sample.Item2);
                CompareStep(.131f, 0);
            }
            driver.PlayDodge(0);
            basePose.Play("Base Layer.Dodge", 0, 0);
            reference.Evaluate(0f);
            CompareStep(.09f, 0);
            Assert.That(driver.IsDodging, Is.True);
            Assert.That(basePose.GetCurrentAnimatorClipInfo(0)[0].clip, Is.SameAs(Clip("DK", "Evade")));
            CompareStep(1.42f, 0);
            CompareStep(.02f, 0);
            Assert.That(driver.IsDodging, Is.False);
        }

        [Test]
        public void RunningAttackAndDodgeSampleTheExpectedIntermediatePoses()
        {
            Bind("Phoebe", "Phoebe_Controller.controller", 1.43936f, 4.91111f, HumanBodyBones.RightUpperArm);
            driver.SetGroundSpeed(4.91111f);
            basePose.SetFloat("MoveAmount", 2);
            CompareStep(.11f, 0);
            var clip = Clip("GS", "Attack1");
            Assert.That(driver.TryPlay(clip, 1, .1f, .08f), Is.True);
            referenceAttack = AnimationClipPlayable.Create(reference, clip);
            reference.Connect(referenceAttack, 0, mix, 1);
            CompareStep(.025f, .25f);
            Assert.That(driver.CurrentClipTime, Is.EqualTo(.025).Within(1e-6));
            CompareStep(.075f, 1f);
            driver.PlayDodge(.08f);
            basePose.Play("Base Layer.Dodge", 0, 0);
            reference.Evaluate(0f);
            Assert.That(driver.CurrentClip, Is.Null);
            CompareStep(.04f, .5f);
            CompareStep(.04f, 0f);
            Assert.That(driver.IsDodging, Is.True);
        }

        [Test]
        public void NaturalAndRequestedExitsSampleTheCorrectPoseAtTheBoundary()
        {
            Bind("Phoebe", "Phoebe_Controller.controller", 1.43936f, 4.91111f, HumanBodyBones.RightUpperArm);
            var clip = Clip("GS", "Attack1");
            referenceAttack = AnimationClipPlayable.Create(reference, clip);
            reference.Connect(referenceAttack, 0, mix, 1);
            referenceAttack.SetSpeed(2);
            driver.TryPlay(clip, 2, 0, .08f);
            CompareStep(clip.length * .5f - .02f, 1f);

            // 独立参考图先到片尾，再以末姿和基础层各一半采样
            reference.Evaluate(.02f);
            referenceAttack.SetTime(clip.length);
            referenceAttack.SetSpeed(0);
            mix.SetInputWeight(0, .5f);
            mix.SetInputWeight(1, .5f);
            reference.Evaluate(.04f);
            driver.Advance(.06);
            Assert.That(driver.CurrentClip, Is.Null);
            Assert.That(Quaternion.Angle(actualBone.localRotation, referenceBone.localRotation), Is.LessThan(.5f));
            CompareStep(.05f, 0);

            reference.Disconnect(mix, 1);
            reference.DestroyPlayable(referenceAttack);
            referenceAttack = AnimationClipPlayable.Create(reference, clip);
            reference.Connect(referenceAttack, 0, mix, 1);
            driver.TryPlay(clip, 1, 0, 0);
            CompareStep(.125f, 1);
            driver.Stop(.08f);
            CompareStep(.04f, .5f);
            driver.Stop(1f);
            CompareStep(.04f, 0);

            reference.Disconnect(mix, 1);
            reference.DestroyPlayable(referenceAttack);
            referenceAttack = AnimationClipPlayable.Create(reference, clip);
            reference.Connect(referenceAttack, 0, mix, 1);
            driver.TryPlay(clip, 1, 0, 0);
            mix.SetInputWeight(0, 0);
            mix.SetInputWeight(1, 1);
            reference.Evaluate(clip.length);
            mix.SetInputWeight(0, 1);
            mix.SetInputWeight(1, 0);
            reference.Evaluate(0);
            driver.Advance(clip.length);
            Assert.That(driver.CurrentClip, Is.Null);
            Assert.That(Quaternion.Angle(actualBone.localRotation, referenceBone.localRotation), Is.LessThan(.5f));
        }

        [Test]
        public void ReplacingAndFinishingSingleSlotReleasesEveryAttackNode()
        {
            Bind("Si", "Si_Locomotion.overrideController", 1.54665f, 6.82405f, HumanBodyBones.RightUpperArm);
            var one = Clip("DK", "Attack1");
            var two = Clip("DK", "Attack2");
            var run = Clip("DK", "Run");
            // 直接检查 Unity 图拥有的节点，不给运行时接口增加测试计数器
            var graph = (PlayableGraph)typeof(CharacterAnimationGraph).GetField("graph",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(driver);
            int baseline = graph.GetPlayableCount();
            for (int i = 0; i < 10; i++)
            {
                Assert.That(driver.TryPlay(two, 1, 0, 0), Is.True);
                driver.Advance(.05);
                Assert.That(driver.TryPlay(one, 2, 0, .08f), Is.True);
                Assert.That(driver.CurrentClip, Is.SameAs(one));
                Assert.That(driver.CurrentClipTime, Is.Zero);
                Assert.That(driver.TryPlay(run, 1, 0, 0), Is.False);
                Assert.That(graph.GetPlayableCount(), Is.EqualTo(baseline + 1));
                driver.Advance(one.length * .5 - .02);
                if (i % 2 == 0) driver.Advance(.06);
                else
                {
                    driver.Advance(.02);
                    driver.Advance(.04);
                }
                Assert.That(driver.CurrentClip, Is.Null);
                Assert.That(driver.CurrentClipTime, Is.Zero);
                Assert.That(graph.GetPlayableCount(), Is.EqualTo(baseline + 1));
                driver.Advance(.041);
                Assert.That(graph.GetPlayableCount(), Is.EqualTo(baseline));
            }
        }
    }
}
