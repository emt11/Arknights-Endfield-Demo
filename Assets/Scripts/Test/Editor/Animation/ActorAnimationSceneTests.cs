using System.Collections;
using Endfield.Actors;
using Endfield.MotionWarping;
using Endfield.CharacterControl;
using Endfield.CombatSession;
using Endfield.Timing;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Endfield.Animation.Tests
{
    public sealed class ActorAnimationSceneTests
    {
        [UnitySetUp]
        public IEnumerator Enter()
        {
            yield return new EnterPlayMode();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/CombatDemo.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Leave()
        {
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator StationaryClipPlaysDodgeWithoutDisplacement()
        {
            var asset = Object.Instantiate(AnimationDefinationTests.Asset("Phoebe"));
            var root = Object.Instantiate(AnimationDefinationTests.Model("Phoebe"), new Vector3(40, 0, 40), Quaternion.identity);
            var overrides = new AnimatorOverrideController(AnimationDefinationTests.Locomotion("Phoebe"));
            try
            {
                using var clipSettings = new SerializedObject(asset);
                clipSettings.FindProperty("clip").objectReferenceValue = AnimationDefinationTests.StationaryClip();
                clipSettings.ApplyModifiedPropertiesWithoutUndo();
                var member = root.AddComponent<PartyMemberControl>();
                overrides[AnimationDefinationTests.Asset("Phoebe").Clip] = asset.Clip;
                BindTestActor(root, asset, overrides);
                yield return null;
                var start = root.transform.position;

                member.SetControlInput(Vector3.zero, false, CharacterCommand.Dodge);
                member.Step(.1);
                Assert.That(member.CurrentState, Is.SameAs(member.Dodge));
                Assert.That(Controller(root.GetComponent<Combatant>().ModelAnimator).GetCurrentAnimatorClipInfo(0)[0].clip,
                    Is.SameAs(asset.Clip));
                Assert.That(Vector3.Distance(start, root.transform.position), Is.LessThan(.001f));
            }
            finally
            {
                Object.Destroy(root);
                Object.Destroy(asset);
                Object.Destroy(overrides);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DodgeWithoutRootMotionStaysInPlaceAndStillNotifies()
        {
            var asset = Object.Instantiate(AnimationDefinationTests.Asset("Phoebe"));
            var root = Object.Instantiate(AnimationDefinationTests.Model("Phoebe"), new Vector3(40, 0, 40), Quaternion.identity);
            var warp = ScriptableObject.CreateInstance<MotionWarpingAsset>();
            try
            {
                using var settings = new SerializedObject(asset);
                settings.FindProperty("useRootMotion").boolValue = false;
                settings.FindProperty("useMotionWarping").boolValue = true;
                settings.FindProperty("warpAsset").objectReferenceValue = warp;
                var points = settings.FindProperty("pointNotifies");
                points.arraySize = 1;
                points.GetArrayElementAtIndex(0).managedReferenceValue = new AnimationPointNotify("窗口结束", 6);
                settings.ApplyModifiedPropertiesWithoutUndo();
                var member = root.AddComponent<PartyMemberControl>();
                var capsule = root.GetComponent<CharacterController>();
                capsule.center = Vector3.up * .9f;
                capsule.height = 1.8f;
                capsule.radius = .25f;
                capsule.minMoveDistance = 0;
                Physics.SyncTransforms();
                BindTestActor(root, asset);
                yield return null;

                var start = root.transform.position;
                var facing = root.transform.rotation;
                var receivedFrames = new System.Collections.Generic.List<int>();
                using var subscription = member.Notifications.Subscribe(notice => receivedFrames.Add(notice.Frame));
                member.SetControlInput(Vector3.right, false, CharacterCommand.Dodge);
                member.Step(asset.FrameTime(3));
                Assert.That(Vector3.Distance(root.transform.position, start), Is.LessThan(.001f));
                member.Step(asset.FrameTime(6) - asset.FrameTime(3));
                Assert.That(Vector3.Distance(root.transform.position, start), Is.LessThan(.001f));
                member.Step(1);
                Assert.That(member.CurrentState, Is.SameAs(member.Walk));
                Assert.That(Vector3.Distance(root.transform.position, start), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(root.transform.rotation, facing), Is.LessThan(.1f));
                Assert.That(receivedFrames, Is.EqualTo(new[] { 6 }));
            }
            finally
            {
                Object.Destroy(root);
                Object.Destroy(warp);
                Object.Destroy(asset);
            }
        }

        [UnityTest]
        public IEnumerator ControllerDodgeUsesMotorCollisionAndStopsAtTheInterruptingNotice()
        {
            var asset = Object.Instantiate(AnimationDefinationTests.Asset("Phoebe"));
            var root = Object.Instantiate(AnimationDefinationTests.Model("Phoebe"));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var settings = new SerializedObject(asset);
                var marks = settings.FindProperty("pointNotifies");
                marks.arraySize = 2;
                marks.GetArrayElementAtIndex(0).managedReferenceValue = new AnimationPointNotify("首帧", 0);
                marks.GetArrayElementAtIndex(1).managedReferenceValue = new AnimationPointNotify("打断", 6);
                settings.ApplyModifiedPropertiesWithoutUndo();
                var reference = Endfield.Editor.Animation.AnimationDefinationEditor.SampleClip(asset.Clip, AnimationDefinationTests.Model("Phoebe"));
                var member = root.AddComponent<PartyMemberControl>();
                var capsule = root.GetComponent<CharacterController>();
                capsule.enabled = false;
                Vector3 start = new Vector3(40, 0, 40);
                root.transform.SetPositionAndRotation(start, Quaternion.identity);
                capsule.radius = .25f;
                capsule.height = 1.8f;
                capsule.center = Vector3.up * .9f;
                capsule.minMoveDistance = 0;
                capsule.enabled = true;
                wall.SetActive(false);
                wall.transform.localScale = new Vector3(4, 2, .1f);
                Physics.SyncTransforms();
                BindTestActor(root, asset);
                yield return null;
                var model = root.GetComponent<Combatant>().ModelAnimator;
                var controller = Controller(model);
                var modelPosition = model.transform.localPosition;
                var modelRotation = model.transform.localRotation;
                member.SetControlInput(Vector3.forward, false, CharacterCommand.Dodge);
                member.Step(0);
                Assert.That(controller.GetNextAnimatorStateInfo(0).IsName("Base Layer.Dodge"), Is.True);
                member.Step(.1);
                var dodgeState = controller.GetCurrentAnimatorStateInfo(0);
                Assert.That(dodgeState.IsName("Base Layer.Dodge"), Is.True);
                double firstTime = (double)dodgeState.normalizedTime * asset.Duration;
                Assert.That(firstTime, Is.EqualTo(.1).Within(1e-6));
                Vector3 firstDisplacement = Vector3.ProjectOnPlane(root.transform.position - start, Vector3.up);
                Assert.That(firstDisplacement.z, Is.GreaterThan(0f).And.LessThan(reference[3].position.z));

                Vector3 beforeWall = root.transform.position;
                wall.transform.position = beforeWall + new Vector3(0, 1, .36f);
                wall.SetActive(true);
                Physics.SyncTransforms();
                member.Step(asset.FrameTime(4) - asset.FrameTime(3));
                Assert.That(controller.GetCurrentAnimatorStateInfo(0).normalizedTime * asset.Duration,
                    Is.EqualTo(asset.FrameTime(4)).Within(1e-6));
                float blockedDistance = root.transform.position.z - beforeWall.z;
                Assert.That(blockedDistance, Is.InRange(0f, .24f));
                Assert.That(blockedDistance, Is.LessThan(reference[4].position.z - reference[3].position.z));
                wall.SetActive(false);
                Physics.SyncTransforms();
                Vector3 before = root.transform.position;
                Vector3 expected = reference[6].position - reference[4].position;
                AnimationNotice notice = default;
                double timeAtNotice = -1;
                using var subscription = member.Notifications.Subscribe(n =>
                {
                    notice = n;
                    timeAtNotice = controller.GetCurrentAnimatorStateInfo(0).normalizedTime * asset.Duration;
                    member.Notifications.End(n.PlayId, NotifyEndReason.Interrupted);
                });
                member.Step(.7);
                Assert.That(timeAtNotice, Is.EqualTo(asset.FrameTime(6)).Within(.00001));
                var gait = controller.GetCurrentAnimatorStateInfo(0);
                Assert.That(gait.IsName("Base Layer.Locomotion"), Is.True);
                Assert.That(gait.normalizedTime * gait.length,
                    Is.EqualTo(.7 - (asset.FrameTime(6) - asset.FrameTime(4))).Within(.00001));
                Vector3 remainingDisplacement = Vector3.ProjectOnPlane(root.transform.position - before, Vector3.up);
                Assert.That(Vector3.Distance(remainingDisplacement, expected), Is.LessThan(.01), $"实测 {remainingDisplacement}，参考 {expected}");
                Assert.That(member.CurrentState, Is.SameAs(member.Walk));
                Assert.That(notice.Frame, Is.EqualTo(6));
                Assert.That(controller.GetBool("IsDodging"), Is.False);
                Assert.That(Vector3.Distance(model.transform.localPosition, modelPosition), Is.LessThan(.001f));
                Assert.That(Quaternion.Angle(model.transform.localRotation, modelRotation), Is.LessThan(.1f));

                Vector3 interruptedPosition = root.transform.position;
                member.SetControlInput(Vector3.zero, false, CharacterCommand.Dodge);
                member.Step(.7);
                Assert.That(member.CurrentState, Is.SameAs(member.Idle));
                Assert.That(notice.Frame, Is.Zero);
                Assert.That(root.transform.position, Is.EqualTo(interruptedPosition));
            }
            finally
            {
                Object.Destroy(root);
                Object.Destroy(wall);
                Object.Destroy(asset);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DeathEndsTheClipEvenWhileTheBattleClockIsPaused()
        {
            var driver = GameObject.Find("Combat Session").GetComponent<BattleTimeDriver>();
            driver.enabled = false;
            driver.GetComponent<PlayerController>().enabled = false;
            var member = GameObject.Find("Characters/PlayerRoot").GetComponent<PartyMemberControl>();
            member.SetControlInput(Vector3.zero, false, CharacterCommand.Dodge);
            driver.Clock.Advance(.1);
            var distanceAtDeath = member.transform.position;
            var controller = Controller(member.GetComponent<Combatant>().ModelAnimator);
            Assert.That(member.CurrentState, Is.SameAs(member.Dodge));
            driver.Clock.SetPaused(true);
            member.GetComponent<Endfield.Resources.CharacterResources>().Container.ApplyDamage(100000);
            Assert.That(member.CurrentState, Is.SameAs(member.Idle));
            Assert.That(controller.GetBool("IsDodging"), Is.False);
            Assert.That(controller.GetNextAnimatorStateInfo(0).IsName("Base Layer.Locomotion"), Is.True);
            Assert.That(controller.GetAnimatorTransitionInfo(0).normalizedTime, Is.Zero);
            Assert.That(member.transform.position, Is.EqualTo(distanceAtDeath));
            yield break;
        }

        internal static AnimatorControllerPlayable Controller(Animator model)
        {
            foreach (var graph in UnityEditor.Playables.Utility.GetAllGraphs())
                for (int i = 0; i < graph.GetOutputCount(); i++)
                {
                    var output = graph.GetOutput(i);
                    if (output.IsPlayableOutputOfType<AnimationPlayableOutput>() &&
                        ((AnimationPlayableOutput)output).GetTarget() == model)
                        return (AnimatorControllerPlayable)output.GetSourcePlayable();
                }
            throw new AssertionException($"{model.name} 缺少动画输出");
        }

        private static void BindTestActor(GameObject root, AnimationDefination notifications, RuntimeAnimatorController controller = null)
        {
            var session = GameObject.Find("Combat Session");
            var driver = session.GetComponent<BattleTimeDriver>();
            driver.enabled = false;
            driver.GetComponent<PlayerController>().enabled = false;
            var animation = root.GetComponent<Combatant>().ModelAnimator.gameObject.AddComponent<AnimationSource>();
            using var animationSettings = new SerializedObject(animation);
            animationSettings.FindProperty("locomotionController").objectReferenceValue = controller ? controller : AnimationDefinationTests.Locomotion("Phoebe");
            animationSettings.ApplyModifiedPropertiesWithoutUndo();
            using var memberSettings = new SerializedObject(root.GetComponent<PartyMemberControl>());
            memberSettings.FindProperty("dodgeAnimation").objectReferenceValue = notifications;
            memberSettings.ApplyModifiedPropertiesWithoutUndo();
            var step = root.AddComponent<ActorStepBinding>();
            using var stepSettings = new SerializedObject(step);
            stepSettings.FindProperty("timeDriver").objectReferenceValue = driver;
            stepSettings.ApplyModifiedPropertiesWithoutUndo();
            session.GetComponent<SceneActors>().Register(root.GetComponent<Combatant>());
        }

        [UnityTest]
        public IEnumerator EachMemberSamplesItsOwnClockAndResumesAfterReentering()
        {
            var driver = GameObject.Find("Combat Session").GetComponent<BattleTimeDriver>();
            driver.enabled = false;
            driver.GetComponent<PlayerController>().enabled = false;
            var a = GameObject.Find("Characters/PlayerRoot").GetComponent<PartyMemberControl>();
            var b = GameObject.Find("Characters/SiRoot").GetComponent<PartyMemberControl>();
            var actor = a.GetComponent<Combatant>();
            var otherModel = b.GetComponent<Combatant>().ModelAnimator;
            var clock = driver.Clock;
            a.SetControlInput(Vector3.forward, false, CharacterCommand.Dodge);
            b.SetControlInput(Vector3.right, false, CharacterCommand.Dodge);
            clock.Advance(.1);
            var first = Controller(actor.ModelAnimator);
            var second = Controller(otherModel);
            Assert.That(first.GetBool("IsDodging"), Is.True);
            Assert.That(second.GetCurrentAnimatorClipInfo(0)[0].clip, Is.SameAs(AnimationDefinationTests.Asset("Si").Clip));
            clock.TryRequestHitStop(actor.Runtime.Id, .03);
            clock.Advance(.02);
            clock.Advance(.02);
            Assert.That(first.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(.11).Within(1e-6));
            Assert.That(second.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(.14).Within(1e-6));
            clock.SetPaused(true);
            clock.Advance(.5);
            yield return null;
            Assert.That(first.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(.11).Within(1e-6));
            clock.SetPaused(false);
            clock.SetBattleRate(.5);
            clock.Advance(.04);
            Assert.That(first.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(.13).Within(1e-6));
            clock.SetBattleRate(1);
            var otherPosition = b.transform.position;
            b.gameObject.SetActive(false);
            clock.Advance(.1);
            Assert.That(b.transform.position, Is.EqualTo(otherPosition));
            var position = a.transform.position;
            b.gameObject.SetActive(true);
            yield return null;
            Assert.That(first.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(.23).Within(1e-6));
            Assert.That(second.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(.16).Within(1e-6));
            Assert.That(a.transform.position, Is.EqualTo(position));
            Assert.That(b.transform.position, Is.EqualTo(otherPosition));
            clock.Advance(1);
            Assert.That(first.GetBool("IsDodging"), Is.False);
            Assert.That(second.GetBool("IsDodging"), Is.False);
            Assert.That(a.CurrentState, Is.SameAs(a.Walk));
            Assert.That(a.GroundSpeed, Is.Zero);
        }
    }
}
