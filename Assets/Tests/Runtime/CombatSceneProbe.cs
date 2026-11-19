#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Endfield.Character;
using Endfield.AnimationSystem;
using Endfield.Combat;
using Endfield.Pooling;
using Endfield.Controller;
using Endfield.Presentation;
using Endfield.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Endfield.Validation
{
    [DefaultExecutionOrder(1000)]
    public sealed class CombatSceneProbe : MonoBehaviour
    {
        public static bool Completed { get; private set; }
        public static string Failure { get; private set; }
        public static int Assertions { get; private set; }
        private readonly List<string> checks = new List<string>();
        private CombatSimulation simulation;
        private PlayerController player;
        private GameInputManager input;

        private IEnumerator Start()
        {
            Completed = false;
            Failure = null;
            Assertions = 0;
            simulation = FindFirstObjectByType<CombatSimulation>();
            player = FindFirstObjectByType<PlayerController>();
            input = FindFirstObjectByType<GameInputManager>();
            IEnumerator run = Run();
            while (true)
            {
                bool next;
                try
                {
                    next = run.MoveNext();
                }
                catch (Exception exception)
                {
                    Failure = exception.ToString();
                    break;
                }

                if (!next)
                    break;
                yield return run.Current;
            }

            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            File.WriteAllText("Logs/CombatSceneValidation.txt", Failure == null ? "PASS P1-07: " + Assertions + " checks: " + string.Join("; ", checks) : "FAIL " + Failure);
            Completed = true;
        }

        private IEnumerator Run()
        {
            yield return null;
            InputSystem.QueueStateEvent(Mouse.current, new MouseState().WithButton(MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            yield return null;
            Require(input.GameplayCaptured, "mouse click captures gameplay");

            Combatant character = player.Character;
            Vector3 start = character.transform.position;
            Animator animator = character.Animation.Model.GetComponent<Animator>();
            Transform leg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            Quaternion idle = leg.localRotation;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.W));
            yield return new WaitForSecondsRealtime(.7f);
            Require(Vector3.Distance(start, character.transform.position) > .4f, "ordinary Update input moves character");
            Require(character.Motor.CurrentState == LocomotionStateId.Walk, "movement intent selects Walk");
            Require(Quaternion.Angle(idle, leg.localRotation) > 2f, "locomotion pose changes across rendered frames");
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.W, Key.LeftShift));
            yield return new WaitForSecondsRealtime(.5f);
            Require(character.Motor.LocomotionSpeed > character.Motor.WalkSpeed, "run reaches faster speed");
            Require(character.Motor.CurrentState == LocomotionStateId.Run, "run intent selects Run");
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return new WaitForSecondsRealtime(.5f);
            Require(character.Motor.LocomotionSpeed < .01f, "release stops locomotion");
            Require(character.Motor.CurrentState == LocomotionStateId.Idle, "release selects Idle");
            character.Motor.enabled = false;
            character.Motor.enabled = true;
            Require(character.Motor.CurrentState == LocomotionStateId.Idle && character.Motor.LocomotionSpeed == 0f, "motor re-enable starts idle without residual speed");
            CustomThirdPersonCamera playerCamera = FindFirstObjectByType<CustomThirdPersonCamera>();
            Require(Vector3.Distance(playerCamera.transform.position + playerCamera.transform.forward * playerCamera.CurrentDistance, character.transform.position + Vector3.up * 1.5f) < .05f, "camera follows player");

            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.C));
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return null;
            CharacterStatsPanelUI panel = FindFirstObjectByType<CharacterStatsPanelUI>(FindObjectsInactive.Include);
            Require(panel.IsOpen && !input.GameplayCaptured, "C opens existing stats panel and releases gameplay");
            double pausedAt = simulation.Now;
            yield return new WaitForSecondsRealtime(.1f);
            Require(simulation.Now == pausedAt, "panel pauses battle clock");
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Escape));
            yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return null;
            Require(!panel.IsOpen && input.GameplayCaptured, "Escape closes panel and restores capture");

            var starts = new List<ActionDefinition>();
            character.Actions.ResultPublished += result =>
            {
                if (result.Started)
                    starts.Add(result.definition);
            };
            InputSystem.QueueStateEvent(Mouse.current, new MouseState().WithButton(MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            yield return null;
            Require(character.Actions.Current != null && starts.Count == 1, "one input edge starts one action through normal Update");
            for (int index = 1; index < 3; index++)
            {
                while (character.Actions.Current != null && !character.Actions.CanCancel(ActionCategory.Attack))
                    yield return null;
                InputSystem.QueueStateEvent(Mouse.current, new MouseState().WithButton(MouseButton.Left));
                yield return null;
                if (index == 1)
                {
                    InputSystem.QueueStateEvent(Mouse.current, new MouseState());
                    yield return null;
                }
                Require(starts.Count == index + 1 && starts[index] == character.Definition.BasicAttacks[index], "live combo segment " + (index + 1));
            }

            ActionRuntime finisher = character.Actions.Current;
            while (character.Actions.Current != null)
                yield return null;
            Require(starts.Count == 3 && finisher.EndReason == ActionEndReason.Completed, "holding the third press lets the finisher complete");

            simulation.enabled = false;
            IEnumerator directions = CheckAttackDirections(character, playerCamera);
            while (directions.MoveNext())
                yield return directions.Current;
            ActionDefinition effects = Instantiate(character.Definition.BasicAttacks[0]);
            effects.animation = Instantiate(effects.animation);
            effects.animation.pointEvents = new[]
            {
                new ComboAdvancePoint { time = 0f },
                new ComboAdvancePoint { time = effects.Duration }
            };
            effects.animation.windowEvents = new[]
            {
                new EffectWindow { start = 0f, end = .5f, effect = AssetDatabase.LoadAssetAtPath<ParticleEffectDefinition>("Assets/ScriptObjects/Presentation/ChargeGlow.asset") },
                new EffectWindow { start = .001f, end = .002f, effect = AssetDatabase.LoadAssetAtPath<ParticleEffectDefinition>("Assets/ScriptObjects/Presentation/ChargeGlow.asset") }
            };
            Require(character.Actions.TryStart(effects, null, out long id), "effect action submits");
            character.Actions.ProcessInitialPoints(id);
            ActionRuntime runtime = character.Actions.Current;
            var handle = (PoolHandle)runtime.Playback.windows[0].state;
            Require(runtime.Playback.NextPoint == 1 && handle.IsValid, "zero point and persistent effect enter once");
            simulation.AdvanceFrame(1f / 60f, false);
            Require(runtime.Playback.windows[1].entered && !runtime.Playback.windows[1].active, "full short effect window enters and exits");
            character.Actions.End(id, ActionEndReason.Interrupted);
            Require(!handle.IsValid && runtime.EndReason == ActionEndReason.Interrupted, "interruption returns owned effect and records reason");
            Destroy(effects.animation);
            Destroy(effects);
            simulation.enabled = true;
        }

        private IEnumerator CheckAttackDirections(Combatant character, CustomThirdPersonCamera playerCamera)
        {
            Combatant dummy = simulation.Units.First(unit => unit != character);
            var body = character.GetComponent<CharacterController>();
            body.enabled = false;
            character.transform.SetPositionAndRotation(new Vector3(-20f, .02f, -20f), Quaternion.identity);
            body.enabled = true;
            dummy.transform.position = character.transform.position + Vector3.forward * 10f;
            Physics.SyncTransforms();

            var click = new MouseState().WithButton(MouseButton.Left);
            const float Step = 1f / 60f;
            foreach (Key key in new[] { Key.A, Key.D })
            {
                InputSystem.QueueStateEvent(Mouse.current, new MouseState());
                yield return null;
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key));
                InputSystem.QueueStateEvent(Mouse.current, click);
                yield return null;
                Quaternion before = character.transform.rotation;
                simulation.SubmitPlayerAction();
                Vector3 expected = playerCamera.PlanarRotation * (key == Key.D ? Vector3.right : Vector3.left);
                Require(character.Actions.Current != null && character.Actions.Current.Target == null
                    && Quaternion.Angle(character.transform.rotation, before) < .1f, "attack keeps its first-frame facing for " + key);
                float largestStep = 0f;
                while (character.Actions.Current.LocalTime < .4f)
                {
                    Quaternion previous = character.transform.rotation;
                    simulation.AdvanceFrame(Step, false);
                    largestStep = Mathf.Max(largestStep, Quaternion.Angle(previous, character.transform.rotation));
                }
                Require(largestStep > 0f && largestStep < 20f && Vector3.Angle(character.transform.forward, expected) < .2f,
                    "attack turns progressively into input " + key);
                character.Actions.End(character.Actions.Current.ExecutionId, ActionEndReason.Cancelled);
            }

            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            yield return null;
            Quaternion facing = character.transform.rotation;
            playerCamera.ApplyLook(new Vector2(450f, 0f));
            InputSystem.QueueStateEvent(Mouse.current, click);
            yield return null;
            simulation.SubmitPlayerAction();
            ActionRuntime first = character.Actions.Current;
            Require(first != null && Quaternion.Angle(character.transform.rotation, facing) < .1f, "camera-only yaw preserves stationary attack facing");
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            yield return null;

            CancelWindow window = first.Definition.animation.windowEvents.OfType<CancelWindow>().First(value => (value.categories & ActionCategory.Attack) != 0);
            while (character.Actions.Current == first && first.LocalTime < window.start - .05f)
                simulation.AdvanceFrame(Step, false);
            facing = character.transform.rotation;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.A));
            InputSystem.QueueStateEvent(Mouse.current, click);
            yield return null;
            simulation.SubmitPlayerAction();
            Require(character.Actions.Current == first && simulation.PendingInputCount == 1 && Quaternion.Angle(character.transform.rotation, facing) < .1f,
                "rejected attack preserves rotation, execution and buffered input");
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.D));
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            yield return null;
            while (character.Actions.Current == first && !character.Actions.CanCancel(ActionCategory.Attack))
                simulation.AdvanceFrame(Step, false);
            Require(character.Actions.CanCancel(ActionCategory.Move), "movement and attack share the acceptance step");
            simulation.SubmitPlayerAction();
            Vector3 right = playerCamera.PlanarRotation * Vector3.right;
            Require(character.Combo.CurrentIndex == 1 && Quaternion.Angle(character.transform.rotation, facing) < .1f
                && simulation.PendingInputCount == 0, "buffered attack wins without snapping");
            while (character.Actions.Current.LocalTime < .4f)
                simulation.AdvanceFrame(Step, false);
            Require(Vector3.Angle(character.transform.forward, right) < .2f, "buffered A to D uses direction at acceptance");
            character.Actions.End(character.Actions.Current.ExecutionId, ActionEndReason.Cancelled);

            Vector3 forward = playerCamera.PlanarRotation * Vector3.forward;
            dummy.transform.position = character.transform.position + forward * 2f;
            Physics.SyncTransforms();
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.S));
            InputSystem.QueueStateEvent(Mouse.current, click);
            yield return null;
            facing = character.transform.rotation;
            simulation.SubmitPlayerAction();
            ActionRuntime opposed = character.Actions.Current;
            Require(Vector3.Dot(player.WorldMove, forward) < -.99f && opposed.Target == dummy,
                "backward input acquires the dummy in front of the camera");
            Require(Quaternion.Angle(character.transform.rotation, facing) < .2f, "opposed input preserves first-frame facing");
            Vector3 startPosition = character.transform.position;
            while (opposed.LocalTime < opposed.Definition.animation.windowEvents.OfType<HitWindow>().First().start)
                simulation.AdvanceFrame(Step, false);
            Require(Vector3.Angle(character.transform.forward, forward) < 2f && opposed.Warp.translate
                && Vector3.Dot(character.transform.position - startPosition, forward) > .05f,
                "backward attack faces and approaches the front dummy by the first hit");
            character.Actions.End(opposed.ExecutionId, ActionEndReason.Cancelled);
            Vector3 neutralFacing = character.transform.forward;
            dummy.transform.position = character.transform.position - neutralFacing * 2f;
            Physics.SyncTransforms();
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            InputSystem.QueueStateEvent(Mouse.current, new MouseState());
            yield return null;
            InputSystem.QueueStateEvent(Mouse.current, click);
            yield return null;
            simulation.SubmitPlayerAction();
            ActionRuntime rear = character.Actions.Current;
            Require(rear.Target == dummy && Vector3.Angle(character.transform.forward, neutralFacing) < .2f, "neutral swing acquires rear target without snapping");
            simulation.AdvanceFrame(Step, false);
            Require(character.Motor.RequestedPlanarDelta.magnitude < .001f, "turn begins before approaching rear target");
            while (rear.LocalTime < rear.Definition.animation.windowEvents.OfType<HitWindow>().First().start)
                simulation.AdvanceFrame(Step, false);
            Vector3 toTarget = dummy.transform.position - character.transform.position;
            toTarget.y = 0f;
            Require(Vector3.Angle(character.transform.forward, toTarget) < 2f, "rear target faced by first hit");
            character.Actions.End(rear.ExecutionId, ActionEndReason.Cancelled);
            double cancelledTime = character.Animation.ClipTime;
            simulation.AdvanceFrame(Step, false);
            Require(character.Actions.Current == null && character.Animation.ClipTime > cancelledTime, "cancelled pose continues during fade");

            ActionDefinition finisher = character.Definition.BasicAttacks[2];
            Require(character.Actions.TryStart(finisher, null, out _), "start finisher recovery check");
            ActionRuntime recovering = character.Actions.Current;
            while (recovering.LocalTime < finisher.Duration - finisher.animation.easeOutDuration)
                simulation.AdvanceFrame(Step, false);
            Animator animator = character.Animation.Model.GetComponent<Animator>();
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Vector3 leftStart = left.position;
            Vector3 rightStart = rightFoot.position;
            float FootTravel() => Mathf.Max(
                Vector3.ProjectOnPlane(left.position - leftStart, Vector3.up).magnitude,
                Vector3.ProjectOnPlane(rightFoot.position - rightStart, Vector3.up).magnitude);

            float blendTravel = 0f;
            while (character.Actions.Current == recovering)
            {
                simulation.AdvanceFrame(Step, false);
                blendTravel = Mathf.Max(blendTravel, FootTravel());
            }
            Require(blendTravel < .05f, $"finisher ease-out foot excursion {blendTravel * 100f:F2} cm");
            leftStart = left.position;
            rightStart = rightFoot.position;
            float idleTravel = 0f;
            for (int frame = 0; frame < 30; frame++)
            {
                simulation.AdvanceFrame(Step, false);
                idleTravel = Mathf.Max(idleTravel, FootTravel());
            }
            Require(idleTravel < .05f, $"post-finisher idle foot excursion {idleTravel * 100f:F2} cm");
        }

        private void Require(bool condition, string message)
        {
            Assertions++;
            if (!condition)
                throw new InvalidOperationException(message);
            checks.Add(message);
        }
    }
}
#endif
