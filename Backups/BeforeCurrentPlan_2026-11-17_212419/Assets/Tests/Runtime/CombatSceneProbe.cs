#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Endfield.Character;
using Endfield.Combat;
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
            Require(Quaternion.Angle(idle, leg.localRotation) > 2f, "locomotion pose changes across rendered frames");
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.W, Key.LeftShift));
            yield return new WaitForSecondsRealtime(.5f);
            Require(character.Motor.CurrentSpeed > character.Motor.WalkSpeed, "run reaches faster speed");
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            yield return new WaitForSecondsRealtime(.5f);
            Require(character.Motor.CurrentSpeed < .01f, "release stops locomotion");
            PlayerCameraTarget cameraTarget = FindFirstObjectByType<PlayerCameraTarget>();
            Require(Vector3.Distance(cameraTarget.transform.position, character.transform.position + Vector3.up * 1.5f) < .05f, "camera follows player");

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
                if (result.started)
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
                InputSystem.QueueStateEvent(Mouse.current, new MouseState());
                yield return null;
                Require(starts.Count == index + 1 && starts[index] == character.Definition.BasicAttacks[index], "live combo segment " + (index + 1));
            }

            while (character.Actions.Current != null)
                yield return null;
            Require(starts.Count == 3, "final strike completes without restarting");

            simulation.enabled = false;
            ActionDefinition effects = Instantiate(character.Actions.BasicAttack);
            effects.points = new[]
            {
                new ActionPoint { time = 0f, kind = ActionPointKind.ComboAdvance },
                new ActionPoint { time = effects.Duration, kind = ActionPointKind.ComboAdvance }
            };
            effects.effectWindows = new[]
            {
                new EffectWindow { start = 0f, end = .5f, effect = AssetDatabase.LoadAssetAtPath<ParticleEffectDefinition>("Assets/ScriptObjects/Presentation/ChargeGlow.asset") },
                new EffectWindow { start = .001f, end = .002f, effect = AssetDatabase.LoadAssetAtPath<ParticleEffectDefinition>("Assets/ScriptObjects/Presentation/ChargeGlow.asset") }
            };
            Require(character.Actions.TryStart(effects, null, out long id), "effect action submits");
            character.Actions.ProcessInitialPoints(id);
            ActionRuntime runtime = character.Actions.Current;
            Require(runtime.NextPoint == 1 && runtime.windowEffects[0].IsValid, "zero point and persistent effect enter once");
            simulation.AdvanceFrame(1f / 60f, false);
            Require(runtime.enteredEffects[1] && !runtime.windowEffects[1].IsValid, "full short effect window enters and exits");
            character.Actions.Cancel(id, ActionEndReason.Interrupted);
            Require(!runtime.windowEffects[0].IsValid && runtime.EndReason == ActionEndReason.Interrupted, "interruption returns owned effect and records reason");
            Destroy(effects);
            simulation.enabled = true;
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
