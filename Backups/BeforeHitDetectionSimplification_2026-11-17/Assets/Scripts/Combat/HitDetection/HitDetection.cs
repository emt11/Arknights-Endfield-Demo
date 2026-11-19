using System;
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
        private Collider[] overlaps = new Collider[20];

        public void Collect(Combatant source, CombatRulesDefinition rules, List<HitRequest> requests)
        {
            ActionRuntime runtime = source.Actions.Current;
            if (runtime == null || runtime.LocalTime <= runtime.PreviousTime)
                return;
            if (source.Weapon == null || source.Weapon.Hitboxes.Count == 0)
                return;

            collected.Clear();
            foreach (HitWindow window in runtime.Definition.hitWindows)
            {
                float start = Mathf.Max(runtime.PreviousTime, window.start);
                float end = Mathf.Min(runtime.LocalTime, window.end);
                if (start >= end)
                    continue;

                float span = runtime.LocalTime - runtime.PreviousTime;
                foreach (int id in window.hitboxIds)
                {
                    Hitbox hitbox = source.Weapon.GetHitbox(id);
                    if (!hitbox.isActiveAndEnabled)
                        continue;

                    HitboxPose from = HitboxPose.Lerp(hitbox.StartPose, hitbox.EndPose, (start - runtime.PreviousTime) / span);
                    HitboxPose to = HitboxPose.Lerp(hitbox.StartPose, hitbox.EndPose, (end - runtime.PreviousTime) / span);
                    int segments = hitbox.GetSweepSegments(from, to);
                    for (int sample = 0; sample <= segments; sample++)
                    {
                        HitboxPose pose = HitboxPose.Lerp(from, to, sample / (float)segments);
                        hitbox.GetGeometry(pose, out Vector3 pointA, out Vector3 pointB, out float radius);
                        int count;
                        do
                        {
                            count = hitbox.shape == HitboxShape.Sphere
                                ? Physics.OverlapSphereNonAlloc(pointA, radius, overlaps, rules.hurtboxMask, QueryTriggerInteraction.Collide)
                                : Physics.OverlapCapsuleNonAlloc(pointA, pointB, radius, overlaps, rules.hurtboxMask, QueryTriggerInteraction.Collide);
                            if (count < overlaps.Length)
                                break;
                            Array.Resize(ref overlaps, overlaps.Length * 2);
                        } while (true);

                        for (int index = 0; index < count; index++)
                        {
                            Collider collider = overlaps[index];
                            Vector3 segment = pointB - pointA;
                            float weight = segment.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(collider.bounds.center - pointA, segment) / segment.sqrMagnitude) : 0f;
                            AddCandidate(collider, source, runtime, window, pointA + segment * weight, rules, requests);
                        }
                        Array.Clear(overlaps, 0, count);
                    }
                }
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

            Vector3 contact = collider.ClosestPoint(position);
            if (Physics.Linecast(source.HitOrigin, contact, rules.worldMask, QueryTriggerInteraction.Ignore))
                return;

            if (collected.Add((window.group, target.UnitId)))
                requests.Add(new HitRequest(source, target, runtime, window, contact));
        }
    }
}
