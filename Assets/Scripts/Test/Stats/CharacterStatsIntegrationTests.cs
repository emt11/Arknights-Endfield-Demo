using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Endfield.Stats.Tests
{
    public sealed class CharacterStatsIntegrationTests
    {
        [Test]
        public void ChangingOneProfileOnlyAffectsItsNextCharacter()
        {
            CharacterStatProfile ProfileOf(string name)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Characters/{name}.prefab");
                using var actor = new SerializedObject(prefab.GetComponent<CharacterStats>());
                return (CharacterStatProfile)actor.FindProperty("profile").objectReferenceValue;
            }

            var original = ProfileOf("Phoebe");
            var other = ProfileOf("Si");
            Assert.That(original, Is.Not.SameAs(other));
            var copy = Object.Instantiate(original);
            var inspector = UnityEditor.Editor.CreateEditor(copy);
            try
            {
                Assert.That(inspector.GetType().Name, Is.EqualTo("CharacterStatProfileEditor"));
                var before = copy.CreateSet();
                double otherHP = other.CreateSet().GetValue(StatId.MaxHP);
                using var settings = new SerializedObject(copy);
                var displayName = settings.FindProperty("displayName");
                displayName.stringValue = "测试干员";
                var values = settings.FindProperty("initialValues");
                var strength = Enumerable.Range(0, values.arraySize).Select(values.GetArrayElementAtIndex)
                    .Single(entry => entry.FindPropertyRelative("id").intValue == (int)StatId.STR);
                strength.FindPropertyRelative("value").doubleValue = 12;
                var critDamage = Enumerable.Range(0, values.arraySize).Select(values.GetArrayElementAtIndex)
                    .SingleOrDefault(entry => entry.FindPropertyRelative("id").intValue == (int)StatId.CritDamage);
                if (critDamage == null)
                {
                    values.arraySize++;
                    critDamage = values.GetArrayElementAtIndex(values.arraySize - 1);
                    critDamage.FindPropertyRelative("id").intValue = (int)StatId.CritDamage;
                }
                critDamage.FindPropertyRelative("value").doubleValue = 1.75;
                settings.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(copy.DisplayName, Is.EqualTo("测试干员"));
                displayName.stringValue = "";
                settings.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(copy.DisplayName, Is.EqualTo(copy.name));
                var updated = copy.CreateSet();
                Assert.That(updated.GetValue(StatId.MaxHP), Is.EqualTo(160));
                Assert.That(updated.GetValue(StatId.CritDamage), Is.EqualTo(1.75));
                Assert.That(before.GetValue(StatId.CritDamage), Is.EqualTo(1.5));
                Assert.That(before.GetValue(StatId.MaxHP), Is.EqualTo(150));
                Assert.That(other.CreateSet().GetValue(StatId.MaxHP), Is.EqualTo(otherHP));
            }
            finally
            {
                Object.DestroyImmediate(inspector);
                Object.DestroyImmediate(copy);
            }
        }

        [UnityTest]
        public IEnumerator CombatPanelShowsSevenIconsAndTracksThePlayersFinalValues()
        {
            yield return new EnterPlayMode();
            const string path = "Assets/Scenes/CombatDemo.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            var scene = SceneManager.GetSceneByPath(path);
            var ui = scene.GetRootGameObjects().Single(obj => obj.name == "GameUI").transform;
            var panel = ui.Find("CharacterPanelRoot/Panel");
            var player = scene.GetRootGameObjects().Single(obj => obj.name == "Characters").transform.Find("PlayerRoot");
            var stats = player.GetComponent<CharacterStats>().Stats;
            var summary = new[]
            {
                (StatId.STR, "PrimaryAttributes/STR"), (StatId.AGI, "PrimaryAttributes/AGI"),
                (StatId.INT, "PrimaryAttributes/INT"), (StatId.WIL, "PrimaryAttributes/WIL"),
                (StatId.MaxHP, "BaseStats/MaxHPUnit"), (StatId.ATK, "BaseStats/AttackUnit"),
                (StatId.DEF, "BaseStats/DefenseUnit")
            };
            var icons = new System.Collections.Generic.HashSet<Sprite>();
            Assert.That(panel.gameObject.activeInHierarchy, Is.True);
            Canvas.ForceUpdateCanvases();
            var playerName = panel.Find("CharacterName").GetComponent<TMP_Text>();
            playerName.ForceMeshUpdate();
            Assert.That(playerName.GetParsedText(), Is.EqualTo(player.GetComponent<CharacterStats>().DisplayName));
            Assert.That(playerName.font.HasCharacters(playerName.text), Is.True);
            foreach (var (id, cellPath) in summary)
            {
                var cell = panel.Find(cellPath);
                var badge = cell.Find("Badge");
                var icon = (badge ? badge : cell).Find("Icon").GetComponent<Image>();
                Assert.That(icon.sprite && icons.Add(icon.sprite), Is.True, cellPath);
                var value = cell.Find("Value").GetComponent<TMP_Text>();
                Assert.That(value.text, Is.EqualTo(stats.GetValue(id).ToString("0.##")));
                value.ForceMeshUpdate();
                Assert.That(value.textInfo.meshInfo[0].vertexCount, Is.Positive, cellPath);
                if (badge)
                {
                    var label = cell.Find("Label").GetComponent<TMP_Text>();
                    Assert.That(label.GetParsedText(), Is.EqualTo(StatCatalog.Get(id).DisplayName));
                    Assert.That(badge.Find("Main").gameObject.activeSelf, Is.EqualTo(id == stats.MainAttribute));
                }
            }

            var groups = panel.Find("Viewport/Groups");
            foreach (StatId id in System.Enum.GetValues(typeof(StatId)))
            {
                var definition = StatCatalog.Get(id);
                var row = groups.Find($"{definition.Group}/{id}");
                if (summary.Any(item => item.Item1 == id))
                    Assert.That(row, Is.Null);
                else
                {
                    string value = stats.GetValue(id).ToString(definition.Unit == StatUnit.Ratio ? "0.##%" : "0.##");
                    Assert.That(row.GetComponent<TMP_Text>().text, Is.EqualTo($"{definition.DisplayName}<pos=64%>{value}"));
                }
            }

            var scroll = groups.GetComponentInParent<ScrollRect>();
            scroll.verticalNormalizedPosition = 0;
            Canvas.ForceUpdateCanvases();
            var lastRow = (RectTransform)groups.Find("Special/HealingReceivedBonus");
            var viewport = (RectTransform)scroll.transform;
            Assert.That(viewport.rect.Contains(viewport.InverseTransformPoint(lastRow.TransformPoint(lastRow.rect.center))), Is.True);
            scroll.verticalNormalizedPosition = 1;

            var hp = panel.Find("BaseStats/MaxHPUnit/Value").GetComponent<TMP_Text>();
            double strength = stats.GetValue(StatId.STR);
            Assert.That(stats.SetBase(StatId.STR, strength + 1).Success, Is.True);
            Assert.That(hp.text, Is.EqualTo(stats.GetValue(StatId.MaxHP).ToString("0.##")));
            panel.Find("CloseBTN").GetComponent<Button>().onClick.Invoke();
            Assert.That(panel.gameObject.activeInHierarchy, Is.False);
            Assert.That(stats.SetBase(StatId.STR, strength).Success, Is.True);
            ui.Find("Open").GetComponent<Button>().onClick.Invoke();
            Assert.That(panel.gameObject.activeInHierarchy, Is.True);
            Assert.That(hp.text, Is.EqualTo(stats.GetValue(StatId.MaxHP).ToString("0.##")));

            yield return SceneManager.UnloadSceneAsync(scene);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator StatsPageReadsFinalValuesAcrossEquipSwitchAndReopen()
        {
            yield return new EnterPlayMode();
            const string path = "Assets/Scenes/StatsDemo.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            var scene = SceneManager.GetSceneByPath(path);
            var ui = scene.GetRootGameObjects().Single(obj => obj.name == "Stats UI");
            var buttons = ui.GetComponentsInChildren<Button>(true).ToDictionary(button => button.name);
            var actors = scene.GetRootGameObjects().Single(obj => obj.name == "Characters").transform;
            var character = actors.Find("Phoebe").GetComponent<CharacterStats>();
            var other = actors.Find("Si").GetComponent<CharacterStats>();

            Assert.That(buttons["Open"].GetComponentInChildren<TMP_Text>().text, Is.EqualTo("角色属性"));
            buttons["Wooden Sword"].onClick.Invoke();
            Assert.That(buttons["Wooden Sword"].GetComponentInChildren<TMP_Text>().text, Is.EqualTo(character.DisplayName + " · 取下木剑"));
            buttons["Open"].onClick.Invoke();
            var groups = ui.transform.Find("Panel/Viewport/Groups");
            Assert.That(groups.childCount, Is.EqualTo(StatCatalog.Groups.Count(group => StatCatalog.GetByGroup(group).Any())));
            TMP_Text Row(string name) => groups.Find(name).GetComponent<TMP_Text>();
            string Display(TMP_Text row)
            {
                Canvas.ForceUpdateCanvases();
                row.ForceMeshUpdate();
                return row.GetParsedText();
            }

            var nameLabel = ui.transform.Find("Panel/Character").GetComponent<TMP_Text>();
            Assert.That(Display(nameLabel), Is.EqualTo(character.DisplayName));
            Assert.That(Display(Row("PrimaryAttributes/Group Name")), Is.EqualTo("基础属性"));
            var strength = Row("PrimaryAttributes/STR");
            var attack = Row("BaseCombat/WeaponATK");
            var maxHP = Row("DerivedCombat/MaxHP");
            var crit = Row("DerivedCombat/CritRate");
            var critDamage = Row("DerivedCombat/CritDamage");
            Assert.That(Display(strength), Is.EqualTo("力量13"));
            Assert.That(Display(attack), Is.EqualTo("武器攻击203"));
            Assert.That(Display(maxHP), Is.EqualTo("最大生命165"));
            string expectedCrit = "暴击率" + character.Stats.GetValue(StatId.CritRate).ToString("0.##%");
            Assert.That(Display(crit), Is.EqualTo(expectedCrit));
            Assert.That(Display(critDamage), Is.EqualTo("暴击伤害150%"));
            var equipment = new SourceID("Equipment", 18);
            Assert.That(character.Stats.ReplaceSource(equipment,
                new StatModifier(StatId.CritDamage, ModifierOperation.Flat, 0.2)).Success, Is.True);
            Assert.That(Display(critDamage), Is.EqualTo("暴击伤害170%"));
            Assert.That(Display(crit), Is.EqualTo(expectedCrit));
            var physical = Row("Resistance/PhysicalResistance");
            var arts = Row("Resistance/ArtsResistance");
            var healing = Row("Special/HealingReceivedBonus");
            groups.GetComponentInParent<ScrollRect>().verticalNormalizedPosition = 0;
            Assert.That(Display(physical), Is.EqualTo("物理抗性0"));
            Assert.That(Display(arts), Is.EqualTo("法术抗性9.09"));
            Assert.That(Display(healing), Is.EqualTo("受治疗效率5%"));
            Assert.That(character.Stats.SetBase(StatId.WIL, 60).Success, Is.True);
            Assert.That(Display(healing), Is.EqualTo("受治疗效率6%"));
            buttons["Wooden Sword"].onClick.Invoke();
            Assert.That(Display(strength), Is.EqualTo("力量10"));
            Assert.That(Display(attack), Is.EqualTo("武器攻击200"));
            Assert.That(Display(maxHP), Is.EqualTo("最大生命150"));

            buttons["Switch"].onClick.Invoke();
            Assert.That(Display(nameLabel), Is.EqualTo(other.DisplayName));
            Assert.That(nameLabel.font.HasCharacters(other.DisplayName), Is.True);
            Assert.That(Display(critDamage), Is.EqualTo("暴击伤害150%"));
            Assert.That(Display(physical), Is.EqualTo("物理抗性1.96"));
            Assert.That(Display(arts), Is.EqualTo("法术抗性0.99"));
            Assert.That(Display(healing), Is.EqualTo("受治疗效率1.2%"));
            string otherDisplay = Display(strength);
            Assert.That(otherDisplay, Is.EqualTo("力量" + other.Stats.GetValue(StatId.STR)));
            Assert.That(character.Stats.SetBase(StatId.STR, 12).Success, Is.True);
            Assert.That(Display(strength), Is.EqualTo(otherDisplay));
            buttons["Close"].onClick.Invoke();
            Assert.That(groups.gameObject.activeInHierarchy, Is.False);
            string closed = strength.text;
            Assert.That(other.Stats.SetBase(StatId.STR, 25).Success, Is.True);
            Assert.That(strength.text, Is.EqualTo(closed));
            buttons["Open"].onClick.Invoke();
            Assert.That(Display(strength), Is.EqualTo("力量25"));

            yield return SceneManager.UnloadSceneAsync(scene);
            yield return new ExitPlayMode();
        }
    }
}
