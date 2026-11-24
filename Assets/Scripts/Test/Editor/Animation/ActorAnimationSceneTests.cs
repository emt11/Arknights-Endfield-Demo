using System.Collections;
using Endfield.Actors;
using Endfield.CombatSession;
using Endfield.Timing;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Endfield.Animation.Tests
{
    public sealed class ActorAnimationSceneTests
    {
        [UnityTest]
        public IEnumerator CombatDemoBindsIndependentTimeAndResumesTheSameUnit()
        {
            yield return new EnterPlayMode();
            const string path = "Assets/Scenes/CombatDemo.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var driver = GameObject.Find("Combat Session").GetComponent<BattleTimeDriver>();
            driver.enabled = false;
            var clock = driver.Clock;
            var player = GameObject.Find("Characters/PlayerRoot").GetComponent<Combatant>();
            var si = GameObject.Find("Characters/SiRoot").GetComponent<Combatant>();
            var a = player.GetComponent<ActorAnimationBinding>().Driver;
            var b = si.GetComponent<ActorAnimationBinding>().Driver;
            a.TryPlay(CharacterAnimationGraphTests.Clip("GS", "Attack1"), 1, 0, .08f);
            b.TryPlay(CharacterAnimationGraphTests.Clip("DK", "Attack1"), 1, 0, .08f);
            clock.Advance(.22);
            var arm = player.ModelAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var otherArm = si.ModelAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var frozen = arm.localRotation;
            var moving = otherArm.localRotation;
            clock.TryRequestHitStop(player.Runtime.Id, .030);
            clock.Advance(.020);
            Assert.That(Quaternion.Angle(frozen, arm.localRotation), Is.LessThan(.05f));
            Assert.That(Quaternion.Angle(moving, otherArm.localRotation), Is.GreaterThan(.05f));
            clock.Advance(.020);
            Assert.That(a.CurrentClipTime, Is.EqualTo(.230).Within(1e-5));
            Assert.That(b.CurrentClipTime, Is.EqualTo(.260).Within(1e-5));
            clock.SetPaused(true);
            clock.Advance(.2);
            yield return null;
            Assert.That(b.CurrentClipTime, Is.EqualTo(.260).Within(1e-5));
            clock.SetPaused(false);
            clock.SetBattleRate(.5);
            clock.Advance(.04);
            Assert.That(a.CurrentClipTime, Is.EqualTo(.250).Within(1e-5));
            var id = si.Runtime.Id;
            var beforeDisable = otherArm.localRotation;
            si.gameObject.SetActive(false);
            clock.Advance(.4);
            si.gameObject.SetActive(true);
            Assert.That(si.Runtime.Id, Is.EqualTo(id));
            Assert.That(Quaternion.Angle(beforeDisable, otherArm.localRotation), Is.LessThan(.5f));
            clock.Advance(.04);
            Assert.That(b.CurrentClipTime, Is.EqualTo(.300).Within(1e-5));
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            Assert.That(clock.TryGetActionTime(id, out _), Is.False);
            Assert.That(GameObject.Find("Characters/SiRoot").GetComponent<ActorAnimationBinding>().Driver.CurrentClip, Is.Null);
        }

        [UnityTearDown]
        public IEnumerator Leave() { yield return new ExitPlayMode(); }
    }
}
