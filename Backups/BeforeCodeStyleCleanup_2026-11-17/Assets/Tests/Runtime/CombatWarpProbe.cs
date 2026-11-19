#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Endfield.Character;
using Endfield.Combat;
using Endfield.Controller;
using UnityEngine;

namespace Endfield.Validation
{
    [DefaultExecutionOrder(1000)]
    public sealed class CombatWarpProbe : MonoBehaviour
    {
        public static bool Completed { get; private set; }
        public static string Failure { get; private set; }
        public static int Assertions { get; private set; }
        private CombatSimulation simulation;
        private Combatant source;
        private Combatant target;
        private ActionDefinition action;
        private readonly List<string> checks = new List<string>();
        private readonly Vector3 origin = new Vector3(-20f, .02f, -20f);

        private void Start()
        {
            Completed = false;
            Failure = null;
            Assertions = 0;
            try
            {
                simulation = FindFirstObjectByType<CombatSimulation>();
                simulation.enabled = false;
                PlayerController player = FindFirstObjectByType<PlayerController>();
                source = player.Character;
                player.enabled = false;
                foreach (Combatant unit in simulation.Units)
                {
                    if (unit != source)
                        target = unit;
                }

                action = Instantiate(source.Actions.BasicAttack);
                action.HitWindows = Array.Empty<HitWindow>();
                action.WarpWindows = new[] { new MotionWarpWindow { Start = 0f, End = .6f, MaxSpeed = 100f, MaxYawSpeed = 10000f } };
                ValidateTrajectories();
                ValidateLimitsAndLifecycle();
            }
            catch (Exception exception)
            {
                Failure = exception.ToString();
            }
            finally
            {
                if (source != null && source.Actions.Current != null)
                    source.Actions.Cancel(source.Actions.Current.ExecutionId, ActionEndReason.Cancelled);
                if (action != null)
                    Destroy(action);
            }

            Completed = true;
            File.WriteAllText("Logs/CombatWarpValidation.txt", Failure == null ? "PASS P1-08: " + Assertions + " checks: " + string.Join("; ", checks) : "FAIL " + Failure);
        }

        private void Place(Vector3 targetOffset)
        {
            if (source.Actions.Current != null)
                source.Actions.Cancel(source.Actions.Current.ExecutionId, ActionEndReason.Cancelled);
            CharacterController controller = source.GetComponent<CharacterController>();
            controller.enabled = false;
            source.transform.SetPositionAndRotation(origin, Quaternion.identity);
            controller.enabled = true;
            source.Motor.ResetMotion();
            target.transform.position = origin + targetOffset;
            Physics.SyncTransforms();
        }

        private ActionRuntime Begin(Combatant selected)
        {
            Require(source.Actions.TryStart(action, selected, out long id), "warp action submits");
            source.Actions.ProcessInitialPoints(id);
            return source.Actions.Current;
        }

        private void To(float end, float frame = 1f / 60f)
        {
            int remaining = 1000;
            while (source.Actions.Current.LocalTime < end - .000001f && remaining-- > 0)
            {
                ActionRuntime runtime = source.Actions.Current;
                int before = source.Motor.MovementSubmissions;
                simulation.AdvanceFrame(Mathf.Min(frame, (end - runtime.LocalTime) / runtime.PlaybackRate), false);
                if (source.Motor.MovementSubmissions - before != simulation.LastStepCount)
                    throw new InvalidOperationException("Warp duplicated Motor movement");
            }
            if (remaining <= 0)
                throw new InvalidOperationException("Warp simulation did not reach requested time");
        }

