using System;
using Endfield.Common;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Endfield.Animation
{
    public interface ICharacterAnimationDriver
    {
        AnimationClip CurrentClip { get; }
        double CurrentClipTime { get; }
        bool IsDodging { get; }
        void SetGroundSpeed(float metersPerSecond);
        bool TryPlay(AnimationClip clip, float speed, float fadeIn, float fadeOut);
        void Stop(float fadeOut);
        void PlayDodge(float attackFadeOut);
    }

    public sealed class CharacterAnimationGraph : ICharacterAnimationDriver, IDisposable
    {
        private static readonly int MoveAmount = Animator.StringToHash("MoveAmount");
        private static readonly int Dodge = Animator.StringToHash("Base Layer.Dodge");
        private readonly float walkSpeed;
        private readonly float runSpeed;
        private PlayableGraph graph;
        private readonly AnimatorControllerPlayable locomotion;
        private readonly AnimationMixerPlayable mixer;
        private AnimationClipPlayable attack;
        private AnimationClip clip;
        private float naturalFadeOut;
        private float weight;
        private float startWeight;
        private float targetWeight;
        private double fadeDuration;
        private double fadeElapsed;
        private bool enterDodge;

        public AnimationClip CurrentClip => targetWeight == 1f ? clip : null;
        public double CurrentClipTime => CurrentClip ? attack.GetTime() : 0;
        public bool IsDodging => locomotion.GetCurrentAnimatorStateInfo(0).fullPathHash == Dodge;

        public CharacterAnimationGraph(Animator animator, RuntimeAnimatorController controller, float walkSpeed, float runSpeed)
        {
            if (!animator || !animator.avatar || !animator.avatar.isValid || !animator.isHuman || !controller)
                throw new ArgumentException("需要有效的人形 Animator 和移动控制器");
            if (!(walkSpeed > 0 && runSpeed > walkSpeed && float.IsFinite(runSpeed)))
                throw new ArgumentOutOfRangeException(nameof(runSpeed), "步态标定须满足 0 < 走速 < 跑速");

            this.walkSpeed = walkSpeed;
            this.runSpeed = runSpeed;
            graph = PlayableGraph.Create(animator.name + " M4");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                locomotion = AnimatorControllerPlayable.Create(graph, controller);
                mixer = AnimationMixerPlayable.Create(graph, 2);
                graph.Connect(locomotion, 0, mixer, 0);
                SetWeight(0f);
                AnimationPlayableOutput.Create(graph, "角色姿态", animator).SetSourcePlayable(mixer);
                graph.Play();
                graph.Evaluate(0f);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void SetGroundSpeed(float metersPerSecond)
        {
            float amount = metersPerSecond <= walkSpeed
                ? Mathf.Clamp01(metersPerSecond / walkSpeed)
                : 1f + Mathf.InverseLerp(walkSpeed, runSpeed, metersPerSecond);
            locomotion.SetFloat(MoveAmount, amount);
        }

        public bool TryPlay(AnimationClip next, float speed, float fadeIn, float fadeOut)
        {
            if (!next || !next.humanMotion || next.isLooping || !(speed > 0 && float.IsFinite(speed))
                || !NumericValidation.IsFiniteInRange(fadeIn, 0, float.MaxValue)
                || !NumericValidation.IsFiniteInRange(fadeOut, 0, float.MaxValue)) return false;

            ReleaseAttack();
            clip = next;
            naturalFadeOut = fadeOut;
            attack = AnimationClipPlayable.Create(graph, next);
            attack.SetSpeed(speed);
            graph.Connect(attack, 0, mixer, 1);
            BeginFade(1f, fadeIn);
            return true;
        }

        public void Stop(float fadeOut)
        {
            NumericValidation.RequireFiniteInRange(fadeOut, 0, float.MaxValue, nameof(fadeOut));
            if (CurrentClip) BeginFade(0f, fadeOut);
        }

        public void PlayDodge(float attackFadeOut)
        {
            NumericValidation.RequireFiniteInRange(attackFadeOut, 0, float.MaxValue, nameof(attackFadeOut));
            locomotion.Play(Dodge, 0, 0f);
            enterDodge = true;
            if (clip) BeginFade(0f, attackFadeOut);
        }

        private void BeginFade(float target, float duration)
        {
            startWeight = weight;
            targetWeight = target;
            fadeElapsed = 0;
            fadeDuration = weight == target ? 0 : duration;
            SetWeight(fadeDuration == 0 ? target : startWeight);
            if (target == 0 && weight == 0) ReleaseAttack();
        }

        private void SetWeight(float value)
        {
            weight = value;
            mixer.SetInputWeight(0, 1f - value);
            mixer.SetInputWeight(1, value);
        }

        public void Advance(double actionDelta)
        {
            if (actionDelta <= 0) return;
            if (enterDodge)
            {
                // Play 后第一次求值只完成状态切换，先采样首姿再消费本步时间
                graph.Evaluate(0f);
                enterDodge = false;
            }
            while (actionDelta > 0 && clip)
            {
                double speed = attack.GetSpeed();
                double untilEnd = speed > 0 ? Math.Max(0, (clip.length - attack.GetTime()) / speed) : double.PositiveInfinity;
                double untilFade = fadeDuration > fadeElapsed ? fadeDuration - fadeElapsed : double.PositiveInfinity;
                double step = Math.Min(actionDelta, Math.Min(untilEnd, untilFade));

                if (step > 0)
                {
                    if (fadeElapsed < fadeDuration)
                    {
                        fadeElapsed = step >= untilFade ? fadeDuration : fadeElapsed + step;
                        SetWeight(Mathf.Lerp(startWeight, targetWeight, (float)(fadeElapsed / fadeDuration)));
                    }
                    graph.Evaluate((float)step);
                    actionDelta -= step;
                }

                if (step >= untilEnd)
                {
                    attack.SetTime(clip.length);
                    attack.SetSpeed(0);
                    if (targetWeight == 1f)
                    {
                        BeginFade(0f, naturalFadeOut);
                        // 恰在步末硬切到基础层时也要输出该姿态
                        if (!clip && actionDelta == 0) graph.Evaluate(0f);
                    }
                }
                if (clip && targetWeight == 0f && fadeElapsed >= fadeDuration) ReleaseAttack();
            }
            if (actionDelta > 0) graph.Evaluate((float)actionDelta);
        }

        public void SamplePose() => graph.Evaluate(0f);

        private void ReleaseAttack()
        {
            if (!attack.IsValid()) return;
            graph.Disconnect(mixer, 1);
            graph.DestroyPlayable(attack);
            attack = default;
            clip = null;
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
            clip = null;
        }
    }
}
