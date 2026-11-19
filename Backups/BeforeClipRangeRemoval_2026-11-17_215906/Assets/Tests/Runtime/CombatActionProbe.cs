#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Endfield.Character;
using Endfield.Combat;
using Endfield.Controller;
using UnityEngine;
using UnityEditor;
using Endfield.Presentation;

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
                Require(!actions.TryStart(player.Definition.BasicAttacks[0], null, out _), "Attack restarted outside cancel window");
                actions.Cancel(first.ExecutionId, ActionEndReason.Cancelled);
                Require(actions.Current == null && first.Ended, "Cancelled action retained authority");
                Require(results.Count == 1, "Cancel result published synchronously");
                Require(actions.TryStart(player.Definition.BasicAttacks[0], null, out long secondId), "Repeated clip failed to start");
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
                ValidateLifecycle(simulation, player);
                ValidatePoints(simulation, player);
            }
            catch (Exception exception)
            {
                Failure = exception.ToString();
            }

            Completed = true;
        }

        private static void ValidateLifecycle(CombatSimulation simulation, Combatant player)
        {
            ActionController actions = player.Actions;
            ActionDefinition definition = Instantiate(player.Definition.BasicAttacks[0]);
            definition.hitWindows = Array.Empty<HitWindow>();
            definition.effectWindows = new[]
            {
                new EffectWindow
                {
                    start = 0f,
                    end = definition.Duration,
                    effect = AssetDatabase.LoadAssetAtPath<ParticleEffectDefinition>("Assets/ScriptObjects/Presentation/ChargeGlow.asset")
                }
            };
            foreach (Behaviour component in new Behaviour[] { actions, player.Animation, player.Motor, player })
            {
                Require(actions.TryStart(definition, null, out long id), "Lifecycle action starts");
                actions.ProcessInitialPoints(id);
                ActionRuntime runtime = actions.Current;
                Require(runtime.windowEffects[0].IsValid, "Lifecycle effect entered");
                component.enabled = false;
                Require(actions.Current == null && runtime.Ended && !runtime.windowEffects[0].IsValid, component.GetType().Name + " interrupts and returns effect");
                Require(!actions.TryStart(definition, null, out _), "Disabled dependency rejects start");
                if (component == actions)
                    player.Motor.SetIntent(Vector3.forward, false);
                int moves = player.Motor.MovementSubmissions;
                simulation.AdvanceFrame(1f / 60f, false);
                Require(runtime.LocalTime == 0f, "Disabled action does not advance");
                Require(player.Motor.MovementSubmissions == moves + (component == actions ? 1 : 0), "Disabled movement dependency is not simulated");
                if (component == actions)
                    Require(player.Motor.CurrentSpeed > 0f, "Disabled actions still allow ordinary movement");
                component.enabled = true;
                player.Motor.ResetMotion();
            }

            Require(actions.TryStart(definition, null, out long objectId), "Object disable action starts");
            actions.ProcessInitialPoints(objectId);
            ActionRuntime objectRuntime = actions.Current;
            player.gameObject.SetActive(false);
            Require(objectRuntime.Ended && !objectRuntime.windowEffects[0].IsValid, "Object disable clears effects");
            simulation.AdvanceFrame(1f / 60f, false);
            player.gameObject.SetActive(true);
            Require(actions.TryStart(definition, null, out long resumeId), "Re-enabled object accepts new execution");
            PlayerController controller = FindFirstObjectByType<PlayerController>();
            simulation.QueueAttack(300);
            controller.enabled = false;
            Require(simulation.PlayerInputs.Count == 0, "Player disable clears queued input");
            simulation.AdvanceFrame(1f / 60f, false);
            Require(actions.Current.ExecutionId == resumeId && actions.Current.LocalTime > 0f, "Player disable preserves running action");
            actions.Cancel(resumeId, ActionEndReason.Cancelled);
            simulation.QueueAttack(301);
            simulation.AdvanceFrame(1f / 60f, false);
            Require(actions.Current == null && simulation.PlayerInputs.Count == 0, "Disabled player does not consume stale attack");
            controller.enabled = true;

            Action<ActionResult> disableDuringPublish = result =>
            {
                if (result.started)
                    player.Animation.enabled = false;
            };
            actions.ResultPublished += disableDuringPublish;
            Require(actions.TryStart(definition, null, out long deferredId), "Deferred cleanup action starts");
            actions.ProcessInitialPoints(deferredId);
            ActionRuntime deferred = actions.Current;
            simulation.AdvanceFrame(1f / 60f, false);
            Require(actions.Current == deferred && !player.Animation.enabled, "Result callback defers cancellation");
            simulation.AdvanceFrame(1f / 60f, false);
            Require(deferred.Ended && actions.Current == null && !deferred.windowEffects[0].IsValid, "Queued cleanup succeeds after graph release");
            actions.ResultPublished -= disableDuringPublish;
            player.Animation.enabled = true;
            Destroy(definition);
        }

        private static void ValidatePoints(CombatSimulation simulation, Combatant player)
        {
            ActionDefinition definition = Instantiate(player.Definition.BasicAttacks[0]);
            AudioDefinition audio = ScriptableObject.CreateInstance<AudioDefinition>();
            AudioClip clip = AudioClip.Create("Point validation", 44100, 1, 44100, false);
            var audioData = new SerializedObject(audio);
            PooledAudioSource audioPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Audio/WorldAudioSource.prefab").GetComponent<PooledAudioSource>();
            audioData.FindProperty("prefab").objectReferenceValue = audioPrefab;
            audioData.FindProperty("clips").arraySize = 1;
            audioData.FindProperty("clips").GetArrayElementAtIndex(0).objectReferenceValue = clip;
            audioData.FindProperty("volume").floatValue = 0f;
            audioData.ApplyModifiedPropertiesWithoutUndo();
            ParticleEffectDefinition effect = AssetDatabase.LoadAssetAtPath<ParticleEffectDefinition>("Assets/ScriptObjects/Presentation/ChargeGlow.asset");
            Transform anchor = player.Animation.Model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightHand);
            string path = AnimationUtility.CalculateTransformPath(anchor, player.Animation.Model);
            definition.useRootMotion = false;
            definition.hitWindows = Array.Empty<HitWindow>();
            definition.effectWindows = Array.Empty<EffectWindow>();
            definition.points = new ActionPoint[]
            {
                new ComboAdvancePoint { time = 0f },
                new AudioPoint
                {
                    time = 0f,
                    audio = audio,
                    bonePath = path,
                    offset = Vector3.right * .2f
                },
                new EffectPoint
                {
                    time = 0f,
                    effect = effect,
                    bonePath = path,
                    offset = Vector3.up * .3f,
                    rotation = new Vector3(0f, 35f, 0f),
                    follow = true
                },
                new ComboAdvancePoint { time = .002f },
                new ComboAdvancePoint { time = .003f },
                new EffectPoint
                {
                    time = definition.Duration,
                    effect = effect,
                    follow = true
                }
            };
            ActionController actions = player.Actions;
            Require(actions.TryStart(definition, null, out long id), "Typed point action starts");
            ActionRuntime runtime = actions.Current;
            Require(runtime.NextPoint == 0, "Start does not process zero points before input consumption");
            actions.ProcessInitialPoints(id);
            Require(runtime.NextPoint == 3 && runtime.ComboAdvanced && runtime.ownedEffects.Count == 1, "Three zero-time point types execute");
            PooledAudioSource audioInstance = FindObjectsByType<PooledAudioSource>(FindObjectsSortMode.None).First(item => item.IsRented);
            PooledParticleEffect effectInstance = FindObjectsByType<PooledParticleEffect>(FindObjectsSortMode.None).First(item => item.IsRented);
            Require(Vector3.Distance(audioInstance.transform.position, anchor.TransformPoint(Vector3.right * .2f)) < .00001f, "Audio point uses configured bone and offset");
            Require(Vector3.Distance(effectInstance.transform.position, anchor.TransformPoint(Vector3.up * .3f)) < .00001f, "Effect point uses configured bone and offset");
            Require(Quaternion.Angle(effectInstance.transform.rotation, anchor.rotation * Quaternion.Euler(0f, 35f, 0f)) < .1f, "Effect point applies rotation");
            actions.ProcessInitialPoints(id);
            Require(runtime.NextPoint == 3 && runtime.ownedEffects.Count == 1, "Zero points do not repeat");
            player.transform.position += Vector3.right;
            typeof(PooledParticleEffect).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(effectInstance, null);
            float followError = Vector3.Distance(effectInstance.transform.position, anchor.TransformPoint(Vector3.up * .3f));
            // 100 倍骨骼矩阵往返会产生约 1e-5 的世界坐标舍入误差
            Require(followError < .0001f, "Following point retains local offset; world error=" + followError + "; bone scale=" + anchor.lossyScale);
            actions.Advance(.01f);
            actions.ProcessEvents();
            Require(runtime.NextPoint == 5, "Step crossing multiple points dispatches all");
            actions.Advance(definition.Duration);
            actions.ProcessEvents();
            Require(runtime.NextPoint == 6 && runtime.ownedEffects.Count == 2, "Endpoint point dispatches before completion");
            var handles = runtime.ownedEffects.ToArray();
            actions.FinishAtEnd();
            Require(handles.All(handle => !handle.IsValid), "Completion returns following point effects");
            Require(actions.TryStart(definition, null, out long secondId), "Shared definition starts a fresh execution");
            ActionRuntime next = actions.Current;
            Require(next.NextPoint == 0 && !next.ComboAdvanced && next.ownedEffects.Count == 0, "Point runtime state is not shared");
            actions.ProcessInitialPoints(secondId);
            var owned = next.ownedEffects[0];
            actions.Cancel(secondId, ActionEndReason.Cancelled);
            actions.Advance(definition.Duration);
            actions.ProcessEvents();
            Require(next.NextPoint == 3 && !owned.IsValid, "Cancellation returns effects and stops pending points");
            foreach (PooledAudioSource instance in FindObjectsByType<PooledAudioSource>(FindObjectsSortMode.None))
                instance.Handle.Return();
            Destroy(definition);
            Destroy(audio);
            Destroy(clip);
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
