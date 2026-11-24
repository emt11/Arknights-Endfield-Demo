using System;
using Endfield.Actors;
using Endfield.Animation;
using Endfield.CombatContracts;
using Endfield.Controller;
using Endfield.Timing;
using UnityEngine;

namespace Endfield.CombatSession
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Combatant))]
    public sealed class ActorAnimationBinding : MonoBehaviour
    {
        [SerializeField] private BattleTimeDriver timeDriver;
        [SerializeField] private RuntimeAnimatorController locomotionController;
        [SerializeField] private float walkReferenceSpeed = 1.43936f;
        [SerializeField] private float runReferenceSpeed = 4.91111f;
        private Combatant unit;
        private ThirdPersonController player;
        private ActorId actorId;
        private CharacterAnimationGraph graph;
        private IDisposable subscription;

        public ICharacterAnimationDriver Driver => graph;

        private void Start()
        {
            unit = GetComponent<Combatant>();
            player = GetComponent<ThirdPersonController>();
            if (unit.Runtime == null || !timeDriver || timeDriver.Clock == null)
                throw new InvalidOperationException($"{name} 缺少已登记角色或战斗时钟");
            actorId = unit.Runtime.Id;
            graph = new CharacterAnimationGraph(unit.ModelAnimator, locomotionController, walkReferenceSpeed, runReferenceSpeed);
            Subscribe();
        }

        private void OnEnable()
        {
            if (graph == null) return;
            if (unit.Runtime == null || !timeDriver || !timeDriver.Clock.TryGetActionTime(actorId, out _))
            {
                Release();
                return;
            }
            graph.SamplePose();
            Subscribe();
        }

        private void Subscribe() => subscription = timeDriver.Clock.SubscribeBattleStep(OnBattleStep);

        private void OnBattleStep(TimeStep battleStep)
        {
            if (!timeDriver.Clock.TryGetActionTime(actorId, out var action))
            {
                Release();
                return;
            }
            if (player && player.isActiveAndEnabled) player.Step(graph, action.Delta);
            graph.Advance(action.Delta);
        }

        private void OnDisable()
        {
            subscription?.Dispose();
            subscription = null;
        }

        private void Release()
        {
            subscription?.Dispose();
            subscription = null;
            graph?.Dispose();
            graph = null;
        }

        private void OnDestroy() => Release();
    }
}
