using System;
using Endfield.Actors;
using UnityEngine;

namespace Endfield.CombatSession
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Combatant))]
    public sealed class ActorStepBinding : MonoBehaviour
    {
        private CharacterControl.CharacterControl control;
        private ActorLifecycle life;
        private IDisposable deathSubscription;
        internal event Action ActivationChanged;

        private void Awake() => control = GetComponent<CharacterControl.CharacterControl>();

        internal void Bind(ActorRuntime actor)
        {
            control.Initialize(actor.Id);
            life = actor.Life;
            deathSubscription = life.SubscribeDied(_ => StopAction());
        }

        internal void Step(double actionDelta)
        {
            if (!this || !isActiveAndEnabled) return;
            if (life.State == ActorLifeState.Alive) control.Step(actionDelta);
            else control.DiscardPresses();
        }

        private void StopAction()
        {
            if (!control) return;
            control.InterruptAction();
            control.DiscardPresses();
        }

        internal void Unbind()
        {
            deathSubscription?.Dispose();
            deathSubscription = null;
            life = null;
            StopAction();
        }

        private void OnEnable() => ActivationChanged?.Invoke();

        private void OnDisable()
        {
            if (control) control.ClearControlInput();
            ActivationChanged?.Invoke();
        }

        private void OnDestroy() => Unbind();
    }
}
