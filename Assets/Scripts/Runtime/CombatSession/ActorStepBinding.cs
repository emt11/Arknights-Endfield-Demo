using System;
using Endfield.Actors;
using Endfield.CombatContracts;
using Endfield.Timing;
using UnityEngine;

namespace Endfield.CombatSession
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Combatant))]
    public sealed class ActorStepBinding : MonoBehaviour
    {
        [SerializeField] private BattleTimeDriver timeDriver;
        private Combatant actor;
        private CharacterControl.CharacterControl control;
        private ActorAnimationBinding animationBinding;
        private ActorId actorId;
        private IDisposable subscription;
        private bool started;

        // 让本地输入丢弃目标禁用期间及重新启用前已采到的按键
        internal event Action ActivationChanged;

        private void Awake()
        {
            actor = GetComponent<Combatant>();
            control = GetComponent<CharacterControl.CharacterControl>();
            animationBinding = GetComponent<ActorAnimationBinding>();
        }

        private void Start()
        {
            started = true;
            BindClock();
        }

        private void OnEnable()
        {
            ActivationChanged?.Invoke();
            if (started) BindClock();
        }

        private void BindClock()
        {
            if (actorId.Value == 0 && actor.Runtime != null) InitializeActor();
            subscription = timeDriver.Clock.SubscribeBattleStep(OnBattleStep);
        }

        private void InitializeActor()
        {
            actorId = actor.Runtime.Id;
            control.Initialize();
            if (animationBinding) animationBinding.Initialize(actor.ModelAnimator);
        }

        private void OnBattleStep(TimeStep battleStep)
        {
            // 动态入场的实体可能在 Start 之后才经 SceneActors 登记
            if (actorId.Value == 0)
            {
                if (actor.Runtime == null) return;
                InitializeActor();
            }
            if (!timeDriver.Clock.TryGetActionTime(actorId, out var step))
            {
                Unsubscribe();
                return;
            }

            if (step.Delta <= 0d || actor.Runtime.Life.State != ActorLifeState.Alive)
            {
                control.DiscardPresses();
                return;
            }
            control.Step(step.Delta);
            if (animationBinding) animationBinding.Advance(control.GroundSpeed, step.Delta);
        }

        private void OnDisable()
        {
            Unsubscribe();
            control.ClearControlInput();
            ActivationChanged?.Invoke();
        }

        private void Unsubscribe()
        {
            subscription?.Dispose();
            subscription = null;
        }
    }
}
