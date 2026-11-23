using System;
using Endfield.CombatContracts;
using Endfield.Common;
using Endfield.Resources;

namespace Endfield.Actors
{
    public enum ActorLifeState { Alive, Dead, Removed }

    public sealed class ActorLifecycle : IDisposable
    {
        private readonly ActorId id;
        private readonly CharacterResourceContainer resources;
        private readonly IDisposable resourceSubscription;
        private readonly ChangeNotifier<ActorId> deaths = new();

        public ActorLifeState State { get; private set; }

        public ActorLifecycle(ActorId id, CharacterResourceContainer resources)
        {
            this.id = id;
            this.resources = resources;
            State = resources.Snapshot.CurrentHP > 0 ? ActorLifeState.Alive : ActorLifeState.Dead;
            resourceSubscription = resources.Subscribe(OnResourcesChanged);
        }

        public IDisposable SubscribeDied(Action<ActorId> listener) => deaths.Subscribe(listener);

        public double RestoreHP(double finalHealing) =>
            State == ActorLifeState.Alive ? resources.RestoreHP(finalHealing) : 0;

        private void OnResourcesChanged(CharacterResourceSnapshot previous, CharacterResourceSnapshot current)
        {
            if (State != ActorLifeState.Alive || previous.CurrentHP <= 0 || current.CurrentHP > 0) return;
            State = ActorLifeState.Dead;
            deaths.Publish(id);
        }

        public void Dispose()
        {
            State = ActorLifeState.Removed;
            resourceSubscription.Dispose();
        }
    }
}
