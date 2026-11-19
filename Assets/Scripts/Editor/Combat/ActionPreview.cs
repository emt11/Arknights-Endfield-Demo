using System;
using System.Collections.Generic;
using Endfield.Presentation;
using Endfield.AnimationSystem;
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
        private sealed class ParticlePreview
        {
            public PooledParticleEffect instance;
            public Transform anchor;
            public Vector3 offset;
            public Quaternion rotation;
            public float start;
            public float end;
            public bool follow;
        }

        private readonly List<ParticlePreview> particles = new List<ParticlePreview>();
        private readonly PreviewRenderUtility preview;
        private readonly GameObject model;
        private readonly ActionDefinition action;
        private readonly AnimationDefinition animation;
        private readonly GameObject sourcePrefab;
        private readonly Hash128 sourcePrefabHash;
        private readonly float bodyRadius;
        private float simulationStep;
        private Hash128 pathHash;
        private Hash128 effectHash;
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
        public float TargetRadius { get; private set; }
        public float BodyRadius => bodyRadius;

        public ActionPreview(ActionDefinition definition, GameObject prefab, float targetRadius = .5f, float step = 1f / 60f, Vector3? initialTarget = null)
            : this(definition.animation, prefab, step, definition, targetRadius, initialTarget) { }

        public ActionPreview(AnimationDefinition animation, GameObject prefab, float step, ActionDefinition definition = null, float targetRadius = .5f, Vector3? initialTarget = null)
        {
            Animator source = prefab.GetComponentInChildren<Animator>(true);
            if (source == null)
                throw new InvalidOperationException("Preview requires an Animator");
            if (animation.clip.humanMotion && (source.avatar == null || !source.avatar.isValid || !source.avatar.isHuman))
                throw new InvalidOperationException("Humanoid animation preview requires a valid Humanoid Avatar");

            action = definition;
            this.animation = animation;
            sourcePrefab = prefab;
            sourcePrefabHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(prefab));
            bodyRadius = prefab.TryGetComponent(out Combatant character) ? character.BodyRadius : 0f;
            preview = new PreviewRenderUtility();
            try
            {
                model = preview.InstantiatePrefabInScene(source.gameObject);
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                WeaponController weapon = prefab.GetComponent<WeaponController>();
                if (action != null && weapon != null)
                {
                    Transform socket = AnimationAnchorBindings.Resolve(model.transform, AnimationAnchor.Weapon);
                    GameObject instance = preview.InstantiatePrefabInScene(weapon.WeaponPrefab);
                    instance.transform.SetParent(socket, false);
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
                clip = AnimationClipPlayable.Create(graph, animation.clip);
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
                clip.SetSpeed(0f);
                Refresh(step, targetRadius, initialTarget ?? targetPosition);
                Sample(0f);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal bool Matches(ActionDefinition definition, AnimationDefinition sourceAnimation, GameObject prefab) =>
            action == definition && animation == sourceAnimation && sourcePrefab == prefab && clip.GetAnimationClip() == sourceAnimation.clip &&
            sourcePrefabHash == AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(prefab));

        internal void Refresh(float step, float targetRadius, Vector3 position)
        {
            Hash128 paths = default;
            Hash128 effects = default;
            if (action != null)
            {
                paths.Append(action.useRootMotion ? 1 : 0);
                paths.Append(action.openingTurnDuration);
                paths.Append(animation.playbackRate);
                if (action.rootMotion != null)
                {
                    paths.Append(action.rootMotion.GetInstanceID());
                    paths.Append(EditorUtility.GetDirtyCount(action.rootMotion));
                }
            }
            foreach (TimelinePointEvent entry in animation.pointEvents)
                AppendEventHash(entry, ref paths, ref effects);
            foreach (TimelineWindowEvent entry in animation.windowEvents)
                AppendEventHash(entry, ref paths, ref effects);
            bool updatePaths = warped == null || paths != pathHash || step != simulationStep || targetRadius != TargetRadius || position != targetPosition;
            simulationStep = step;
            TargetRadius = targetRadius;
            if (action != null && updatePaths)
                SetTarget(position);
            pathHash = paths;
            if (effects == effectHash)
                return;

            foreach (ParticlePreview particle in particles)
                UnityEngine.Object.DestroyImmediate(particle.instance.gameObject);
            particles.Clear();
            foreach (TimelinePointEvent entry in animation.pointEvents)
            {
                if (entry is EffectPoint effect)
                    AddEffect(effect.effect, effect.anchor, effect.offset, effect.rotation, effect.time, float.PositiveInfinity, effect.follow);
            }
            foreach (TimelineWindowEvent entry in animation.windowEvents)
            {
                if (entry is EffectWindow effect)
                    AddEffect(effect.effect, effect.anchor, effect.offset, effect.rotation, effect.start, effect.end, effect.follow);
            }
            effectHash = effects;
        }

        private static void AppendEventHash(object entry, ref Hash128 paths, ref Hash128 effects)
        {
            if (entry is MotionWarpWindow)
                paths.Append(JsonUtility.ToJson(entry));
            else if (entry is EffectPoint || entry is EffectWindow)
            {
                effects.Append(JsonUtility.ToJson(entry));
                ParticleEffectDefinition definition = entry is EffectPoint point ? point.effect : ((EffectWindow)entry).effect;
                if (definition != null)
                    effects.Append(EditorUtility.GetDirtyCount(definition));
            }
        }

        public void SetTarget(Vector3 position)
        {
            targetPosition = position;
            bool eligible = TargetingController.IsWithinAssistance(position);
            var target = new WarpTarget(true, position, TargetRadius, eligible);
            var sourceRuntime = new ActionRuntime(action, 0, null);
            var warpedRuntime = new ActionRuntime(action, 0, null);
            float localStep = simulationStep * animation.playbackRate;
            int count = Mathf.CeilToInt(animation.Duration / localStep) + 1;
            original = new RootMotionSample[count];
            warped = new RootMotionSample[count];
            original[0] = new RootMotionSample { rotation = Quaternion.identity };
            warped[0].rotation = warpedRuntime.InitializeOpeningFacing(Quaternion.identity, eligible ? position : Vector3.zero);
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clip.SetTime(0d);
            clip.SetSpeed(animation.playbackRate);
            graph.Evaluate(0f);
            for (int index = 1; index < count; index++)
            {
                float time = index == count - 1 ? animation.Duration : index * localStep;
                sourceRuntime.Advance((time - sourceRuntime.LocalTime) / animation.playbackRate);
                warpedRuntime.Advance((time - warpedRuntime.LocalTime) / animation.playbackRate);
                RootMotionSample from = original[index - 1];
                RootMotionSample previousWarp = warped[index - 1];
                Vector3 sourceDelta = Vector3.zero;
                Quaternion sourceRotation = from.rotation;
                Vector3 warpedDelta = Vector3.zero;
                Quaternion warpedRotation = previousWarp.rotation;
                if (action.useRootMotion && warpedRuntime.HasWarp)
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
                    graph.Evaluate((time - from.time) / animation.playbackRate);
                    sourceDelta = root.DeltaPosition;
                    sourceDelta.y = 0f;
                    sourceRotation *= root.DeltaRotation;
                    Vector3 localDelta = Quaternion.Inverse(from.rotation) * sourceDelta;
                    ActionMovement movement = warpedRuntime.EvaluateAnimationMovement(previousWarp.rotation, localDelta, root.DeltaRotation);
                    warpedDelta = movement.delta;
                    warpedRotation = movement.rotation;
                }
                else
                    warpedRotation = warpedRuntime.EvaluateAnimationMovement(previousWarp.rotation, Vector3.zero, Quaternion.identity).rotation;
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

        private void AddEffect(ParticleEffectDefinition effect, AnimationAnchor point, Vector3 offset, Vector3 rotation, float start, float end, bool follow)
        {
            if (effect == null || effect.Prefab == null)
                return;

            Transform anchor = AnimationAnchorBindings.Resolve(model.transform, point);

            GameObject instance = preview.InstantiatePrefabInScene(effect.Prefab.gameObject);
            instance.transform.localScale *= effect.Scale;
            instance.SetActive(false);
            foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.randomSeed = 1u;
            }
            particles.Add(new ParticlePreview
            {
                instance = instance.GetComponent<PooledParticleEffect>(),
                anchor = anchor,
                offset = offset,
                rotation = Quaternion.Euler(rotation),
                start = start,
                end = end,
                follow = follow
            });
        }

        public void Sample(float time)
        {
            // 预览一个战斗步的姿态区间，仅采样模型，不执行物理查询
            foreach (ParticlePreview effect in particles)
            {
                bool visible = time >= effect.start && time < effect.end;
                effect.instance.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                EvaluatePose(effect.follow ? time : effect.start);
                effect.instance.transform.SetPositionAndRotation(effect.anchor.TransformPoint(effect.offset), effect.anchor.rotation * effect.rotation);
                foreach (ParticleSystem system in effect.instance.StartSystems)
                    system.Simulate((time - effect.start) / animation.playbackRate, false, true, false);
            }
            PreviousTime = Mathf.Max(0f, time - animation.playbackRate * simulationStep);
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
            clip.SetTime(Mathf.Clamp(time, 0f, animation.Duration));
            graph.Evaluate(0f);
            if (warped != null)
            {
                int index = Mathf.Min(Mathf.FloorToInt(time / (simulationStep * animation.playbackRate)), warped.Length - 2);
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
