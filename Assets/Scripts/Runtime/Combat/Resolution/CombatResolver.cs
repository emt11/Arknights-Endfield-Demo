using System;
using Endfield.Character;
using UnityEngine;

namespace Endfield.Combat
{
    public readonly struct HitResult
    {
        public readonly long executionId;
        public readonly int sourceId;
        public readonly int targetId;
        public readonly float damage;
        public readonly Vector3 position;

        public HitResult(HitRequest request, float damage)
        {
            executionId = request.runtime.ExecutionId;
            sourceId = request.source.UnitId;
            targetId = request.target.UnitId;
            this.damage = damage;
            position = request.position;
        }
    }

    public static class DamageCalculator
    {
        public static float Calculate(float attack, float multiplier, float defense, float defenseConstant) =>
            Mathf.Max(0f, attack * multiplier) * defenseConstant / (defenseConstant + Mathf.Max(0f, defense));
    }

    public sealed class CombatResolver
    {
        private readonly CombatSimulation simulation;

        public event Action<HitResult> HitResolved;

        public CombatResolver(CombatSimulation owner) => simulation = owner;

        public bool Resolve(HitRequest request)
        {
            Combatant source = request.source;
            Combatant target = request.target;
            bool sourceInvalid = source == null || !source.CanExecuteAction ||
                source.Actions.Current != request.runtime || request.runtime.Ended;
            if (sourceInvalid || target == null || !target.isActiveAndEnabled || target.Health.IsDead || target.Team == source.Team)
                return false;

            if (!request.runtime.hits.Add((request.window.group, target.UnitId)))
                return false;

            float damage = DamageCalculator.Calculate(source.Stats.Attack, request.window.damageMultiplier, target.Stats.Defense, simulation.Rules.defenseConstant);
            float applied = target.Health.ApplyDamage(damage);
            var result = new HitResult(request, applied);
            simulation.Defer(() => HitResolved?.Invoke(result));
            return true;
        }
    }
}
