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
        public static string Signature(RootMotionDefinition root) => root.CurrentSignature();

        public static void ValidateSignature(RootMotionDefinition root)
        {
            if (root.DependencySignature != Signature(root))
                throw new InvalidOperationException(root.name + " 源动画或模型已变化，请重新烘焙根轨迹");
        }

        public static void Bake(RootMotionDefinition root)
        {
            if (root.ModelPrefab == null || root.Clip == null || root.SampleRate <= 0f)
                throw new InvalidOperationException("Select a pure model prefab, clip and positive sample rate");

            Scene scene = EditorSceneManager.NewPreviewScene();
            PlayableGraph graph = default;
            try
            {
                GameObject model = UnityEngine.Object.Instantiate(root.ModelPrefab);
                SceneManager.MoveGameObjectToScene(model, scene);
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                model.transform.localScale = root.ModelScale;
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
                AnimationClipPlayable clip = AnimationClipPlayable.Create(graph, root.Clip);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Root", animator);
                output.SetSourcePlayable(clip);
                graph.Play();
                graph.Evaluate(0f);
                int count = Mathf.CeilToInt(root.Clip.length * root.SampleRate) + 1;
                var samples = new RootMotionSample[count];
                samples[0] = new RootMotionSample { Time = 0f, Rotation = Quaternion.identity };
                float previous = 0f;
                for (int index = 1; index < count; index++)
                {
                    float time = Mathf.Min(index / root.SampleRate, root.Clip.length);
                    receiver.Clear();
                    graph.Evaluate(time - previous);
                    model.transform.position += receiver.DeltaPosition;
                    model.transform.rotation *= receiver.DeltaRotation;
                    samples[index] = new RootMotionSample
                    {
                        Time = time,
                        Position = model.transform.position,
                        Rotation = Quaternion.Euler(0f, model.transform.eulerAngles.y, 0f)
                    };
                    previous = time;
                }

                Undo.RecordObject(root, "Bake root motion");
                root.Avatar = animator.avatar;
                root.Samples = samples;
                root.DependencySignature = Signature(root);
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
