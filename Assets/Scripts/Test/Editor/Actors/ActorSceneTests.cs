using System;
using System.Collections;
using Endfield.CharacterBuild;
using Endfield.CombatContracts;
using Endfield.Effects;
using Endfield.Resources;
using Endfield.Stats;
using Endfield.Status;
using Endfield.Timing;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Endfield.Actors.Tests
{
    public sealed class ActorSceneTests
    {
        private const string ScenePath = "Assets/Scenes/CombatDemo.unity";
        private SceneActors actors;
        private BattleTimeDriver driver;
        private Combatant phoebe;
        private Combatant si;

        [UnitySetUp]
        public IEnumerator LoadActors()
        {
            yield return new EnterPlayMode();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            actors = GameObject.Find("Combat Session").GetComponent<SceneActors>();
            driver = actors.GetComponent<BattleTimeDriver>();
            phoebe = GameObject.Find("Characters/PlayerRoot").GetComponent<Combatant>();
            si = GameObject.Find("Characters/SiRoot").GetComponent<Combatant>();
        }

        [UnityTearDown]
        public IEnumerator LeaveGame()
        {
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator OpeningRosterBindsEachCharactersModulesAndStartsFreshOnReload()
        {
            var player = phoebe.Runtime;
            Assert.That((phoebe.DisplayName, si.DisplayName), Is.EqualTo(("菲比", "祀")));
            Assert.That((player.Id.Value, si.Runtime.Id.Value), Is.EqualTo((1u, 2u)));
            foreach (var unit in new[] { phoebe, si })
            {
                var runtime = unit.Runtime;
                Assert.That(actors.TryGet(runtime.Id, out var found) && found == unit, Is.True);
                Assert.That(unit.ModelAnimator, Is.SameAs(unit.GetComponentInChildren<Animator>()));
                Assert.That(unit.Faction, Is.EqualTo(Faction.Player));
                Assert.That(runtime.Build, Is.SameAs(unit.GetComponent<CharacterBuildComponent>().State));
                Assert.That(runtime.Stats, Is.SameAs(unit.GetComponent<CharacterStats>().Stats));
                Assert.That(runtime.Resources, Is.SameAs(unit.GetComponent<CharacterResources>().Container));
                Assert.That(runtime.Effects, Is.SameAs(unit.GetComponent<CharacterEffects>().Controller));
                Assert.That(runtime.Status, Is.SameAs(unit.GetComponent<CharacterStatus>().Controller));
                Assert.That(actors.Register(unit), Is.SameAs(runtime));
            }
            var hp = player.Resources.Snapshot;
            var other = si.Runtime.Resources.Snapshot;
            Assert.That((hp.CurrentHP, hp.MaxHP, hp.UltimateEnergy, hp.MaxUltimateEnergy), Is.EqualTo((150d, 150d, 90d, 100d)));
            Assert.That((other.CurrentHP, other.MaxHP, other.UltimateEnergy, other.MaxUltimateEnergy), Is.EqualTo((180d, 180d, 30d, 80d)));
            Assert.That(actors.CanTargetEnemy(player.Id, si.Runtime.Id), Is.False);
            Assert.That(actors.CanTargetEnemy(player.Id, player.Id), Is.False);
            phoebe.GetComponent<CharacterResources>().Container.ApplyDamage(40);
            Assert.That(si.Runtime.Resources.Snapshot.CurrentHP, Is.EqualTo(180));
            var oldClock = driver.Clock;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            Assert.That(player.Life.State, Is.EqualTo(ActorLifeState.Removed));
            Assert.That(oldClock.TryGetActionTime(player.Id, out _), Is.False);
            var reopened = GameObject.Find("Characters/PlayerRoot").GetComponent<Combatant>().Runtime;
            Assert.That((reopened.Id.Value, reopened.Resources.Snapshot.CurrentHP), Is.EqualTo((1u, 150d)));
        }

        [UnityTest]
        public IEnumerator EnemyEligibilityTracksDisableAndDeathWithoutLosingIdentity()
        {
            // 只覆盖本用例中场景实例的阵营
            using var data = new SerializedObject(si);
            data.FindProperty("faction").enumValueIndex = (int)Faction.Enemy;
            data.ApplyModifiedPropertiesWithoutUndo();
            var player = phoebe.Runtime.Id;
            var enemy = si.Runtime.Id;
            Assert.That(actors.CanTargetEnemy(player, enemy) && actors.CanTargetEnemy(enemy, player), Is.True);
            si.gameObject.SetActive(false);
            yield return null;
            Assert.That(actors.TryGet(enemy, out var found) && found == si, Is.True);
            Assert.That(driver.Clock.TryGetActionTime(enemy, out _), Is.True);
            Assert.That(actors.CanTargetEnemy(player, enemy) || actors.CanTargetEnemy(enemy, player), Is.False);
            si.gameObject.SetActive(true);
            Assert.That(actors.CanTargetEnemy(player, enemy), Is.True);
            si.gameObject.SetActive(false);
            si.GetComponent<CharacterResources>().Container.ApplyDamage(180);
            si.gameObject.SetActive(true);
            Assert.That(si.Runtime.Life.State, Is.EqualTo(ActorLifeState.Dead));
            Assert.That(actors.TryGet(enemy, out _), Is.True);
            Assert.That(actors.CanTargetEnemy(player, enemy) || actors.CanTargetEnemy(enemy, player), Is.False);
            Assert.That(phoebe.Runtime.Life.State, Is.EqualTo(ActorLifeState.Alive));
        }

        [UnityTest]
        public IEnumerator IndependentClocksAndReentryReleaseTheOldIdentity()
        {
            driver.enabled = false;
            var clock = driver.Clock;
            var old = si.Runtime;
            var player = phoebe.Runtime.Id;
            Assert.That(clock.TryGetActionTime(player, out var beforePlayer), Is.True);
            Assert.That(clock.TryGetActionTime(old.Id, out var beforeSi), Is.True);
            Assert.That(clock.TryRequestHitStop(player, .030), Is.True);
            clock.Advance(.020);
            clock.Advance(.020);
            clock.TryGetActionTime(player, out var afterPlayer);
            clock.TryGetActionTime(old.Id, out var afterSi);
            Assert.That(afterPlayer.Elapsed - beforePlayer.Elapsed, Is.EqualTo(.010).Within(1e-8));
            Assert.That(afterSi.Elapsed - beforeSi.Elapsed, Is.EqualTo(.040).Within(1e-8));

            Object.Destroy(si.gameObject);
            yield return null;
            Assert.That(actors.TryGet(old.Id, out _), Is.False);
            Assert.That(clock.TryGetActionTime(old.Id, out _), Is.False);
            Assert.That(clock.TryRequestHitStop(old.Id, .030), Is.False);
            Assert.That(old.Life.State, Is.EqualTo(ActorLifeState.Removed));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Si.prefab");
            var replacement = Object.Instantiate(prefab).GetComponent<Combatant>();
            using var data = new SerializedObject(replacement);
            var nameField = data.FindProperty("displayName");
            nameField.stringValue = "";
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.Throws<InvalidOperationException>(() => actors.Register(replacement));
            Assert.That(replacement.Runtime, Is.Null);
            var next = new ActorId(3);
            Assert.That(actors.TryGet(next, out _), Is.False);
            Assert.That(clock.TryGetActionTime(next, out _), Is.False);
            nameField.stringValue = "祀";
            data.ApplyModifiedPropertiesWithoutUndo();
            var runtime = actors.Register(replacement);
            Assert.That(runtime.Id, Is.EqualTo(next));
            Assert.That(runtime.Resources.Snapshot.CurrentHP, Is.EqualTo(180));
            Assert.That(clock.TryGetActionTime(next, out var fresh), Is.True);
            Assert.That(fresh.Elapsed, Is.Zero);
            Assert.That(clock.TryGetActionTime(player, out var retained), Is.True);
            Assert.That(retained.Elapsed, Is.EqualTo(afterPlayer.Elapsed));
        }
    }
}
