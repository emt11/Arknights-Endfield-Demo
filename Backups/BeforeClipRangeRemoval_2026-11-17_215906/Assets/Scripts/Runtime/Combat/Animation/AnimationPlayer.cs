using System;
using Endfield.Controller;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Endfield.Combat
{
    [DisallowMultipleComponent]
    public sealed class AnimationPlayer : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private RuntimeAnimatorController locomotionController;
        [SerializeField] private float walkReferenceSpeed = 2f;
        [SerializeField] private float runReferenceSpeed = 5f;
        private PlayableGraph graph;
        private AnimatorControllerPlayable locomotion;
        private AnimationMixerPlayable mixer;
        private AnimationClipPlayable current;
        private AnimationClipPlayable previous;
        private AnimatorRootMotion root;
        private float currentEnd;
        private float previousEnd;
        private float previousRate;
        private float blendDuration;
        private float blendTime;
        private bool fadingOut;

        public Transform Model => animator.transform;
        public Vector3 RootDelta => root.DeltaPosition;
        public Quaternion RootRotation => root.DeltaRotation;
        public double ClipTime => current.IsValid() ? current.GetTime() : 0d;

        private void OnEnable()
        {
            fadingOut = false;
            blendTime = 0f;
            if (animator == null || locomotionController == null)
                throw new InvalidOperationException(name + " requires an Animator and locomotion controller");

            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            root = animator.GetComponent<AnimatorRootMotion>();
            if (root == null)
                root = animator.gameObject.AddComponent<AnimatorRootMotion>();

            graph = PlayableGraph.Create(name + " Animation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            locomotion = AnimatorControllerPlayable.Create(graph, locomotionController);
            mixer = AnimationMixerPlayable.Create(graph, 3);
            graph.Connect(locomotion, 0, mixer, 0);
            mixer.SetInputWeight(0, 1f);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Character", animator);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        public void Play(ActionRuntime runtime)
        {
            DestroyClip(ref previous, 1);
            if (current.IsValid())
            {
                graph.Disconnect(mixer, 2);
                previous = current;
                previousEnd = currentEnd;
                previousRate = (float)current.GetSpeed();
                graph.Connect(previous, 0, mixer, 1);
            }

            current = AnimationClipPlayable.Create(graph, runtime.Definition.clip);
            currentEnd = runtime.Definition.clipEnd;
            current.SetTime(runtime.Definition.clipStart);
            current.SetSpeed(runtime.PlaybackRate);
            graph.Connect(current, 0, mixer, 2);
            blendDuration = runtime.Definition.blendDuration;
            blendTime = 0f;
            fadingOut = false;
            UpdateWeights();
            graph.Evaluate(0f);
            root.Clear();
        }

        public void Evaluate(float step, CharacterMotor motor, ActionRuntime runtime)
        {
            float speed = motor.CurrentSpeed;
            float moveAmount;
            float rate;
            if (speed == 0f)
            {
                moveAmount = 0f;
                rate = 1f;
            }
            else if (speed <= motor.WalkSpeed)
            {
                moveAmount = speed / motor.WalkSpeed;
                rate = motor.WalkSpeed / walkReferenceSpeed;
            }
            else
            {
                float weight = Mathf.InverseLerp(motor.WalkSpeed, motor.RunSpeed, speed);
                moveAmount = 1f + weight;
                rate = speed / Mathf.Lerp(walkReferenceSpeed, runReferenceSpeed, weight);
            }

            locomotion.SetFloat("MoveAmount", moveAmount);
            locomotion.SetFloat("PlaybackRate", rate);
            locomotion.SetFloat("TurnAngle", motor.TurnAngle);
            if (current.IsValid())
            {
                double increment = runtime != null ? runtime.LocalTime - runtime.PreviousTime : 0d;
                current.SetSpeed(increment / step);
            }

            if (previous.IsValid())
                previous.SetSpeed(Math.Min(previousRate, Math.Max(0d, previousEnd - previous.GetTime()) / step));

            blendTime += step;
            UpdateWeights();
            root.Clear();
            graph.Evaluate(step);
            if (blendTime >= blendDuration)
            {
                DestroyClip(ref previous, 1);
                if (fadingOut)
                    DestroyClip(ref current, 2);
            }
        }

        public void FadeOut(float duration)
        {
            fadingOut = true;
            blendDuration = duration;
            blendTime = 0f;
        }

        private void UpdateWeights()
        {
            float blend = blendDuration <= 0f ? 1f : Mathf.Clamp01(blendTime / blendDuration);
            if (fadingOut)
            {
                mixer.SetInputWeight(0, blend);
                mixer.SetInputWeight(1, 0f);
                mixer.SetInputWeight(2, current.IsValid() ? 1f - blend : 0f);
            }
            else
            {
                mixer.SetInputWeight(0, previous.IsValid() ? 0f : 1f - blend);
                mixer.SetInputWeight(1, previous.IsValid() ? 1f - blend : 0f);
                mixer.SetInputWeight(2, current.IsValid() ? blend : 0f);
                if (!current.IsValid())
                    mixer.SetInputWeight(0, 1f);
            }
        }

        private void DestroyClip(ref AnimationClipPlayable playable, int port)
        {
            if (!playable.IsValid())
                return;

            graph.Disconnect(mixer, port);
            graph.DestroyPlayable(playable);
            playable = default;
            mixer.SetInputWeight(port, 0f);
        }

        private void OnDisable()
        {
            var owner = GetComponent<Endfield.Character.Combatant>();
            owner.InterruptAction();
            owner.Motor.ResetMotion();
            if (graph.IsValid())
                graph.Destroy();

            current = default;
            previous = default;
        }
    }
}
