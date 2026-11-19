using System;
using System.IO;
using System.Linq;
using Endfield.Validation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace Endfield.EditorValidation
{
    [InitializeOnLoad]
    public static class CombatValidationBatch
    {
        private const string RunningKey = "Endfield.AnimationProbe.Running";
        private static double startedAt;

        static CombatValidationBatch()
        {
            if (SessionState.GetBool(RunningKey, false))
            {
                startedAt = EditorApplication.timeSinceStartup;
                EditorApplication.update += Poll;
            }
        }

        public static void RunAnimationProbe()
        {
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/Player.prefab");
                Animator source = prefab.GetComponentInChildren<Animator>(true);
                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Animations/Characters/Clips/GreatSword/Normal/GS_Attack1.fbx")
                    .OfType<AnimationClip>().First(item => !item.name.StartsWith("__preview__"));
                float bakedDistance = ValidatePreview(source.gameObject, clip);
                if (bakedDistance < 0.01f)
                    throw new InvalidOperationException("Preview Scene root samples are zero");

                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                CombatAnimationProbe probe = new GameObject("Animation prerequisite validation").AddComponent<CombatAnimationProbe>();
                probe.Model = source.gameObject;
                probe.Controller = source.runtimeAnimatorController;
                probe.Clip = clip;
                Directory.CreateDirectory("Assets/Tests/Scenes");
                EditorSceneManager.SaveScene(scene, "Assets/Tests/Scenes/CombatAnimationValidation.unity");
                Debug.Log("COMBAT_PREVIEW_PROBE distance=" + bakedDistance + " clip=" + clip.name + " avatar=" + source.avatar.name);
                SessionState.SetBool(RunningKey, true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private static float ValidatePreview(GameObject prefab, AnimationClip clip)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            PlayableGraph graph = default;
            try
            {
                GameObject model = UnityEngine.Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(model, scene);
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Animator animator = model.GetComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                ProbeRootMotion root = model.AddComponent<ProbeRootMotion>();
                graph = PlayableGraph.Create("Preview root prerequisite");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Model", animator);
                output.SetSourcePlayable(playable);
                graph.Play();
                graph.Evaluate(0f);
                Vector3 total = Vector3.zero;
                float time = 0f;
                while (time < clip.length)
                {
                    float delta = Mathf.Min(1f / 60f, clip.length - time);
                    graph.Evaluate(delta);
                    total += root.Delta;
                    model.transform.position += root.Delta;
                    model.transform.rotation *= root.Rotation;
                    time += delta;
                }

                total.y = 0f;
                return total.magnitude;
            }
            finally
            {
                if (graph.IsValid())
                    graph.Destroy();

                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void Poll()
        {
            if (CombatAnimationProbe.Completed)
            {
                bool passed = string.IsNullOrEmpty(CombatAnimationProbe.Failure);
                Finish(passed, "assertions=" + CombatAnimationProbe.Assertions + "; rootDistance=" + CombatAnimationProbe.RootDistance + "; " + CombatAnimationProbe.Failure);
            }
            else if (EditorApplication.timeSinceStartup - startedAt > 90d)
                Finish(false, "Play Mode validation timed out");
        }

        private static void Finish(bool passed, string details)
        {
            SessionState.SetBool(RunningKey, false);
            EditorApplication.update -= Poll;
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/CombatAnimationValidation.txt", (passed ? "PASS\n" : "FAIL\n") + details);
            Debug.Log("COMBAT_ANIMATION_VALIDATION " + (passed ? "PASS " : "FAIL ") + details);
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
