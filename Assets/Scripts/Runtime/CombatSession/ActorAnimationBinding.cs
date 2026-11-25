using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Endfield.CombatSession
{
    [DisallowMultipleComponent]
    public sealed class ActorAnimationBinding : MonoBehaviour
    {
        private static readonly int MoveAmount = Animator.StringToHash("MoveAmount");
        [SerializeField] private RuntimeAnimatorController locomotionController;
        [SerializeField, Min(.01f)] private float walkReferenceSpeed = 1.43936f;
        [SerializeField, Min(.01f)] private float runReferenceSpeed = 4.91111f;
        private Animator model;
        private PlayableGraph graph;
        private AnimatorControllerPlayable locomotion;

        internal void Initialize(Animator animator)
        {
            model = animator;
            graph = PlayableGraph.Create(name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            locomotion = AnimatorControllerPlayable.Create(graph, locomotionController);
            var output = AnimationPlayableOutput.Create(graph, "移动姿态", model);
            output.SetSourcePlayable(locomotion);
            graph.Play();
            if (isActiveAndEnabled && model.isActiveAndEnabled) graph.Evaluate(0f);
        }

        internal void Advance(float groundSpeed, double actionDelta)
        {
            if (!isActiveAndEnabled || !model.isActiveAndEnabled) return;
            float amount = groundSpeed <= walkReferenceSpeed
                ? Mathf.InverseLerp(0f, walkReferenceSpeed, groundSpeed)
                : 1f + Mathf.InverseLerp(walkReferenceSpeed, runReferenceSpeed, groundSpeed);
            locomotion.SetFloat(MoveAmount, amount);
            graph.Evaluate((float)actionDelta);
        }

        private void OnEnable()
        {
            if (graph.IsValid() && model.isActiveAndEnabled) graph.Evaluate(0f);
        }

        private void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
