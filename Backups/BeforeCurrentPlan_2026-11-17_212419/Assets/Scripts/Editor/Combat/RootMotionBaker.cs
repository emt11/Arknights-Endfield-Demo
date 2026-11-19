using System;
using Endfield.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace Endfield.EditorCombat
{
    public static class RootMotionBaker
    {
        public static string Signature(RootMotionDefinition root) => AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(root.clip)) + ":" + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(root.modelPrefab)) + ":" + root.modelScale.ToString("R");

        public static void Bake(RootMotionDefinition root)
        {
            if (root.modelPrefab == null || root.clip == null || root.sampleRate <= 0f)
                throw new InvalidOperationException("Select a pure model prefab, clip and positive sample rate");

            Scene scene = EditorSceneManager.NewPreviewScene();
            PlayableGraph graph = default;
            try
            {
                GameObject model = UnityEngine.Object.Instantiate(root.modelPrefab);
                SceneManager.MoveGameObjectToScene(model, scene);
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                model.transform.localScale = root.modelScale;
                Animator animator = model.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isValid)
                    throw new InvalidOperationException("Root bake requires a valid Animator and Avatar on model root");

                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = true;
                animator.fireEvents = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                AnimatorRootMotion receiver = model.AddComponent<AnimatorRootMotion>();
                graph = PlayableGraph.Create("Root motion bake");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                AnimationClipPlayable clip = AnimationClipPlayable.Create(graph, root.clip);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Root", animator);
                output.SetSourcePlayable(clip);
                graph.Play();
                graph.Evaluate(0f);
                int count = Mathf.CeilToInt(root.clip.length * root.sampleRate) + 1;
                var samples = new RootMotionSample[count];
                samples[0] = new RootMotionSample { time = 0f, rotation = Quaternion.identity };
                float previous = 0f;
                for (int index = 1; index < count; index++)
                {
                    float time = Mathf.Min(index / root.sampleRate, root.clip.length);
                    receiver.Clear();
                    graph.Evaluate(time - previous);
                    model.transform.position += receiver.DeltaPosition;
                    model.transform.rotation *= receiver.DeltaRotation;
                    samples[index] = new RootMotionSample
                    {
                        time = time,
                        position = model.transform.position,
                        rotation = Quaternion.Euler(0f, model.transform.eulerAngles.y, 0f)
                    };
                    previous = time;
                }

                Undo.RecordObject(root, "Bake root motion");
                root.avatar = animator.avatar;
                root.samples = samples;
                root.dependencySignature = Signature(root);
                EditorUtility.SetDirty(root);
                AssetDatabase.SaveAssetIfDirty(root);
            }
            finally
            {
                if (graph.IsValid())
                    graph.Destroy();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
