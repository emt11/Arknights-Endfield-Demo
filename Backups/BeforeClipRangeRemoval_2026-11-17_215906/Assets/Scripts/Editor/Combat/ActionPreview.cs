using System;
using Endfield.Combat;
using Endfield.Character;
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
        private readonly float bodyRadius;
        private readonly float simulationStep;
        private readonly AnimatorRootMotion root;
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
        public float TargetRadius { get; }
        public float BodyRadius => bodyRadius;

        public ActionPreview(ActionDefinition definition, GameObject prefab, float targetRadius = .5f, float step = 1f / 60f)
        {
            Animator source = prefab.GetComponentInChildren<Animator>(true);
            if (source == null || source.avatar == null || !source.avatar.isValid)
                throw new InvalidOperationException("Preview requires a model with a valid Avatar");

            action = definition;
            bodyRadius = prefab.GetComponent<Combatant>().BodyRadius;
            TargetRadius = targetRadius;
            simulationStep = step;
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
                animator.applyRootMotion = true;
                root = model.GetComponent<AnimatorRootMotion>();
                if (root == null)
                    root = model.AddComponent<AnimatorRootMotion>();
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
            bool eligible = TargetingController.IsWithinAssistance(Vector3.forward, position);
            var target = new WarpTarget(true, position, TargetRadius, eligible);
            var sourceRuntime = new ActionRuntime(action, 0, null);
            var warpedRuntime = new ActionRuntime(action, 0, null);
            float localStep = simulationStep * action.playbackRate;
            int count = Mathf.CeilToInt(action.Duration / localStep) + 1;
            original = new RootMotionSample[count];
            warped = new RootMotionSample[count];
            original[0] = new RootMotionSample { rotation = Quaternion.identity };
            warped[0] = original[0];
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clip.SetTime(action.clipStart);
            clip.SetSpeed(action.playbackRate);
            graph.Evaluate(0f);
            for (int index = 1; index < count; index++)
            {
                float time = Mathf.Min(index * localStep, action.Duration);
                sourceRuntime.Advance((time - sourceRuntime.LocalTime) / action.playbackRate);
                warpedRuntime.Advance((time - warpedRuntime.LocalTime) / action.playbackRate);
                RootMotionSample from = original[index - 1];
                RootMotionSample previousWarp = warped[index - 1];
                Vector3 sourceDelta = Vector3.zero;
                Quaternion sourceRotation = from.rotation;
                Vector3 warpedDelta = Vector3.zero;
                Quaternion warpedRotation = previousWarp.rotation;
                if (action.useRootMotion && action.warpWindows.Length > 0)
                {
                    ActionMovement source = MotionWarping.Evaluate(sourceRuntime, from.position, from.rotation, bodyRadius, default, _ => true);
                    ActionMovement movement = MotionWarping.Evaluate(warpedRuntime, previousWarp.position, previousWarp.rotation, bodyRadius, target, _ => true);
                    sourceDelta = source.delta;
                    sourceRotation = source.rotation;
                    warpedDelta = movement.delta;
                    warpedRotation = movement.rotation;
                }
                else if (action.useRootMotion)
                {
                    model.transform.SetPositionAndRotation(from.position, from.rotation);
                    root.Clear();
                    graph.Evaluate((time - from.time) / action.playbackRate);
                    sourceDelta = root.DeltaPosition;
                    sourceDelta.y = 0f;
                    sourceRotation *= root.DeltaRotation;
                    warpedDelta = sourceDelta;
                    warpedRotation = sourceRotation;
                }
                original[index] = new RootMotionSample
                {
                    time = time,
                    position = from.position + sourceDelta,
                    rotation = sourceRotation
                };
                warped[index] = new RootMotionSample
                {
                    time = time,
                    position = previousWarp.position + warpedDelta,
                    rotation = warpedRotation
                };
            }
            clip.SetSpeed(0f);
        }

        public void Sample(float time)
        {
            // 预览一个战斗步的姿态区间，仅采样模型，不执行物理查询
            PreviousTime = Mathf.Max(0f, time - action.playbackRate * simulationStep);
            EvaluatePose(PreviousTime);
            foreach (Hitbox hitbox in Hitboxes)
                hitbox.CaptureStart();
            EvaluatePose(time);
            foreach (Hitbox hitbox in Hitboxes)
                hitbox.CaptureEnd();
        }

        private void EvaluatePose(float time)
        {
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clip.SetTime(action.clipStart + Mathf.Clamp(time, 0f, action.Duration));
            graph.Evaluate(0f);
            if (warped != null)
            {
                int index = Mathf.Min(Mathf.FloorToInt(time / (simulationStep * action.playbackRate)), warped.Length - 2);
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
