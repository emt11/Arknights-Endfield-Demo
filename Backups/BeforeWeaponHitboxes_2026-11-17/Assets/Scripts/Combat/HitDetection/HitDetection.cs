using System.Collections.Generic;
using Endfield.Character;
using UnityEngine;

namespace Endfield.Combat
{
    public readonly struct HitRequest
    {
        public readonly Combatant source;
        public readonly Combatant target;
        public readonly ActionRuntime runtime;
        public readonly HitWindow window;
        public readonly Vector3 position;

        public HitRequest(Combatant source, Combatant target, ActionRuntime runtime, HitWindow window, Vector3 position)
        {
            this.source = source;
            this.target = target;
            this.runtime = runtime;
            this.window = window;
            this.position = position;
        }
    }

    public sealed class HitDetection
    {
        private readonly HashSet<(int Group, int Target)> collected = new HashSet<(int, int)>();

        public void Collect(Combatant source, CombatRulesDefinition rules, List<HitRequest> requests)
        {
            ActionRuntime runtime = source.Actions.Current;
            if (runtime == null || runtime.LocalTime <= runtime.PreviousTime)
                return;

            collected.Clear();
            foreach (HitWindow window in runtime.Definition.hitWindows)
            {
                float start = Mathf.Max(runtime.PreviousTime, window.start);
                float end = Mathf.Min(runtime.LocalTime, window.end);
                if (start >= end)
                    continue;

                Vector3 before = source.StepStartPosition + source.StepStartRotation * window.offset;
                Vector3 after = source.transform.TransformPoint(window.offset);
                float span = runtime.LocalTime - runtime.PreviousTime;
                Vector3 from = Vector3.Lerp(before, after, (start - runtime.PreviousTime) / span);
                Vector3 to = Vector3.Lerp(before, after, (end - runtime.PreviousTime) / span);
                foreach (Collider collider in Physics.OverlapSphere(from, window.radius, rules.hurtboxMask, QueryTriggerInteraction.Collide))
                    AddCandidate(collider, source, runtime, window, from, rules, requests);

                foreach (Collider collider in Physics.OverlapSphere(to, window.radius, rules.hurtboxMask, QueryTriggerInteraction.Collide))
                    AddCandidate(collider, source, runtime, window, to, rules, requests);

                Vector3 movement = to - from;
                if (movement.sqrMagnitude <= 0.000001f)
                    continue;

                foreach (RaycastHit hit in Physics.SphereCastAll(from, window.radius, movement.normalized, movement.magnitude, rules.hurtboxMask, QueryTriggerInteraction.Collide))
                    AddCandidate(hit.collider, source, runtime, window, hit.point, rules, requests);
            }
        }

        private void AddCandidate(Collider collider, Combatant source, ActionRuntime runtime, HitWindow window, Vector3 position,
            CombatRulesDefinition rules, List<HitRequest> requests)
        {
            Hurtbox hurtbox = collider.GetComponent<Hurtbox>();
            if (hurtbox == null)
                return;

            Combatant target = hurtbox.Owner;
            if (target == source || target.Team == source.Team || target.Health.IsDead || !target.isActiveAndEnabled)
                return;

            Vector3 origin = source.StepStartPosition + Vector3.up * window.offset.y;
            Vector3 contact = collider.ClosestPoint(position);
            if (Physics.Linecast(origin, contact, rules.worldMask, QueryTriggerInteraction.Ignore))
                return;

            if (collected.Add((window.group, target.UnitId)))
                requests.Add(new HitRequest(source, target, runtime, window, contact));
        }
    }
}
