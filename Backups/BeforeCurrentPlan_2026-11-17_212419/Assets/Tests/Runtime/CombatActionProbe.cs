#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Endfield.Character;
using Endfield.Combat;
using Endfield.Controller;
using UnityEngine;

namespace Endfield.Validation
{
    [DefaultExecutionOrder(1000)]
    public sealed class CombatActionProbe : MonoBehaviour
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
                ActionController actions = player.Actions;
                var results = new List<ActionResult>();
                actions.ResultPublished += results.Add;
                simulation.QueueAttack(200);
                int moves = player.Motor.MovementSubmissions;
                simulation.AdvanceFrame(1f / 60f, false);
                Require(actions.Current != null, "Attack input did not start action");
                Require(simulation.PlayerInputs.Count == 0, "Accepted attack input not consumed");
                Require(player.Motor.MovementSubmissions == moves + 1, "Motor did not commit exactly once");
                ActionRuntime first = actions.Current;
                Require(Math.Abs(player.Animation.ClipTime - first.Definition.clipStart - first.LocalTime) < 0.00001d, "Animation is not synchronized");
                Require(results.Count == 1 && results[0].started, "Start result not published exactly once");
                Require(!actions.TryStart(actions.BasicAttack, null, out _), "Attack restarted outside cancel window");
                actions.Cancel(first.ExecutionId, ActionEndReason.Cancelled);
                Require(actions.Current == null && first.Ended, "Cancelled action retained authority");
                Require(results.Count == 1, "Cancel result published synchronously");
                Require(actions.TryStart(actions.BasicAttack, null, out long secondId), "Repeated clip failed to start");
                actions.ProcessInitialPoints(secondId);
                actions.Cancel(first.ExecutionId, ActionEndReason.Interrupted);
                Require(actions.Current.ExecutionId == secondId, "Old ID cancelled new action");
                simulation.AdvanceFrame(1f / 60f, false);
                Require(results.Count == 3 && !results[1].started && results[2].started, "Replacement result order is wrong");
                ActionRuntime second = actions.Current;
                second.PlaybackRate = 3f;
                int limit = 500;
                while (actions.Current != null && limit-- > 0)
                {
                    moves = player.Motor.MovementSubmissions;
                    simulation.AdvanceFrame(1f / 60f, false);
                    Require(player.Motor.MovementSubmissions == moves + 1, "Movement committed more than once");
                }

                Require(limit > 0 && second.Ended, "Action failed to complete");
                Require(second.NextPoint == second.Definition.points.Length, "Action skipped points including endpoint");
                Require(Math.Abs(player.Animation.ClipTime - second.Definition.clipEnd) < 0.0001d, "Final pose overshot clip end");
                Require(results.Count == 4 && results[3].reason == ActionEndReason.Completed, "Completion result missing or duplicated");
                Vector3 position = player.transform.position;
                simulation.AdvanceFrame(0.5f, true);
                Require(Vector3.Distance(position, player.transform.position) < 0.00001f, "Paused character moved");
                actions.ResultPublished -= results.Add;
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