        private void ValidateTrajectories()
        {
            foreach (float fps in new[] { 30f, 60f, 120f })
            {
                foreach (float rate in new[] { .5f, 1f, 3f })
                {
                    Place(new Vector3(.8f, 0f, 2.2f));
                    ActionRuntime runtime = Begin(target);
                    runtime.PlaybackRate = rate;
                    To(.6f, 1f / fps);
                    Vector3 error = source.transform.position - runtime.Warp.LockedPosition;
                    error.y = 0f;
                    Require(error.magnitude < .05f, "endpoint at " + fps + " FPS step and rate " + rate);
                    float yaw = Quaternion.LookRotation(runtime.Warp.Direction).eulerAngles.y;
                    Require(Mathf.Abs(Mathf.DeltaAngle(source.transform.eulerAngles.y, yaw)) < 3f, "yaw at " + fps + " FPS step and rate " + rate);
                }
            }

            Place(new Vector3(0f, 0f, .5f));
            Begin(target);
            To(.6f);
            Require((source.transform.position - origin).z < .01f, "close target compresses forward motion to zero");

            Place(Vector3.forward * 2f);
            Begin(null);
            To(.6f);
            Require(Mathf.Abs((source.transform.position - origin).z - action.RootMotion.Sample(.6f).Position.z) < .01f, "no target uses exactly one baked source trajectory");

            Place(Vector3.forward * 2f);
            ActionRuntime moving = Begin(target);
            To(.1f);
            Vector3 locked = moving.Warp.LockedPosition;
            target.transform.position += Vector3.right;
            To(.6f);
            Require(moving.Warp.LockedPosition == locked, "moving target does not retarget locked endpoint");

            foreach (Vector3 offset in new[] { Vector3.forward * 4f, Vector3.back * 2f, new Vector3(0f, 1f, 2f) })
            {
                Place(offset);
                Require(source.Targeting.SelectAttackTarget() == null, "reject far, rear or raised target " + offset);
            }
        }

        private void ValidateLimitsAndLifecycle()
        {
            Place(Vector3.forward * 2.5f);
            action.WarpWindows[0].MaxSpeed = .5f;
            ActionRuntime slow = Begin(target);
            To(.6f);
            Require((source.transform.position - origin).z <= .301f, "speed cap does not teleport to endpoint");
            action.WarpWindows[0].MaxSpeed = 100f;

            Place(Vector3.forward * 2.5f);
            ActionRuntime blocked = Begin(target);
            To(.1f);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Warp validation wall";
            wall.layer = 8;
            wall.transform.position = origin + new Vector3(0f, 1f, .85f);
            wall.transform.localScale = new Vector3(3f, 3f, .1f);
            Physics.SyncTransforms();
            To(.4f);
            Require(blocked.Warp.Blocked, "Motor collision disables remaining window translation");
            Require(source.transform.position.z < origin.z + .85f, "warping cannot cross wall");
            Vector3 stopped = source.transform.position;
            wall.SetActive(false);
            Physics.SyncTransforms();
            To(.6f);
            Require(Mathf.Abs(source.transform.position.z - stopped.z) < .01f, "removing wall does not catch up lost displacement");
            Destroy(wall);
            long oldId = blocked.ExecutionId;
            source.Actions.Cancel(oldId, ActionEndReason.Interrupted);
            Place(Vector3.forward * 2f);
            ActionRuntime next = Begin(target);
            source.Actions.Cancel(oldId, ActionEndReason.Cancelled);
            Require(source.Actions.Current == next && !next.Warp.Blocked && next.Warp.WindowIndex == -1, "new execution owns fresh warp state");

            action.WarpWindows[0].Start = .003f;
            action.WarpWindows[0].End = .009f;
            action.WarpWindows[0].MaxSpeed = 8f;
            Place(Vector3.forward * 2f);
            ActionRuntime shortWindow = Begin(target);
            To(1f / 60f);
            Require(shortWindow.Warp.WindowIndex == 0 && source.Motor.RequestedPlanarDelta.magnitude < .15f, "single step handles before, short warp and after segments");

            action.WarpWindows[0].Start = 0f;
            action.WarpWindows[0].End = .6f;
            Place(Vector3.forward * 2f);
            ActionRuntime losingTarget = Begin(target);
            To(.1f);
            target.Health.ApplyDamage(99999f);
            Vector3 previous = source.transform.position;
            To(.1f + 1f / 60f);
            Require(!losingTarget.Warp.TargetValid && Vector3.Distance(source.transform.position, previous) < .1f, "target death disables correction without a jump");
        }

        private void Require(bool passed, string message)
        {
            Assertions++;
            if (!passed)
                throw new InvalidOperationException(message);
            checks.Add(message);
        }
    }
}
#endif
