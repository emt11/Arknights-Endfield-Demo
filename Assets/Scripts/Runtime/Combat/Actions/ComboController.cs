using Endfield.Character;
using UnityEngine;

namespace Endfield.Combat
{
    public sealed class ComboController
    {
        private readonly Combatant owner;

        public ComboController(Combatant character) => owner = character;

        public int CurrentIndex => owner.Actions.Current == null ? -1 : owner.Definition.IndexOfBasicAttack(owner.Actions.Current.Definition);

        public bool TryAttack(out long executionId, Vector3 moveDirection = default)
        {
            executionId = 0;
            int count = owner.Definition.BasicAttacks.Count;
            if (count == 0)
                return false;

            ActionRuntime current = owner.Actions.Current;
            int index = 0;
            if (current != null)
            {
                int previous = CurrentIndex;
                if (previous < 0 || !current.ComboAdvanced)
                    return false;
                index = (previous + 1) % count;
            }

            Combatant target = owner.Targeting.SelectAttackTarget();
            Vector3 facingDirection = target != null ? target.transform.position - owner.transform.position : moveDirection;
            return owner.Actions.TryStart(owner.Definition.BasicAttacks[index], target, out executionId, facingDirection);
        }
    }
}
