using System;
using Endfield.Character;
using UnityEngine;

namespace Endfield.Combat
{
    public readonly struct HitResult
    {
        public readonly long ExecutionId;
        public readonly int SourceId;
        public readonly int TargetId;
        public readonly float Damage;
        public readonly Vector3 Position;

        public HitResult(HitRequest request, float damage)
        {
            ExecutionId = request.Runtime.ExecutionId;
            SourceId = request.Source.UnitId;
            TargetId = request.Target.UnitId;
            Damage = damage;
            Position = request.Position;
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
            Combatant source = request.Source;
            Combatant target = request.Target;
            bool sourceInvalid = source == null || !source.isActiveAndEnabled || source.Health.IsDead ||
                source.Actions.Current != request.Runtime || request.Runtime.Ended;
            if (sourceInvalid || target == null || !target.isActiveAndEnabled || target.Health.IsDead || target.Team == source.Team)
                return false;

            if (!request.Runtime.Hits.Add((request.Window.Group, target.UnitId)))
                return false;

            float damage = DamageCalculator.Calculate(source.Stats.Attack, request.Window.DamageMultiplier, target.Stats.Defense, simulation.Rules.DefenseConstant);
            float applied = target.Health.ApplyDamage(damage);
            var result = new HitResult(request, applied);
            simulation.Defer(() => HitResolved?.Invoke(result));
            return true;
        }
    }
}
