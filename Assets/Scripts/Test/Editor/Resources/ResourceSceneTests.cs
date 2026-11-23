using System.Collections;
using System.Linq;
using Endfield.Stats;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Endfield.Resources.Tests
{
    public sealed class ResourceSceneTests
    {
        private GameObject hud;
        private CharacterResources player;
        private Scene scene;

        [UnitySetUp]
        public IEnumerator LoadCombatDemo()
        {
            yield return new EnterPlayMode();
            const string path = "Assets/Scenes/CombatDemo.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            hud = roots.Single(obj => obj.name == "GameUI").transform.Find("HealthHUD").gameObject;
            player = roots.Single(obj => obj.name == "Characters").transform.Find("PlayerRoot").GetComponent<CharacterResources>();
        }

        [UnityTearDown]
        public IEnumerator LeaveCombatDemo()
        {
            yield return new ExitPlayMode();
        }

        private void CheckHud(string expectedText, float fraction)
        {
            Assert.That(hud.transform.Find("HPText").GetComponent<TMP_Text>().text, Is.EqualTo(expectedText));
            Assert.That(hud.transform.Find("Track/Fill").GetComponent<Image>().fillAmount, Is.EqualTo(fraction).Within(0.00001f));
        }

        [UnityTest]
        public IEnumerator CombatHudTracksDamageEquipmentAndHealingWhileHidden()
        {
            var team = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TeamSkillPoints>(true)).Single();
            Assert.That((team.Pool.Snapshot.CurrentSP, team.Pool.Snapshot.MaxSP), Is.EqualTo((60d, 100d)));
            CheckHud("150 / 150", 1);
            player.Container.ApplyDamage(40);
            CheckHud("110 / 150", 110f / 150);
            var stats = player.GetComponent<CharacterStats>().Stats;
            Assert.That(stats.ReplaceSource(new SourceID("Equipment", 17),
                new StatModifier(StatId.STR, ModifierOperation.Flat, 3),
                new StatModifier(StatId.WeaponATK, ModifierOperation.Flat, 3)).Success, Is.True);
            CheckHud("110 / 165", 110f / 165);
            var panelValue = hud.transform.parent.Find("CharacterPanelRoot/Panel/BaseStats/MaxHPUnit/Value");
            Assert.That(panelValue.GetComponent<TMP_Text>().text, Is.EqualTo("165"));

            hud.SetActive(false);
            player.Container.RestoreHP(42);
            CheckHud("110 / 165", 110f / 165);
            hud.SetActive(true);
            CheckHud("152 / 165", 152f / 165);
            Assert.That(stats.ReplaceSource(new SourceID("Effect", 2),
                new StatModifier(StatId.MaxHP, ModifierOperation.Override, 0)).Success, Is.True);
            CheckHud("0 / 0", 0);
            yield break;
        }

        [UnityTest]
        public IEnumerator RebindingAndCharacterLifetimeKeepResourcesWithTheirOwner()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Si.prefab");
            var si = Object.Instantiate(prefab).GetComponent<CharacterResources>();
            hud.SendMessage("Bind", si);
            CheckHud("180 / 180", 1);
            player.Container.ApplyDamage(40);
            CheckHud("180 / 180", 1);
            hud.SendMessage("Bind", player);
            CheckHud("110 / 150", 110f / 150);

            var oldStats = si.GetComponent<CharacterStats>().Stats;
            var previousContainer = si.Container;
            si.Container.ApplyDamage(25);
            si.gameObject.SetActive(false);
            Assert.That(oldStats.SetBase(StatId.STR, 17).Success, Is.True);
            si.gameObject.SetActive(true);
            Assert.That(si.Container, Is.SameAs(previousContainer));
            Assert.That((si.Container.Snapshot.CurrentHP, si.Container.Snapshot.MaxHP), Is.EqualTo((155d, 185d)));
            Assert.That((player.Container.Snapshot.UltimateEnergy, si.Container.Snapshot.UltimateEnergy), Is.EqualTo((90d, 30d)));

            Object.Destroy(si.gameObject);
            yield return null;
            Assert.That(oldStats.SetBase(StatId.STR, 18).Success, Is.True);
            Assert.That(previousContainer.Snapshot.MaxHP, Is.EqualTo(185));
            si = Object.Instantiate(prefab).GetComponent<CharacterResources>();
            hud.SendMessage("Bind", si);
            CheckHud("180 / 180", 1);
        }
    }
}
