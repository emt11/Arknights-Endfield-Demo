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
        private ActorId actorId;
        private IDisposable subscription, deathSubscription;
        private bool started;

        // 让本地输入丢弃目标禁用期间及重新启用前已采到的按键
        internal event Action ActivationChanged;

        private void Awake()
        {
            actor = GetComponent<Combatant>();
            control = GetComponent<CharacterControl.CharacterControl>();
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
            control.Initialize(actorId);
            deathSubscription = actor.Runtime.Life.SubscribeDied(_ => StopAction());
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
                StopAction();
                Unsubscribe();
                return;
            }

            if (actor.Runtime == null || actor.Runtime.Life.State != ActorLifeState.Alive)
            {
                StopAction();
                return;
            }
            if (step.Delta <= 0d)
            {
                control.DiscardPresses();
                return;
            }
            control.Step(step.Delta);
        }

        private void StopAction()
        {
            control.InterruptAction();
            control.DiscardPresses();
        }

        private void OnDisable()
        {
            Unsubscribe();
            control.ClearControlInput();
            ActivationChanged?.Invoke();
        }

        private void OnDestroy() => deathSubscription?.Dispose();

        private void Unsubscribe()
        {
            subscription?.Dispose();
            subscription = null;
        }
    }
}
