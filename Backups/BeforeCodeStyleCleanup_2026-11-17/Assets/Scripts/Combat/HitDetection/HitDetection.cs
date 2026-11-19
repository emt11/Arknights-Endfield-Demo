using System.Collections.Generic;
using Endfield.Character;
using UnityEngine;

namespace Endfield.Combat
{
    public readonly struct HitRequest
    {
        public readonly Combatant Source;
        public readonly Combatant Target;
        public readonly ActionRuntime Runtime;
        public readonly HitWindow Window;
        public readonly Vector3 Position;

        public HitRequest(Combatant source, Combatant target, ActionRuntime runtime, HitWindow window, Vector3 position)
        {
            Source = source;
            Target = target;
            Runtime = runtime;
            Window = window;
            Position = position;
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
            foreach (HitWindow window in runtime.Definition.HitWindows)
            {
                float start = Mathf.Max(runtime.PreviousTime, window.Start);
                float end = Mathf.Min(runtime.LocalTime, window.End);
                if (start >= end)
                    continue;

                Vector3 before = source.StepStartPosition + source.StepStartRotation * window.Offset;
                Vector3 after = source.transform.TransformPoint(window.Offset);
                float span = runtime.LocalTime - runtime.PreviousTime;
                Vector3 from = Vector3.Lerp(before, after, (start - runtime.PreviousTime) / span);
                Vector3 to = Vector3.Lerp(before, after, (end - runtime.PreviousTime) / span);
                foreach (Collider collider in Physics.OverlapSphere(from, window.Radius, rules.HurtboxMask, QueryTriggerInteraction.Collide))
                    AddCandidate(collider, source, runtime, window, from, rules, requests);

                foreach (Collider collider in Physics.OverlapSphere(to, window.Radius, rules.HurtboxMask, QueryTriggerInteraction.Collide))
                    AddCandidate(collider, source, runtime, window, to, rules, requests);

                Vector3 movement = to - from;
                if (movement.sqrMagnitude <= 0.000001f)
                    continue;

                foreach (RaycastHit hit in Physics.SphereCastAll(from, window.Radius, movement.normalized, movement.magnitude, rules.HurtboxMask, QueryTriggerInteraction.Collide))
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

            Vector3 origin = source.StepStartPosition + Vector3.up * window.Offset.y;
            Vector3 contact = collider.ClosestPoint(position);
            if (Physics.Linecast(origin, contact, rules.WorldMask, QueryTriggerInteraction.Ignore))
                return;

            if (collected.Add((window.Group, target.UnitId)))
                requests.Add(new HitRequest(source, target, runtime, window, contact));
        }
    }
}
