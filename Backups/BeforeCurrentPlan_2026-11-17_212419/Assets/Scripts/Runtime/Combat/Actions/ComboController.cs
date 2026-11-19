using System;
using Endfield.Character;

namespace Endfield.Combat
{
    public sealed class ComboController
    {
        private readonly Combatant owner;

        public ComboController(Combatant character) => owner = character;

        public int CurrentIndex => owner.Actions.Current == null ? -1 : Array.IndexOf(owner.Definition.BasicAttacks, owner.Actions.Current.Definition);

        public bool TryAttack(out long executionId)
        {
            executionId = 0;
            ActionRuntime current = owner.Actions.Current;
            int index = 0;
            if (current != null)
            {
                int previous = CurrentIndex;
                if (previous < 0 || !current.ComboAdvanced)
                    return false;
                index = previous + 1;
            }

            // 末段不自动绕回首段，下一轮必须在动作结束后重新起手
            if (index >= owner.Definition.BasicAttacks.Length)
                return false;

            return owner.Actions.TryStart(owner.Definition.BasicAttacks[index], owner.Targeting.SelectAttackTarget(), out executionId);
        }
    }
}
