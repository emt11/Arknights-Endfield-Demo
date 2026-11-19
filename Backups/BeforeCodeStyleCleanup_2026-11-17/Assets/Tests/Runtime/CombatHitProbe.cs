#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Endfield.Character;
using Endfield.Combat;
using Endfield.Controller;
using Endfield.Pooling;
using Endfield.Presentation;
using UnityEditor;
using UnityEngine;

namespace Endfield.Validation
{
    [DefaultExecutionOrder(1000)]
    public sealed class CombatHitProbe : MonoBehaviour
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
                ValidateHits();
                ValidateFollowingEffect();
            }
            catch (Exception exception)
            {
                Failure = exception.ToString();
            }

            Completed = true;
        }

        private static void ValidateHits()
        {
            CombatSimulation simulation = FindFirstObjectByType<CombatSimulation>();
            simulation.enabled = false;
            Combatant source = FindFirstObjectByType<PlayerController>().Character;
            Combatant target = null;
            foreach (Combatant unit in simulation.Units)
            {
                if (unit != source)
                {
                    target = unit;
                    break;
                }
            }

            source.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.identity);
            source.StepStartPosition = source.transform.position;
            source.StepStartRotation = Quaternion.identity;
            target.transform.position = new Vector3(0f, 3f, 1.4f);
            ActionDefinition definition = Instantiate(source.Actions.BasicAttack);
            definition.UseRootMotion = false;
            definition.WarpWindows = Array.Empty<MotionWarpWindow>();
            definition.HitWindows = new[] { new HitWindow { Start = 0.1f, End = 0.12f, Group = 0, DamageMultiplier = 0.8f, Offset = new Vector3(0f, 1f, 1f), Radius = 0.8f } };
            Require(source.Actions.TryStart(definition, target, out long id), "Could not start test action");
            ActionRuntime runtime = source.Actions.Current;
            runtime.PreviousTime = 0.09f;
            runtime.LocalTime = 0.13f;
            Physics.SyncTransforms();
            var requests = new List<HitRequest>();
            var detection = new HitDetection();
            detection.Collect(source, simulation.Rules, requests);
            Require(requests.Count == 1, "Short window or multiple Hurtbox collection failed: " + requests.Count);
            Require(runtime.Hits.Count == 0, "Detection prematurely registered hit");
            float before = target.Health.Current;
            int notifications = 0;
            target.Health.Changed += _ => notifications++;
            Require(simulation.Resolver.Resolve(requests[0]), "First legal hit was rejected");
            float expected = DamageCalculator.Calculate(source.Stats.Attack, 0.8f, target.Stats.Defense, simulation.Rules.DefenseConstant);
            Require(Math.Abs(before - target.Health.Current - expected) < 0.001f, "Incorrect damage");
            Require(!simulation.Resolver.Resolve(requests[0]), "Duplicate request damaged twice");
            Require(notifications == 0, "Health result published inside settlement");
            var secondWindow = new HitWindow { Group = 1, DamageMultiplier = 0.8f };
            Require(simulation.Resolver.Resolve(new HitRequest(source, target, runtime, secondWindow, requests[0].Position)), "Different hit group was incorrectly deduplicated");

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 4f, 0.5f);
            wall.transform.localScale = new Vector3(3f, 3f, 0.2f);
            Physics.SyncTransforms();
            requests.Clear();
            detection.Collect(source, simulation.Rules, requests);
            Require(requests.Count == 0, "Attack penetrated World obstruction");
            wall.SetActive(false);
            Destroy(wall);
            Physics.SyncTransforms();
            requests.Clear();
            detection.Collect(source, simulation.Rules, requests);
            Require(requests.Count == 1, "Contact did not recover after obstacle removal");
            source.Actions.Cancel(id, ActionEndReason.Cancelled);
            Require(!simulation.Resolver.Resolve(requests[0]), "Cancelled source resolved stale hit");
            Require(source.Actions.TryStart(definition, target, out _), "New execution failed");
            Require(source.Actions.Current.Hits.Count == 0, "New execution inherited hit records");
            source.Health.ApplyDamage(float.MaxValue);
            Require(source.Actions.Current == null, "Death did not cancel action immediately");
            foreach (Hurtbox hurtbox in source.GetComponentsInChildren<Hurtbox>())
                Require(!hurtbox.GetComponent<Collider>().enabled, "Death left Hurtbox active");

            simulation.AdvanceFrame(1f / 60f, false);
            Require(notifications == 2, "Settlement did not publish captured health results");
            Destroy(definition);
        }

        private static void ValidateFollowingEffect()
        {
            GameObjectPool pool = FindFirstObjectByType<GameObjectPool>();
            ParticleEffectDefinition definition = AssetDatabase.LoadAssetAtPath<ParticleEffectDefinition>("Assets/ScriptObjects/Presentation/ChargeGlow.asset");
            GameObject anchor = new GameObject("Effect validation anchor");
            Vector3 offset = new Vector3(1f, 0.5f, 0.25f);
            Quaternion rotation = Quaternion.Euler(0f, 25f, 0f);
            PooledParticleEffect effect = pool.Rent(definition.Prefab, offset, rotation);
            PoolHandle handle = effect.Play(anchor.transform);
            Require(Vector3.Distance(effect.transform.position, offset) < 0.0001f, "Initial follow offset was lost");
            anchor.transform.SetPositionAndRotation(new Vector3(5f, 2f, 0f), Quaternion.Euler(0f, 80f, 0f));
            effect.SendMessage("LateUpdate");
            Require(Vector3.Distance(effect.transform.position, anchor.transform.TransformPoint(offset)) < 0.0001f, "Following offset is incorrect");
            Require(Quaternion.Angle(effect.transform.rotation, anchor.transform.rotation * rotation) < 0.001f, "Following rotation is incorrect");
            handle.Return();
            effect = pool.Rent(definition.Prefab, Vector3.one, Quaternion.identity);
            handle = effect.Play();
            effect.SendMessage("LateUpdate");
            Require(Vector3.Distance(effect.transform.position, Vector3.one) < 0.0001f, "Pool reuse retained old follow pose");
            handle.Return();
            Destroy(anchor);
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
