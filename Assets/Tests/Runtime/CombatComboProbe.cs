using System.Linq;
#if UNITY_EDITOR
using System;
using Endfield.Character;
using Endfield.Combat;
using Endfield.Controller;
using UnityEngine;

namespace Endfield.Validation
{
    [DefaultExecutionOrder(1000)]
    public sealed class CombatComboProbe : MonoBehaviour
    {
        public static bool Completed { get; private set; }
        public static string Failure { get; private set; }
        public static int Assertions { get; private set; }

        private void Start()
        {
            Completed = false;
            Failure = null;
            Assertions = 0;
            try
            {
                CombatSimulation simulation = FindFirstObjectByType<CombatSimulation>();
                simulation.enabled = false;
                Combatant player = FindFirstObjectByType<PlayerController>().Character;
                simulation.QueueAttack(300);
                simulation.AdvanceFrame(1f / 60f, false);
                Require(player.Combo.CurrentIndex == 0, "First attack is not first combo entry");
                long firstId = player.Actions.Current.ExecutionId;
                simulation.QueueAttack(301);
                for (int step = 0; step < 16; step++)
                    simulation.AdvanceFrame(1f / 60f, false);

                Require(player.Actions.Current.ExecutionId == firstId && simulation.PendingInputCount == 0, "Early input failed to expire");
                int count = player.Definition.BasicAttacks.Count;
                for (int press = 1; press <= count; press++)
                {
                    ActionRuntime current = player.Actions.Current;
                    CancelWindow attackWindow = current.Definition.animation.windowEvents.OfType<CancelWindow>().First(window => (window.categories & ActionCategory.Attack) != 0);
                    float readyAt = attackWindow.start;
                    while (player.Actions.Current == current && current.LocalTime < readyAt - 0.05f)
                        simulation.AdvanceFrame(1f / 60f, false);

                    simulation.QueueAttack(301 + press);
                    int timeout = 20;
                    while (player.Actions.Current == current && timeout-- > 0)
                        simulation.AdvanceFrame(1f / 60f, false);

                    Require(timeout > 0 && player.Combo.CurrentIndex == press % count, "Buffered combo did not advance or wrap");
                    Require(current.ComboAdvanced && (current.LocalTime >= attackWindow.start && current.LocalTime < attackWindow.end), "Transition outside legal window");
                    Require(current.EndReason == ActionEndReason.Cancelled && player.Actions.Current.ExecutionId != current.ExecutionId
                        && simulation.PendingInputCount == 0, "Transition must replace the current execution and consume its input");
                }

                ActionRuntime last = player.Actions.Current;
                int remaining = 400;
                while (player.Actions.Current != null && remaining-- > 0)
                    simulation.AdvanceFrame(1f / 60f, false);

                Require(remaining > 0 && last.EndReason == ActionEndReason.Completed, "Wrapped attack never completed");
                simulation.AdvanceFrame(0.05f, false);
                Require(player.Actions.Current == null, "Consumed combo input started another chain");
                simulation.QueueAttack(400);
                simulation.AdvanceFrame(1f / 60f, false);
                Require(player.Combo.CurrentIndex == 0 && player.Actions.Current.ExecutionId != firstId, "New press did not start a new chain");
            }
            catch (Exception exception)
            {
                Failure = exception.ToString();
            }

            Completed = true;
        }

        private static void Require(bool condition, string message)
        {
            Assertions++;
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
#endif
