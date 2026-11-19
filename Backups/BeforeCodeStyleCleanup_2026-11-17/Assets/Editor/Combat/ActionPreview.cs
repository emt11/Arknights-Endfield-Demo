using System;
using Endfield.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Endfield.EditorCombat
{
    public sealed class ActionPreview : IDisposable
    {
        private readonly PreviewRenderUtility preview;
        private readonly GameObject model;
        private readonly ActionDefinition action;
        private PlayableGraph graph;
        private AnimationClipPlayable clip;
        private RootMotionSample[] warped;
        private RootMotionSample[] original;
        private Vector3 targetPosition = new Vector3(0f, 0f, 2f);

        public Camera Camera => preview.camera;
        public Transform Model => model.transform;
        public RootMotionSample[] OriginalPath => original;
        public RootMotionSample[] WarpedPath => warped;
        public Vector3 TargetPosition => targetPosition;

        public ActionPreview(ActionDefinition definition, GameObject prefab)
        {
            Animator source = prefab.GetComponentInChildren<Animator>(true);
            if (source == null || source.avatar == null || !source.avatar.isValid)
                throw new InvalidOperationException("Preview requires a model with a valid Avatar");

            action = definition;
            preview = new PreviewRenderUtility();
            try
            {
                model = preview.InstantiatePrefabInScene(source.gameObject);
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                Animator animator = model.GetComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.fireEvents = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph = PlayableGraph.Create("Action isolated preview");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                clip = AnimationClipPlayable.Create(graph, action.Clip);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Model", animator);
                output.SetSourcePlayable(clip);
                graph.Play();
                preview.cameraFieldOfView = 30f;
                preview.camera.transform.position = new Vector3(3.2f, 2f, 4.4f);
                preview.camera.transform.LookAt(new Vector3(0f, 0.9f, 0f));
                preview.camera.nearClipPlane = 0.01f;
                preview.camera.farClipPlane = 100f;
                preview.camera.clearFlags = CameraClearFlags.Color;
                preview.camera.backgroundColor = new Color(0.08f, 0.09f, 0.11f);
                preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35f, 30f, 0f);
                preview.lights[1].intensity = 0.8f;
                preview.ambientColor = Color.gray;
                SetTarget(targetPosition);
                Sample(0f);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void SetTarget(Vector3 position)
        {
            targetPosition = position;
            if (action.WarpWindows.Length == 0)
                return;
            bool eligible = position.magnitude <= 3.5f && Vector3.Angle(Vector3.forward, position) <= 60f && Mathf.Abs(position.y) <= .5f;
            original = MotionWarping.Preview(action, default);
            warped = MotionWarping.Preview(action, new WarpTarget(true, position, .4f, eligible));
        }

        public void Sample(float time)
        {
            clip.SetTime(action.ClipStart + Mathf.Clamp(time, 0f, action.Duration));
            graph.Evaluate(0f);
            if (warped != null)
            {
                int index = Mathf.Min(Mathf.FloorToInt(time * 60f), warped.Length - 2);
                RootMotionSample from = warped[index];
                RootMotionSample to = warped[index + 1];
                float weight = Mathf.InverseLerp(from.Time, to.Time, time);
                model.transform.SetPositionAndRotation(Vector3.Lerp(from.Position, to.Position, weight), Quaternion.Slerp(from.Rotation, to.Rotation, weight));
            }
        }

        public void Draw(Rect rect)
        {
            preview.BeginPreview(rect, GUIStyle.none);
            preview.Render(true);
            GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.StretchToFill, false);
        }

        public Texture2D Snapshot(int width, int height)
        {
            preview.BeginStaticPreview(new Rect(0f, 0f, width, height));
            preview.Render(true);
            return preview.EndStaticPreview();
        }

        public void Dispose()
        {
            if (graph.IsValid())
                graph.Destroy();

            preview.Cleanup();
        }
    }
}
