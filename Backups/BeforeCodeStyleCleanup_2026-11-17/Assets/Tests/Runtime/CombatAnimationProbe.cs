#if UNITY_EDITOR
using System;
using System.Collections;
using Endfield.Controller;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Endfield.Validation
{
    public sealed class CombatAnimationProbe : MonoBehaviour
    {
        public GameObject Model;
        public RuntimeAnimatorController Controller;
        public AnimationClip Clip;
        public static bool Completed { get; private set; }
        public static string Failure { get; private set; }
        public static int Assertions { get; private set; }
        public static float RootDistance { get; private set; }
        private PlayableGraph graph;
        private GameObject actor;

        private IEnumerator Start()
        {
            Completed = false;
            Failure = null;
            Assertions = 0;
            yield return null;
            IEnumerator validation = Validate();
            while (true)
            {
                bool running;
                try
                {
                    running = validation.MoveNext();
                }
                catch (Exception exception)
                {
                    Failure = exception.ToString();
                    break;
                }

                if (!running)
                    break;

                yield return validation.Current;
            }

            if (graph.IsValid())
                graph.Destroy();

            Destroy(actor);
            Completed = true;
        }

        private IEnumerator Validate()
        {
            actor = new GameObject("Animation validation actor");
            CharacterController body = actor.AddComponent<CharacterController>();
            body.center = Vector3.up;
            body.height = 2f;
            body.radius = 0.3f;
            body.minMoveDistance = 0f;
            CharacterMotor motor = actor.AddComponent<CharacterMotor>();
            GameObject model = Instantiate(Model, actor.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            Animator animator = model.GetComponent<Animator>();
            Require(animator != null && animator.avatar != null && animator.avatar.isValid, "Actual model Avatar is invalid");
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            ProbeRootMotion root = model.AddComponent<ProbeRootMotion>();
            graph = PlayableGraph.Create("Combat animation prerequisite");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimatorControllerPlayable locomotion = AnimatorControllerPlayable.Create(graph, Controller);
            AnimationClipPlayable action = AnimationClipPlayable.Create(graph, Clip);
            AnimationMixerPlayable mixer = AnimationMixerPlayable.Create(graph, 2);
            graph.Connect(locomotion, 0, mixer, 0);
            graph.Connect(action, 0, mixer, 1);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Model", animator);
            output.SetSourcePlayable(mixer);
            graph.Play();

            foreach (float weight in new[] { 0f, 0.5f, 1f })
            {
                mixer.SetInputWeight(0, 1f - weight);
                mixer.SetInputWeight(1, weight);
                locomotion.SetFloat("MoveAmount", 1f);
                locomotion.SetFloat("PlaybackRate", 1f);
                action.SetTime(0d);
                graph.Evaluate(0f);
                Vector3 poseBefore = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                for (int index = 0; index < 20; index++)
                    graph.Evaluate(1f / 60f);

                Vector3 poseAfter = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                Require(Vector3.Distance(poseBefore, poseAfter) > 0.0001f, "Mixer produced no pose change at weight " + weight);
            }

            mixer.SetInputWeight(0, 0f);
            mixer.SetInputWeight(1, 1f);
            foreach (float rate in new[] { 0.5f, 1f, 3f })
            {
                double start = 0.1d;
                double end = Math.Min(Clip.length, 1.25d);
                double localTime = start;
                action.SetTime(start);
                graph.Evaluate(0f);
                Vector3 total = Vector3.zero;
                while (localTime < end - 0.000001d)
                {
                    // 同一渲染帧最多求值四次，覆盖正式时间模型的补跑路径
                    for (int step = 0; step < 4 && localTime < end - 0.000001d; step++)
                    {
                        float delta = 1f / 60f;
                        double next = Math.Min(end, localTime + delta * rate);
                        action.SetSpeed((next - localTime) / delta);
                        int calls = root.Calls;
                        Vector3 before = actor.transform.position;
                        Vector3 modelLocal = model.transform.localPosition;
                        graph.Evaluate(delta);
                        Require(root.Calls == calls + 1, "OnAnimatorMove did not run once per Evaluate");
                        Require(Vector3.Distance(modelLocal, model.transform.localPosition) < 0.0001f, "Animator applied root motion to Transform");
                        Vector3 requested = root.Delta;
                        requested.y = 0f;
                        motor.ApplyMovement(requested, delta);
                        Vector3 actual = actor.transform.position - before;
                        actual.y = 0f;
                        Require(Vector3.Distance(requested, actual) < 0.001f, "Motor movement differs from one root delta");
                        Require(Math.Abs(action.GetTime() - next) < 0.00001d, "Playable and authoritative time differ");
                        total += requested;
                        localTime = next;
                    }

                    double frozenTime = action.GetTime();
                    yield return null;
                    Require(Math.Abs(action.GetTime() - frozenTime) < 0.00001d, "Manual graph advanced automatically");
                }

                Require(Math.Abs(action.GetTime() - end) < 0.00001d, "ClipEnd overshoot");
                Require(total.magnitude > 0.01f, "Actual GreatSword clip has no usable root movement");
                RootDistance = total.magnitude;
            }
        }

        private static void Require(bool condition, string message)
        {
            Assertions++;
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private void OnDestroy()
        {
            if (graph.IsValid())
                graph.Destroy();
        }
    }
}
#endif
