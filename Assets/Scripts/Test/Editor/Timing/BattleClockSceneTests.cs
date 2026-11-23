using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Endfield.Actors;
using Endfield.CombatContracts;
using Endfield.Effects;
using Endfield.Resources;
using Endfield.Stats;
using Endfield.Status;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Endfield.Timing.Tests
{
    public sealed class BattleClockSceneTests
    {
        [UnitySetUp]
        public IEnumerator EnterGame()
        {
            yield return new EnterPlayMode();
        }

        [UnityTearDown]
        public IEnumerator LeaveGame()
        {
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator CombatDemoDrivesCharacterEffectsAcrossPauseAndCharacterSwitch()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/CombatDemo.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            var driver = GameObject.Find("Combat Session").GetComponent<BattleTimeDriver>();
            var clock = driver.Clock;
            float timeScale = Time.timeScale;
            yield return null;
            var player = GameObject.Find("Characters/PlayerRoot");
            var actor = player.GetComponent<Combatant>().Runtime.Id;
            var si = GameObject.Find("Characters/SiRoot");
            double sampled = 0;
            using var sample = clock.SubscribeBattleStep(step => sampled = Time.unscaledDeltaTime);
            yield return null;
            Assert.That(sampled, Is.Positive);
            Assert.That(clock.Real.Delta, Is.EqualTo(sampled));
            clock.SetPaused(true);
            yield return null;
            Assert.That(clock.Real.Delta, Is.Positive);
            Assert.That(clock.Battle.Delta, Is.Zero);
            clock.TryGetActionTime(actor, out var paused);
            Assert.That(paused.Delta, Is.Zero);
            Assert.That(Time.timeScale, Is.EqualTo(timeScale));
            clock.SetPaused(false);
            yield return null;
            Assert.That(clock.Battle.Delta, Is.Positive);

            // 保留场景订阅关系，用确定的步长验收角色切换、周期回能与冻结
            driver.enabled = false;
            var effects = si.GetComponent<CharacterEffects>().Controller;
            var stats = si.GetComponent<CharacterStats>().Stats;
            var resources = si.GetComponent<CharacterResources>().Container;
            var status = player.GetComponent<CharacterStatus>().Controller;
            status.ApplyAttachment(AttachmentElement.Heat, 1);
            status.ApplyAttachment(AttachmentElement.Cryo, 1);
            var observed = new List<double>();
            using var resourceChanges = resources.Subscribe((_, next) => observed.Add(next.UltimateEnergy));
            int endings = 0;
            using var ended = effects.SubscribeEnded(_ => endings++);
            var regen = AssetDatabase.LoadAssetAtPath<EffectProfile>("Assets/ScriptObjects/Effects/UltimateRegen.asset");
            Assert.That(effects.TryApply(regen, stats, out var handle, out var error), Is.True, error);
            Assert.That(resources.Snapshot.UltimateEnergy, Is.EqualTo(30));
            si.SetActive(false);
            clock.Advance(.5);
            clock.SetPaused(true);
            clock.Advance(8);
            Assert.That(status.IsActive(StatusType.Frozen), Is.True);
            Assert.That(resources.Snapshot.UltimateEnergy, Is.EqualTo(30));
            clock.SetPaused(false);
            clock.SetBattleRate(.5);
            clock.Advance(5);
            si.SetActive(true);
            clock.Advance(2);
            Assert.That(observed, Is.EqualTo(new[] { 35d, 40d, 45d }));
            Assert.That(status.IsActive(StatusType.Frozen), Is.False);
            Assert.That(effects.IsActive(handle), Is.False);
            Assert.That(endings, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ManualCharacterGraphsAdvanceOnlyByTheirOwnActionTime()
        {
            var clock = new BattleClock();
            var target = new ActorId(1);
            var other = new ActorId(2);
            clock.RegisterActor(target);
            clock.RegisterActor(other);
            var phoebe = new AnimationProbe("Phoebe", "GreatSword", "GS_Attack1");
            var si = new AnimationProbe("Si", "DualKatana", "DK_Attack1");
            try
            {
                var startingPose = phoebe.Pose;
                var otherPose = si.Pose;
                double targetStart = phoebe.Time;
                double otherStart = si.Time;
                clock.TryRequestHitStop(target, .030);
                void EvaluateNextStep()
                {
                    clock.Advance(.020);
                    clock.TryGetActionTime(target, out var victim);
                    clock.TryGetActionTime(other, out var attacker);
                    phoebe.Advance(victim.Delta);
                    si.Advance(attacker.Delta);
                }

                EvaluateNextStep();
                Assert.That(Quaternion.Angle(startingPose, phoebe.Pose), Is.LessThan(.05f));
                float movingAngle = Quaternion.Angle(otherPose, si.Pose);
                Assert.That(movingAngle, Is.GreaterThan(.05f));
                EvaluateNextStep();
                float resumedAngle = Quaternion.Angle(startingPose, phoebe.Pose);
                Assert.That(resumedAngle, Is.GreaterThan(.05f));
                Assert.That(phoebe.Time - targetStart, Is.EqualTo(.010).Within(1e-6));
                Assert.That(si.Time - otherStart, Is.EqualTo(.040).Within(1e-6));
                TestContext.WriteLine($"菲比片段推进 {phoebe.Time - targetStart:F3}s，恢复后骨骼变化 {resumedAngle:F2}°；祀片段推进 {si.Time - otherStart:F3}s，首步骨骼变化 {movingAngle:F2}°");
                double sampledTarget = phoebe.Time;
                double sampledOther = si.Time;
                yield return null;
                Assert.That(phoebe.Time, Is.EqualTo(sampledTarget));
                Assert.That(si.Time, Is.EqualTo(sampledOther));
            }
            finally
            {
                phoebe.Destroy();
                si.Destroy();
            }
        }

        private sealed class AnimationProbe
        {
            private readonly GameObject instance;
            private readonly PlayableGraph graph;
            private readonly AnimationClipPlayable playable;
            private readonly Transform upperArm;

            public Quaternion Pose => upperArm.localRotation;
            public double Time => playable.GetTime();

            public AnimationProbe(string character, string weapon, string clipName)
            {
                var clip = AssetDatabase.LoadAllAssetsAtPath(
                    $"Assets/Game/Animations/Characters/Clips/{weapon}/Normal/{clipName}.fbx")
                    .OfType<AnimationClip>().Single(asset => asset.name == clipName);
                instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Prefabs/Characters/{character}.prefab"));
                var animator = instance.GetComponentInChildren<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                graph = PlayableGraph.Create(character);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimationClipPlayable.Create(graph, clip);
                AnimationPlayableOutput.Create(graph, character, animator).SetSourcePlayable(playable);
                graph.Play();
                playable.SetTime(clip.length * .25);
                graph.Evaluate(0);
            }

            public void Advance(double delta)
            {
                if (delta > 0) graph.Evaluate((float)delta);
            }

            public void Destroy()
            {
                graph.Destroy();
                Object.Destroy(instance);
            }
        }
    }
}
