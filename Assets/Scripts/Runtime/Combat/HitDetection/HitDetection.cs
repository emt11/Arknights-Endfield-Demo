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
            if (runtime == null || runtime.hitIntervals.Count == 0)
                return;
            if (source.Weapon == null || source.Weapon.Hitboxes.Count == 0)
                return;

            collected.Clear();
            float span = runtime.LocalTime - runtime.PreviousTime;
            foreach (var interval in runtime.hitIntervals)
            {
                HitWindow window = interval.Window;
                float start = interval.From;
                float end = interval.To;
                float startFraction = (start - runtime.PreviousTime) / span;
                float endFraction = (end - runtime.PreviousTime) / span;
                foreach (int id in window.hitboxIds)
                {
                    Hitbox hitbox = source.Weapon.GetHitbox(id);
                    if (!hitbox.isActiveAndEnabled)
                        continue;

                    IReadOnlyList<HitboxContact> contacts = hitbox.Query(startFraction, endFraction, rules.targetMask);
                    for (int index = 0; index < contacts.Count; index++)
                    {
                        HitboxContact contact = contacts[index];
                        Combatant target = contact.collider.GetComponentInParent<Combatant>();
                        if (target == null || target == source || target.Team == source.Team || target.Health.IsDead || !target.isActiveAndEnabled)
                            continue;
                        var key = (window.group, target.UnitId);
                        if (collected.Contains(key))
                            continue;
                        if (Physics.Linecast(source.HitOrigin, contact.position, rules.worldMask, QueryTriggerInteraction.Ignore))
                            continue;

                        collected.Add(key);
                        requests.Add(new HitRequest(source, target, runtime, window, contact.position));
                    }
                }
            }
        }
    }
}
