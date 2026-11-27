using System;
using System.Collections.Generic;
using Endfield.CombatContracts;
using Endfield.CombatSession;
using Endfield.Timing;
using UnityEngine;

namespace Endfield.Actors
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BattleTimeDriver))]
    [DefaultExecutionOrder(-10)]
    public sealed class SceneActors : MonoBehaviour
    {
        [SerializeField] private Combatant[] initialActors = Array.Empty<Combatant>();
        private readonly Dictionary<ActorId, Combatant> actors = new();
        private BattleClock clock;
        private uint nextId = 1;

        private void Awake() => clock = GetComponent<BattleTimeDriver>().Clock;

        private void Start()
        {
            foreach (var unit in initialActors) Register(unit);
        }

        public ActorRuntime Register(Combatant unit)
        {
            if (!unit || unit.gameObject.scene != gameObject.scene)
                throw new ArgumentException("登记对象必须是本场景的战斗单位", nameof(unit));
            if (unit.Runtime != null)
            {
                if (TryGet(unit.Runtime.Id, out var existing) && existing == unit) return unit.Runtime;
                throw new InvalidOperationException($"{unit.name} 已向其他单位表登记");
            }
            if (!unit.isActiveAndEnabled) throw new InvalidOperationException($"{unit.name} 首次登记时需要启用");

            var id = new ActorId(nextId);
            var binding = unit.GetComponent<ActorStepBinding>();
            if (!clock.RegisterActor(id, binding ? binding.Step : (Action<double>)null)) throw new InvalidOperationException($"动作时钟无法登记角色 ID {id.Value}");
            try
            {
                unit.Bind(this, id);
                if (binding) binding.Bind(unit.Runtime);
            }
            catch
            {
                if (unit.Runtime != null) unit.Unbind();
                clock.UnregisterActor(id);
                throw;
            }
            actors.Add(id, unit);
            nextId++;
            return unit.Runtime;
        }

        public bool TryGet(ActorId id, out Combatant unit) => actors.TryGetValue(id, out unit);

        public bool CanTargetEnemy(ActorId source, ActorId target) =>
            TryGet(source, out var attacker) && TryGet(target, out var defender) &&
            attacker.isActiveAndEnabled && defender.isActiveAndEnabled && attacker.Faction != defender.Faction &&
            attacker.Runtime.Life.State == ActorLifeState.Alive && defender.Runtime.Life.State == ActorLifeState.Alive;

        internal void Unregister(Combatant unit)
        {
            var id = unit.Runtime.Id;
            if (unit.TryGetComponent<ActorStepBinding>(out var binding)) binding.Unbind();
            actors.Remove(id);
            clock.UnregisterActor(id);
            unit.Unbind();
        }

        private void OnDestroy()
        {
            foreach (var unit in new List<Combatant>(actors.Values)) Unregister(unit);
        }
    }
}
