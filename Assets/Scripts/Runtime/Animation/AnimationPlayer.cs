using System;
using System.Collections.Generic;
using Endfield.Presentation;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Endfield.AnimationSystem
{
    [DisallowMultipleComponent]
    public sealed class AnimationPlayer : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private RuntimeAnimatorController locomotionController;
        [SerializeField] private CombatFeedbackPlayer feedback;
        private PlayableGraph graph;
        private AnimatorControllerPlayable locomotion;
        private AnimationMixerPlayable mixer;
        private sealed class ClipInput
        {
            public AnimationDefinition definition;
            public AnimationClipPlayable clip;
            public int port;
            public float rate;
            public float startWeight;
        }

        private readonly List<ClipInput> inputs = new List<ClipInput>();
        private ClipInput current;
        private float locomotionStartWeight;
        private float transitionTime;
        private float transitionDuration;
        private float naturalExitStart;
        private bool returningToLocomotion;
        private AnimatorRootMotion root;
        private bool drivenBySimulation;

        public AnimationPlayback Current { get; private set; }
        public Transform Model => animator.transform;
        public Vector3 RootDelta => root.DeltaPosition;
        public Quaternion RootRotation => root.DeltaRotation;
        public double ClipTime { get; private set; }
        internal event Action Disabled;

        private void OnEnable()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (animator == null)
                throw new InvalidOperationException(name + " requires an Animator");

            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            root = animator.GetComponent<AnimatorRootMotion>();
            if (root == null)
                root = animator.gameObject.AddComponent<AnimatorRootMotion>();

            graph = PlayableGraph.Create(name + " Animation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 1);
            if (locomotionController != null)
            {
                locomotion = AnimatorControllerPlayable.Create(graph, locomotionController);
                graph.Connect(locomotion, 0, mixer, 0);
                mixer.SetInputWeight(0, 1f);
            }
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Model", animator);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        internal void UseSimulationClock(CombatFeedbackPlayer presentation)
        {
            drivenBySimulation = true;
            feedback = presentation;
        }

        internal void SetLocomotion(float moveAmount, float rate)
        {
            if (!locomotion.IsValid())
                return;

            locomotion.SetFloat("MoveAmount", moveAmount);
            locomotion.SetFloat("PlaybackRate", rate);
        }

        private void Update()
        {
            float step = Time.deltaTime;
            if (drivenBySimulation || step <= 0f)
                return;

            AnimationPlayback playback = Current;
            playback?.Advance(step);
            if (Current != playback || !isActiveAndEnabled)
                return;

            EvaluatePose(step);
            DispatchEvents(playback);
        }

        // 战斗动作持有播放权，普通播放请求可通过返回值判断是否获准
        public AnimationPlayback Play(AnimationDefinition definition)
        {
            if (!isActiveAndEnabled || Current?.IsActionPlayback == true)
                return null;

            return ReplaceWith(definition);
        }

        internal AnimationPlayback ReplaceWith(AnimationDefinition definition)
        {
            var next = new AnimationPlayback(definition, Model, feedback, null);
            AnimationPlayback outgoing = StartPlayback(next);
            outgoing?.End();
            return next;
        }

        internal AnimationPlayback StartPlayback(AnimationPlayback next)
        {
            AnimationPlayback outgoing = Current;
            CaptureStartWeights();
            AnimationDefinition definition = next.Definition;
            // 短片段按同一比例压缩首尾过渡，让入场与自然收招各有一段时间
            float totalDuration = definition.easeInDuration + definition.easeOutDuration;
            float scale = locomotion.IsValid() && totalDuration > definition.Duration ? definition.Duration / totalDuration : 1f;
            transitionDuration = definition.easeInDuration * scale;
            naturalExitStart = definition.Duration - definition.easeOutDuration * scale;
            transitionTime = 0f;
            returningToLocomotion = false;

            int port = 1;
            while (port < mixer.GetInputCount() && mixer.GetInput(port).IsValid())
                port++;
            if (port == mixer.GetInputCount())
                mixer.SetInputCount(port + 1);

            current = new ClipInput
            {
                definition = definition,
                clip = AnimationClipPlayable.Create(graph, definition.clip),
                port = port,
                startWeight = inputs.Count == 0 && !locomotion.IsValid() ? 1f : 0f
            };
            inputs.Add(current);
            graph.Connect(current.clip, 0, mixer, port);
            Current = next;
            EvaluatePose(0f);
            return outgoing;
        }

        private void CaptureStartWeights()
        {
            foreach (ClipInput input in inputs)
                input.startWeight = mixer.GetInputWeight(input.port);
            if (locomotion.IsValid())
                locomotionStartWeight = mixer.GetInputWeight(0);
        }

        internal void EvaluatePose(float step)
        {
            if (current != null)
            {
                if (Current != null)
                {
                    current.rate = Current.PlaybackRate;
                    if (!returningToLocomotion && locomotion.IsValid() && Current.Time >= naturalExitStart)
                    {
                        // 即使一步跨过边界，自然收招也从入场完成时的姿态开始
                        foreach (ClipInput input in inputs)
                            input.startWeight = input == current ? 1f : 0f;
                        locomotionStartWeight = 0f;
                        returningToLocomotion = true;
                        transitionDuration = current.definition.Duration - naturalExitStart;
                    }
                    transitionTime = returningToLocomotion ? Current.Time - naturalExitStart : Current.Time;
                }
                else
                    transitionTime += step * current.rate;

                EvaluateTransition(step);
            }

            root.Clear();
            graph.Evaluate(step);
            ClipTime = current != null ? current.clip.GetTime() : 0d;
        }

        private void EvaluateTransition(float step)
        {
            bool finished = transitionTime >= transitionDuration;
            float sourceShare = 1f;
            if (finished)
                sourceShare = 0f;
            else if (transitionTime > 0f)
            {
                float progress = transitionTime / transitionDuration;
                AnimationDefinition definition = current.definition;
                sourceShare = returningToLocomotion ? Mathf.Clamp01(definition.easeOutCurve.Evaluate(progress))
                    : 1f - Mathf.Clamp01(definition.easeInCurve.Evaluate(progress));
            }

            float targetShare = 1f - sourceShare;
            if (locomotion.IsValid())
                mixer.SetInputWeight(0, locomotionStartWeight * sourceShare + (returningToLocomotion ? targetShare : 0f));

            for (int index = inputs.Count - 1; index >= 0; index--)
            {
                ClipInput input = inputs[index];
                bool releaseSource = input != current && (finished || input.startWeight == 0f);
                if (releaseSource || (finished && returningToLocomotion && Current == null))
                {
                    mixer.SetInputWeight(input.port, 0f);
                    graph.Disconnect(mixer, input.port);
                    graph.DestroyPlayable(input.clip);
                    inputs.RemoveAt(index);
                    continue;
                }

                bool incoming = input == current && !returningToLocomotion;
                mixer.SetInputWeight(input.port, input.startWeight * sourceShare + (incoming ? targetShare : 0f));

                double clipTime = input.clip.GetTime();
                double increment = Math.Min(step * input.rate, Math.Max(0d, input.definition.Duration - clipTime));
                if (input == current && Current != null)
                    increment = Current.Time - clipTime;
                else if (incoming)
                    increment = 0d; // 没有基础移动时，已停止的当前片段保留最后采样姿态
                input.clip.SetSpeed(step > 0f ? increment / step : 0d);
            }
            if (inputs.Count == 0)
                current = null;
        }

        internal void DispatchEvents(AnimationPlayback playback)
        {
            if (playback == null || Current != playback)
                return;

            playback.Dispatch();
            if (!playback.IsActionPlayback && playback.Time >= playback.Definition.Duration)
                Stop(playback);
        }

        public bool Stop(AnimationPlayback playback)
        {
            if (playback == null || playback != Current || playback.IsActionPlayback)
                return false;

            EndPlayback(playback);
            return true;
        }

        internal void EndPlayback(AnimationPlayback playback)
        {
            if (playback == null || Current != playback)
                return;

            Current = null;
            if (locomotion.IsValid() && !returningToLocomotion)
            {
                CaptureStartWeights();
                returningToLocomotion = true;
                transitionTime = 0f;
                transitionDuration = playback.Definition.easeOutDuration;
                current.rate = playback.PlaybackRate;
                if (transitionDuration == 0f)
                    EvaluatePose(0f);
            }
            else if (returningToLocomotion && transitionTime >= transitionDuration)
                EvaluateTransition(0f); // 保留本步已采样的片尾时间，释放自然收招的输入
            playback.End();
        }

        private void OnDisable()
        {
            AnimationPlayback ended = Current;
            Current = null;
            ended?.End();
            Disabled?.Invoke();
            if (graph.IsValid())
                graph.Destroy();
            inputs.Clear();
            current = null;
            ClipTime = 0d;
        }
    }
}
