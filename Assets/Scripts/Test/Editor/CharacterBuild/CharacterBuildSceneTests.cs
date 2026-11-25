using System.Collections;
using Endfield.Resources;
using Endfield.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Endfield.CharacterBuild.Tests
{
    public sealed class CharacterBuildSceneTests
    {
        private const string BuildFolder = "Assets/ScriptObjects/CharacterBuild/";

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
        public IEnumerator StartingWeaponAppliesBeforeHealthAndStaysWithItsCharacter()
        {
            var phoebe = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Phoebe.prefab");
            var si = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Si.prefab");
            var sword = AssetDatabase.LoadAssetAtPath<BuildModifierProfile>(BuildFolder + "WoodenSword.asset");
            var configured = Object.Instantiate(AssetDatabase.LoadAssetAtPath<CharacterBuildProfile>(BuildFolder + "Phoebe.asset"));
            var parent = new GameObject("构筑测试角色");
            parent.SetActive(false);
            try
            {
                var profileData = new SerializedObject(configured);
                profileData.FindProperty("initialWeapon").objectReferenceValue = sword;
                profileData.ApplyModifiedPropertiesWithoutUndo();
                var equipped = Object.Instantiate(phoebe, parent.transform).GetComponent<CharacterBuildComponent>();
                var componentData = new SerializedObject(equipped);
                componentData.FindProperty("profile").objectReferenceValue = configured;
                componentData.ApplyModifiedPropertiesWithoutUndo();
                var unarmed = Object.Instantiate(phoebe, parent.transform).GetComponent<CharacterResources>();
                var teammate = Object.Instantiate(si, parent.transform).GetComponent<CharacterResources>();

                parent.SetActive(true);
                var resources = equipped.GetComponent<CharacterResources>().Container;
                Assert.That((resources.Snapshot.CurrentHP, resources.Snapshot.MaxHP), Is.EqualTo((165d, 165d)));
                Assert.That(unarmed.Container.Snapshot.CurrentHP, Is.EqualTo(150));
                Assert.That(teammate.Container.Snapshot.CurrentHP, Is.EqualTo(180));
                Assert.That(unarmed.GetComponent<CharacterBuildComponent>().State.GetEquipped(BuildSlot.Weapon), Is.Null);
                Assert.That(teammate.GetComponent<CharacterBuildComponent>().State.GetEquipped(BuildSlot.Weapon), Is.Null);
                resources.ApplyDamage(40);
                equipped.gameObject.SetActive(false);
                equipped.gameObject.SetActive(true);
                Assert.That(equipped.State.GetEquipped(BuildSlot.Weapon), Is.SameAs(sword));
                resources = equipped.GetComponent<CharacterResources>().Container;
                Assert.That((resources.Snapshot.CurrentHP, resources.Snapshot.MaxHP), Is.EqualTo((125d, 165d)));
            }
            finally
            {
                Object.Destroy(parent);
                Object.Destroy(configured);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator CombatDemoEquipmentUpdatesHealthBarAndStatPanel()
        {
            const string path = "Assets/Scenes/CombatDemo.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            var player = GameObject.Find("Characters/PlayerRoot");
            var ui = GameObject.Find("GameUI").transform;
            var panelRoot = ui.Find("CharacterPanelRoot");
            panelRoot.GetComponent<CharacterStatsPanel>().Open();
            var healthText = ui.Find("HealthHUD/HPText").GetComponent<TMP_Text>();
            var fill = ui.Find("HealthHUD/Track/Fill").GetComponent<Image>();
            var panel = panelRoot.Find("PanelView/Panel/BaseStats");
            var build = player.GetComponent<CharacterBuildComponent>().State;
            Assert.That(healthText.text, Is.EqualTo("150 / 150"));
            player.GetComponent<CharacterResources>().Container.ApplyDamage(40);
            Assert.That(healthText.text, Is.EqualTo("110 / 150"));
            var sword = AssetDatabase.LoadAssetAtPath<BuildModifierProfile>(BuildFolder + "WoodenSword.asset");
            Assert.That(build.TryEquip(BuildSlot.Weapon, sword, out var error), Is.True, error);
            Assert.That(healthText.text, Is.EqualTo("110 / 165"));
            Assert.That(fill.fillAmount, Is.EqualTo(110f / 165).Within(0.00001f));
            Assert.That(panel.Find("MaxHPUnit/Value").GetComponent<TMP_Text>().text, Is.EqualTo("165"));
            Assert.That(panel.Find("AttackUnit/Value").GetComponent<TMP_Text>().text, Is.EqualTo("710.24"));
        }
    }
}
