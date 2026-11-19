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
                // 当前动作不在普攻数组中 || 当前动作还没经过“允许连段推进”的时间点
                if (previous < 0 || !current.ComboAdvanced)
                    return false;
                index = previous + 1;
            }

            // 是否为连招最后一段
            if (index >= owner.Definition.BasicAttacks.Length)
                return false;

            // 尝试进行连招
            return owner.Actions.TryStart(owner.Definition.BasicAttacks[index], owner.Targeting.SelectAttackTarget(), out executionId);
        }
    }
}
