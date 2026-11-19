using Endfield.Character;
using UnityEngine;

namespace Endfield.Combat
{
    public sealed class TargetingController
    {
        private readonly Combatant owner;
        private readonly CombatSimulation simulation;

        public TargetingController(Combatant character, CombatSimulation combat)
        {
            owner = character;
            simulation = combat;
        }

        public Combatant SelectAttackTarget()
        {
            Combatant best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (Combatant unit in simulation.Units)
            {
                if (!CanAssist(unit))
                    continue;
                float distance = (unit.transform.position - owner.transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = unit;
                    bestDistance = distance;
                }
            }

            return best;
        }

        public bool IsEnemy(Combatant target) => target != null && target.isActiveAndEnabled && !target.Health.IsDead && target.Team != owner.Team;

        public bool CanAssist(Combatant target)
        {
            if (!IsEnemy(target))
                return false;
            Vector3 offset = target.transform.position - owner.transform.position;
            if (Mathf.Abs(offset.y) > .5f)
                return false;
            offset.y = 0f;
            return offset.magnitude <= 3.5f && Vector3.Angle(owner.transform.forward, offset) <= 60f && !Physics.Linecast(owner.transform.position + Vector3.up, target.transform.position + Vector3.up, simulation.Rules.WorldMask, QueryTriggerInteraction.Ignore);
        }

        public bool IsSafeStandingPosition(Vector3 position)
        {
            float radius = owner.BodyRadius;
            bool ground = Physics.Raycast(position + Vector3.up * .5f, Vector3.down, out RaycastHit hit, 1f, simulation.Rules.WorldMask, QueryTriggerInteraction.Ignore);
            bool obstructed = Physics.CheckCapsule(position + Vector3.up * (radius + .05f), position + Vector3.up * (1.8f - radius), radius, simulation.Rules.WorldMask, QueryTriggerInteraction.Ignore);
            return ground && Mathf.Abs(hit.point.y - position.y) <= .5f && !obstructed;
        }
    }
}
