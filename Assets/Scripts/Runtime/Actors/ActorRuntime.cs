using Endfield.CharacterBuild;
using Endfield.CombatContracts;
using Endfield.Effects;
using Endfield.Resources;
using Endfield.Stats;
using Endfield.Status;

namespace Endfield.Actors
{
    public sealed class ActorRuntime
    {
        public ActorId Id { get; }
        public CharacterBuildState Build { get; }
        public IReadOnlyStatSet Stats { get; }
        public IReadOnlyCharacterResources Resources { get; }
        public EffectController Effects { get; }
        public StatusController Status { get; }
        public ActorLifecycle Life { get; }

        internal ActorRuntime(ActorId id, CharacterBuildState build, IReadOnlyStatSet stats,
            CharacterResourceContainer resources, EffectController effects, StatusController status)
        {
            Id = id;
            Build = build;
            Stats = stats;
            Resources = resources;
            Effects = effects;
            Status = status;
            Life = new ActorLifecycle(id, resources);
        }
    }
}
