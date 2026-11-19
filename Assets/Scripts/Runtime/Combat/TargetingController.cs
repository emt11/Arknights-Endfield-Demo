using System;
using Endfield.Character;
using UnityEngine;

namespace Endfield.Combat
{
    public sealed class TargetingController
    {
        private readonly Combatant owner;
        private readonly CombatSimulation simulation;
        private readonly CharacterController controller;

        internal Func<Vector3, Vector3, bool> CheckLineOfSight { get; }
        internal Func<Vector3, bool> CheckStandingPosition { get; }

        public TargetingController(Combatant character, CombatSimulation combat)
        {
            owner = character;
            simulation = combat;
            controller = character.GetComponent<CharacterController>();
            CheckLineOfSight = HasLineOfSight;
            CheckStandingPosition = IsSafeStandingPosition;
        }

        public Combatant SelectAttackTarget()
        {
            Combatant best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (Combatant unit in simulation.Units)
            {
                if (!IsEligibleForAttackAssist(unit))
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

        public bool IsEligibleForAttackAssist(Combatant target)
        {
            if (!IsEnemy(target))
                return false;
            Vector3 from = owner.transform.position;
            Vector3 to = target.transform.position;
            return IsWithinAssistance(to - from) && HasLineOfSight(from, to);
        }

        /// <summary>
        /// 水平距离在 3.5 米以内，高差在 ±0.5 米以内，范围是水平 360°
        /// </summary>
        public static bool IsWithinAssistance(Vector3 offset) =>
            Mathf.Abs(offset.y) <= .5f && offset.x * offset.x + offset.z * offset.z <= 3.5f * 3.5f;

        /// <summary>
        /// 双方位置各抬高 1 米后，这两个点之间的直线有没有被障碍物挡住
        /// </summary>
        private bool HasLineOfSight(Vector3 from, Vector3 to) =>
            !Physics.Linecast(from + Vector3.up, to + Vector3.up, simulation.Rules.worldMask, QueryTriggerInteraction.Ignore);

        public bool IsSafeStandingPosition(Vector3 position)
        {
            int mask = simulation.Rules.worldMask;
            if (!Physics.Raycast(position + Vector3.up * .5f, Vector3.down, 1f, mask, QueryTriggerInteraction.Ignore))
                return false;

            Transform root = controller.transform;
            Vector3 scale = root.lossyScale;
            float radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfSegment = Mathf.Max(0f, controller.height * Mathf.Abs(scale.y) * .5f - radius);
            Vector3 center = position + root.TransformVector(controller.center);
            Vector3 top = center + root.up * halfSegment;
            // 底端留出地面接触余量，顶端仍使用控制器的实际高度
            Vector3 bottom = center - root.up * (halfSegment - Mathf.Min(.05f, 2f * halfSegment));
            return !Physics.CheckCapsule(bottom, top, radius, mask, QueryTriggerInteraction.Ignore);
        }
    }
}
