using System;
using System.IO;
using System.Linq;
using Endfield.Combat;
using Endfield.Controller;
using Endfield.UI;
using Endfield.Validation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Endfield.EditorValidation
{
    [InitializeOnLoad]
    public static class CombatEntryBatch
    {
        private const string SceneKey = "Endfield.EntryProbe.Scene";
        private static double startedAt;

        static CombatEntryBatch()
        {
            if (SessionState.GetString(SceneKey, "") != "")
                StartPolling();
        }

        public static void Run() => OpenProbe("CombatEntryValidation");

        public static void RunActions() => OpenProbe("CombatActionValidation");

        public static void RunHits() => OpenProbe("CombatHitValidation");

        public static void RunDemo() => OpenProbe("CombatDemo");

        private static void OpenProbe(string scene)
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Launch this entry point in Unity batch mode");

            SessionState.SetString(SceneKey, scene);
            try
            {
                string folder = scene == "CombatDemo" ? "Assets/Scenes/" : "Assets/Tests/Scenes/";
                EditorSceneManager.OpenScene(folder + scene + ".unity");
                StartPolling();
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Finish(scene, false, exception.ToString());
            }
        }

        private static void StartPolling()
        {
            startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            string scene = SessionState.GetString(SceneKey, "");
            if (EditorApplication.timeSinceStartup - startedAt > 60d)
            {
                Finish(scene, false, "Play Mode timed out");
                return;
            }
            if (!EditorApplication.isPlaying)
                return;

            if (scene == "CombatDemo")
            {
                if (Time.frameCount < 3)
                    return;
                try
                {
                    Finish(scene, true, ValidateDemo());
                }
                catch (Exception exception)
                {
                    Finish(scene, false, exception.ToString());
                }
                return;
            }

            (bool completed, int assertions, string failure) = scene switch
            {
                "CombatHitValidation" => (CombatHitProbe.Completed, CombatHitProbe.Assertions, CombatHitProbe.Failure),
                "CombatActionValidation" => (CombatActionProbe.Completed, CombatActionProbe.Assertions, CombatActionProbe.Failure),
                _ => (CombatEntryProbe.Completed, CombatEntryProbe.Assertions, CombatEntryProbe.Failure)
            };
            if (completed)
                Finish(scene, string.IsNullOrEmpty(failure), $"assertions={assertions} {failure}");
        }

        private static string ValidateDemo()
        {
            var simulation = UnityEngine.Object.FindFirstObjectByType<CombatSimulation>();
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
            var character = player.Character;
            var target = simulation.Units.First(unit => unit.Team != character.Team);
            var controller = character.GetComponent<CharacterController>();
            var hurtbox = character.GetComponentsInChildren<CapsuleCollider>().Single(collider => collider.isTrigger);
            if (!Mathf.Approximately(controller.height, 1.6f) || !Mathf.Approximately(controller.radius, .3f) ||
                controller.center != Vector3.up * .8f || !Mathf.Approximately(hurtbox.height, 1.7f) ||
                !Mathf.Approximately(hurtbox.radius, .45f) || hurtbox.center != Vector3.up * .85f)
                throw new InvalidOperationException("CombatDemo player controller or hurtbox dimensions differ");

            using var hud = new SerializedObject(UnityEngine.Object.FindFirstObjectByType<CombatHUD>());
            var actionLabel = (TMP_Text)hud.FindProperty("actionText").objectReferenceValue;
            var targetLabel = (TMP_Text)hud.FindProperty("targetText").objectReferenceValue;
            if (hud.FindProperty("simulation").objectReferenceValue != simulation || hud.FindProperty("player").objectReferenceValue != player ||
                !actionLabel.text.Contains("待机") || !targetLabel.text.Contains(target.DisplayName))
                throw new InvalidOperationException("CombatDemo HUD did not initialize its character and target labels");

            target.transform.position = character.transform.position + character.transform.forward * 1.5f;
            Physics.SyncTransforms();
            float previousHealth = target.Health.Current;
            simulation.QueueAttack(Time.frameCount);
            for (int frame = 0; frame < 120 && target.Health.Current == previousHealth; frame++)
                simulation.AdvanceFrame(1f / 60f, false);
            float damage = previousHealth - target.Health.Current;
            if (damage <= 0f || !targetLabel.text.Contains($"{target.Health.Current:0} / {target.Health.Max:0}") ||
                !actionLabel.text.Contains($"最近命中 {damage:0.#}"))
                throw new InvalidOperationException("CombatDemo hit did not update both HUD labels");
            return $"controller {controller.height:F2} m, hurtbox {hurtbox.height:F2} m, first attack {damage:0.#} damage, both HUD labels updated";
        }

        private static void Finish(string scene, bool passed, string details)
        {
            SessionState.EraseString(SceneKey);
            EditorApplication.update -= Poll;
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/" + scene + ".txt", (passed ? "PASS\n" : "FAIL\n") + details);
            Debug.Log("COMBAT_ENTRY_VALIDATION " + (passed ? "PASS " : "FAIL ") + details);
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
