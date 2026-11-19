#if UNITY_EDITOR
using System;
using Endfield.Combat;
using UnityEngine;

namespace Endfield.Validation
{
    public sealed class CombatEntryProbe : MonoBehaviour
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
                var buffer = new CombatInputBuffer();
                buffer.Enqueue(CombatInputKind.Attack, 100, simulation.Now, 0.2f);
                buffer.Enqueue(CombatInputKind.Attack, 100, simulation.Now, 0.2f);
                Require(buffer.Count == 1, "One frame queued twice");
                simulation.AdvanceFrame(0.5f, false);
                Require(simulation.LastStepCount == 4, "Long frame exceeded step budget");
                Require(Math.Abs(simulation.Now - 4d / 60d) < 0.000001d, "Clock included discarded time");
                Require(Math.Abs(simulation.DiscardedTime - (0.5f - 4f / 60f)) < 0.000001f, "Discarded time is incorrect");
                Require(buffer.Count == 1, "Input was consumed without an accepted action");
                buffer.TryPeek(CombatInputKind.Attack, out CombatInput input);
                Require(!buffer.Consume(input.Sequence + 1), "Wrong request was consumed");
                Require(buffer.Consume(input.Sequence), "Accepted request not consumed");
                Require(!buffer.Consume(input.Sequence), "Request consumed twice");
                double before = simulation.Now;
                simulation.AdvanceFrame(1f / 60f, false);
                Require(simulation.LastStepCount == 1, "Discarded time carried to next frame");
                Require(Math.Abs(simulation.Now - before - 1d / 60d) < 0.000001d, "Unexpected clock increment");
                simulation.QueueAttack(101);
                before = simulation.Now;
                simulation.AdvanceFrame(1f, true);
                Require(simulation.Now == before && simulation.LastStepCount == 0, "Pause advanced clock");
                Require(simulation.PlayerInputs.Count == 0, "Pause retained combat input");
                buffer.Enqueue(CombatInputKind.Attack, 102, simulation.Now, 0.2f);
                for (int index = 0; index < 15; index++)
                    simulation.AdvanceFrame(1f / 60f, false);

                buffer.Expire(simulation.Now);
                Require(buffer.Count == 0, "Expired attack survived");
                before = simulation.Now;
                simulation.RequestHitStop(0.03f);
                simulation.AdvanceFrame(0.02f, false);
                Require(simulation.Now == before, "Hit stop advanced combat");
                simulation.AdvanceFrame(0.02f, false);
                Require(Math.Abs(simulation.Now - before - 0.01d) < 0.000001d, "Hit stop remainder discarded valid time");
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
