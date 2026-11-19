using System;
using System.IO;
using System.Linq;
using Endfield.Character;
using Endfield.Combat;
using Endfield.Controller;
using Endfield.Presentation;
using Endfield.Validation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Endfield.EditorValidation
{
    [InitializeOnLoad]
    public static class CombatEntryBatch
    {
        private const string RunningKey = "Endfield.EntryProbe.Running";
        private static double startedAt;

        public static void RunActions()
        {
            SessionState.SetBool("Endfield.Probe.Actions", true);
            Run();
        }

        public static void RunHits()
        {
            SessionState.SetBool("Endfield.Probe.Hits", true);
            RunActions();
        }

        static CombatEntryBatch()
        {
            if (SessionState.GetBool(RunningKey, false))
            {
                startedAt = EditorApplication.timeSinceStartup;
                EditorApplication.update += Poll;
            }
        }

        public static void Run()
        {
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/CombatDemo.unity");
                Directory.CreateDirectory("Assets/ScriptObjects/Combat/Rules");
                const string rulesPath = "Assets/ScriptObjects/Combat/Rules/CombatRulesDefinition.asset";
                CombatRulesDefinition rules = AssetDatabase.LoadAssetAtPath<CombatRulesDefinition>(rulesPath);
                if (rules == null)
                {
                    rules = ScriptableObject.CreateInstance<CombatRulesDefinition>();
                    AssetDatabase.CreateAsset(rules, rulesPath);
                }

                PlayerController player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
                GameInputManager input = UnityEngine.Object.FindFirstObjectByType<GameInputManager>();
                CombatSimulation simulation = UnityEngine.Object.FindFirstObjectByType<CombatSimulation>();
                if (simulation == null)
                    simulation = new GameObject("CombatSimulation").AddComponent<CombatSimulation>();

                Assign(simulation, "rules", rules);
                Assign(simulation, "input", input);
                Assign(simulation, "player", player);
                Assign(player, "simulation", simulation);
                if (SessionState.GetBool("Endfield.Probe.Actions", false))
                    ConfigureActions(player, simulation);

                if (SessionState.GetBool("Endfield.Probe.Hits", false))
                {
                    ConfigureHits(rules);
                    RegisterSceneUnits(simulation);
                }

                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                AssetDatabase.SaveAssets();
                bool actionProbe = SessionState.GetBool("Endfield.Probe.Actions", false);
                bool hitProbe = SessionState.GetBool("Endfield.Probe.Hits", false);
                if (hitProbe)
                    new GameObject("P1-03 validation").AddComponent<CombatHitProbe>();
                else if (actionProbe)
                    new GameObject("P1-02 validation").AddComponent<CombatActionProbe>();
                else
                    new GameObject("P1-01 validation").AddComponent<CombatEntryProbe>();

                string sceneName = hitProbe ? "CombatHitValidation" : actionProbe ? "CombatActionValidation" : "CombatEntryValidation";
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/Tests/Scenes/" + sceneName + ".unity", true);
                SessionState.SetBool(RunningKey, true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private static void ConfigureActions(PlayerController player, CombatSimulation simulation)
        {
            Directory.CreateDirectory("Assets/ScriptObjects/Combat/Actions");
            const string actionPath = "Assets/ScriptObjects/Combat/Actions/GreatSwordAttack1.asset";
            ActionDefinition action = AssetDatabase.LoadAssetAtPath<ActionDefinition>(actionPath);
            if (action == null)
            {
                action = ScriptableObject.CreateInstance<ActionDefinition>();
                action.clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Animations/Characters/Clips/GreatSword/Normal/GS_Attack1.fbx")
                    .OfType<AnimationClip>().First(clip => !clip.name.StartsWith("__preview__"));
                action.clipEnd = action.clip.length;
                action.cancelWindows = new[] { new CancelWindow { start = action.Duration * 0.6f, end = action.Duration, categories = ActionCategory.Attack | ActionCategory.Move } };
                action.points = new[]
                {
                    new ComboAdvancePoint { time = 0f },
                    new ComboAdvancePoint { time = action.Duration * 0.5f },
                    new ComboAdvancePoint { time = action.Duration }
                };
                AssetDatabase.CreateAsset(action, actionPath);
            }

            // 原 Prefab 的移动参考速度为 1.31/5.29，迁移时保留配置值
            const string playerPath = "Assets/Game/Prefabs/Characters/Player.prefab";
            GameObject prefab = PrefabUtility.LoadPrefabContents(playerPath);
            try
            {
                ConfigureCharacter(prefab, action);
                PrefabUtility.SaveAsPrefabAsset(prefab, playerPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }

            ConfigureCharacter(player.Character.gameObject, action);
            Assign(simulation, "feedback", UnityEngine.Object.FindFirstObjectByType<CombatFeedbackPlayer>());
            RegisterSceneUnits(simulation);
        }

        private static void RegisterSceneUnits(CombatSimulation simulation)
        {
            Combatant[] units = UnityEngine.Object.FindObjectsByType<Combatant>(FindObjectsSortMode.InstanceID);
            var serialized = new SerializedObject(simulation);
            SerializedProperty array = serialized.FindProperty("units");
            array.arraySize = units.Length;
            for (int index = 0; index < units.Length; index++)
                array.GetArrayElementAtIndex(index).objectReferenceValue = units[index];

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureCharacter(GameObject target, ActionDefinition action)
        {
            AnimationPlayer animation = target.GetComponent<AnimationPlayer>();
            if (animation == null)
                animation = target.AddComponent<AnimationPlayer>();

            Animator animator = target.GetComponentInChildren<Animator>(true);
            Assign(animation, "animator", animator);
            Assign(animation, "locomotionController", AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Game/Animations/Characters/Controllers/Phoebe_Controller.controller"));
            var serialized = new SerializedObject(animation);
            serialized.FindProperty("walkReferenceSpeed").floatValue = 1.31f;
            serialized.FindProperty("runReferenceSpeed").floatValue = 5.29f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ActionController actions = target.GetComponent<ActionController>();
            if (actions == null)
                actions = target.AddComponent<ActionController>();

            var character = new SerializedObject(target.GetComponent<Combatant>());
            CharacterDefinition definition = (CharacterDefinition)character.FindProperty("characterAsset").objectReferenceValue;
            var characterData = new SerializedObject(definition);
            SerializedProperty attacks = characterData.FindProperty("basicAttacks");
            if (attacks.arraySize == 0)
            {
                attacks.arraySize = 1;
                attacks.GetArrayElementAtIndex(0).objectReferenceValue = action;
                characterData.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void ConfigureHits(CombatRulesDefinition rules)
        {
            if (!UnityEngine.Object.FindObjectsByType<Combatant>(FindObjectsSortMode.None).Any(unit => unit.Team != 0))
            {
                GameObject target = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/TrainingDummy.prefab"));
                PlayerController player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
                Transform character = player.Character.transform;
                target.transform.position = character.position + character.forward * 2.5f;
            }

            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tags.FindProperty("layers");
            layers.GetArrayElementAtIndex(10).stringValue = "HitTarget";
            tags.ApplyModifiedPropertiesWithoutUndo();
            rules.targetMask = 1 << 10;
            rules.worldMask = LayerMask.GetMask("Default", "Environment");
            Physics.IgnoreLayerCollision(9, 9, true);
            Physics.IgnoreLayerCollision(9, 10, true);
            EditorUtility.SetDirty(rules);
            ActionDefinition action = AssetDatabase.LoadAssetAtPath<ActionDefinition>("Assets/ScriptObjects/Combat/Actions/GreatSwordAttack1.asset");
            action.hitWindows = new[] { new HitWindow { start = 38f / 60f, end = 47f / 60f, group = 0, damageMultiplier = 0.8f, hitboxIds = new[] { 0 } } };
            EditorUtility.SetDirty(action);
            foreach (string path in new[] { "Assets/Game/Prefabs/Characters/Player.prefab", "Assets/Game/Prefabs/Characters/TrainingDummy.prefab" })
            {
                GameObject prefab = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    ConfigureTargetColliders(prefab, path.Contains("TrainingDummy") ? 1 : 0);
                    PrefabUtility.SaveAsPrefabAsset(prefab, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefab);
                }
            }

            foreach (Combatant unit in UnityEngine.Object.FindObjectsByType<Combatant>(FindObjectsSortMode.InstanceID))
                ConfigureTargetColliders(unit.gameObject, unit.Team);
        }

        private static void ConfigureTargetColliders(GameObject target, int team)
        {
            var serialized = new SerializedObject(target.GetComponent<Combatant>());
            serialized.FindProperty("team").intValue = team;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            foreach (Collider body in colliders)
            {
                if (body.gameObject.layer != 10)
                    body.gameObject.layer = 9;
            }

            if (colliders.Any(collider => collider.gameObject.layer == 10))
                return;

            GameObject bodyBox = new GameObject("Body Collider");
            bodyBox.transform.SetParent(target.transform, false);
            bodyBox.layer = 10;
            CapsuleCollider capsule = bodyBox.AddComponent<CapsuleCollider>();
            capsule.center = Vector3.up;
            capsule.height = 1.8f;
            capsule.radius = 0.45f;
            capsule.isTrigger = true;
            GameObject headBox = new GameObject("Head Collider");
            headBox.transform.SetParent(target.transform, false);
            headBox.layer = 10;
            SphereCollider sphere = headBox.AddComponent<SphereCollider>();
            sphere.center = Vector3.up * 1.65f;
            sphere.radius = 0.3f;
            sphere.isTrigger = true;
        }

        private static void Assign(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Poll()
        {
            bool actionProbe = SessionState.GetBool("Endfield.Probe.Actions", false);
            bool hitProbe = SessionState.GetBool("Endfield.Probe.Hits", false);
            if (hitProbe && CombatHitProbe.Completed)
                Finish(string.IsNullOrEmpty(CombatHitProbe.Failure), "P1-03 assertions=" + CombatHitProbe.Assertions + "; " + CombatHitProbe.Failure);
            else if (!hitProbe && actionProbe && CombatActionProbe.Completed)
                Finish(string.IsNullOrEmpty(CombatActionProbe.Failure), "P1-02 assertions=" + CombatActionProbe.Assertions + "; " + CombatActionProbe.Failure);
            else if (!actionProbe && CombatEntryProbe.Completed)
                Finish(string.IsNullOrEmpty(CombatEntryProbe.Failure), "assertions=" + CombatEntryProbe.Assertions + "; " + CombatEntryProbe.Failure);
            else if (EditorApplication.timeSinceStartup - startedAt > 60d)
                Finish(false, "P1-01 Play Mode timed out");
        }

        private static void Finish(bool passed, string details)
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= Poll;
            bool actionProbe = SessionState.GetBool("Endfield.Probe.Actions", false);
            bool hitProbe = SessionState.GetBool("Endfield.Probe.Hits", false);
            string report = hitProbe ? "Logs/CombatHitValidation.txt" : actionProbe ? "Logs/CombatActionValidation.txt" : "Logs/CombatEntryValidation.txt";
            File.WriteAllText(report, (passed ? "PASS\n" : "FAIL\n") + details);
            Debug.Log("COMBAT_ENTRY_VALIDATION " + (passed ? "PASS " : "FAIL ") + details);
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
