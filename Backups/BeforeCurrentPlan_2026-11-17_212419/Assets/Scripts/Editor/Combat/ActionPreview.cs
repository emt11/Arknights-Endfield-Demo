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
        public Hitbox[] Hitboxes { get; } = Array.Empty<Hitbox>();
        public float PreviousTime { get; private set; }

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
                WeaponController weapon = prefab.GetComponent<WeaponController>();
                if (weapon != null)
                {
                    string socketPath = AnimationUtility.CalculateTransformPath(weapon.Socket, source.transform);
                    GameObject instance = preview.InstantiatePrefabInScene(weapon.WeaponPrefab);
                    instance.transform.SetParent(model.transform.Find(socketPath), false);
                    Hitboxes = instance.GetComponentsInChildren<Hitbox>(true);
                }
                Animator animator = model.GetComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false;
                animator.fireEvents = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph = PlayableGraph.Create("Action isolated preview");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                clip = AnimationClipPlayable.Create(graph, action.clip);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Model", animator);
                output.SetSourcePlayable(clip);
                graph.Play();
                preview.cameraFieldOfView = 30f;
                preview.camera.transform.position = new Vector3(4.5f, 3f, 6f);
                preview.camera.transform.LookAt(new Vector3(0f, 1f, 1f));
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
            if (action.warpWindows.Length == 0)
                return;
            bool eligible = position.magnitude <= 3.5f && Vector3.Angle(Vector3.forward, position) <= 60f && Mathf.Abs(position.y) <= .5f;
            original = MotionWarping.Preview(action, default);
            warped = MotionWarping.Preview(action, new WarpTarget(true, position, .4f, eligible));
        }

        public void Sample(float time)
        {
            // 预览一个 60 Hz 战斗步的姿态区间，仅采样模型，不执行物理查询
            PreviousTime = Mathf.Max(0f, time - action.playbackRate / 60f);
            EvaluatePose(PreviousTime);
            foreach (Hitbox hitbox in Hitboxes)
                hitbox.CaptureStart();
            EvaluatePose(time);
            foreach (Hitbox hitbox in Hitboxes)
                hitbox.CaptureEnd();
        }

        private void EvaluatePose(float time)
        {
            clip.SetTime(action.clipStart + Mathf.Clamp(time, 0f, action.Duration));
            graph.Evaluate(0f);
            if (warped != null)
            {
                int index = Mathf.Min(Mathf.FloorToInt(time * 60f), warped.Length - 2);
                RootMotionSample from = warped[index];
                RootMotionSample to = warped[index + 1];
                float weight = Mathf.InverseLerp(from.time, to.time, time);
                model.transform.SetPositionAndRotation(Vector3.Lerp(from.position, to.position, weight), Quaternion.Slerp(from.rotation, to.rotation, weight));
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
