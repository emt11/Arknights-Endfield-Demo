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

                Require(player.Actions.Current.ExecutionId == firstId && simulation.PlayerInputs.Count == 0, "Early input failed to expire");
                for (int index = 1; index < 3; index++)
                {
                    ActionRuntime current = player.Actions.Current;
                    float readyAt = current.Definition.cancelWindows[0].start;
                    while (current.LocalTime < readyAt - 0.05f)
                        simulation.AdvanceFrame(1f / 60f, false);

                    simulation.QueueAttack(301 + index);
                    int timeout = 20;
                    while (player.Actions.Current == current && timeout-- > 0)
                        simulation.AdvanceFrame(1f / 60f, false);

                    Require(timeout > 0 && player.Combo.CurrentIndex == index, "Buffered combo did not advance");
                    Require(current.Ended && simulation.PlayerInputs.Count == 0, "Combo failed to clean previous action or consume input");
                }

                ActionRuntime last = player.Actions.Current;
                int remaining = 400;
                while (player.Actions.Current != null && remaining-- > 0)
                    simulation.AdvanceFrame(1f / 60f, false);

                Require(remaining > 0 && last.Ended, "Final attack never completed");
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
