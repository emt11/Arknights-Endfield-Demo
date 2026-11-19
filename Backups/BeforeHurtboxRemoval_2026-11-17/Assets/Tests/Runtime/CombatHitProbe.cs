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
                ValidateAnimatedWeapon();
                ValidateHits();
                ValidateFollowingEffect();
            }
            catch (Exception exception)
            {
                Failure = exception.ToString();
            }

            Completed = true;
        }

        private static void ValidateAnimatedWeapon()
        {
            CombatSimulation simulation = FindFirstObjectByType<CombatSimulation>();
            simulation.enabled = false;
            Combatant source = FindFirstObjectByType<PlayerController>().Character;
            Combatant target = null;
            foreach (Combatant unit in simulation.Units)
            {
                if (unit != source)
                    target = unit;
            }
            Require(source.Weapon.Hitboxes.Count == 1 && source.Weapon.Socket.childCount == 1, "Weapon did not mount exactly once");
            Hitbox blade = source.Weapon.Hitboxes[0];
            blade.GetGeometry(blade.ReadPose(), out _, out _, out float radius);
            Require(Mathf.Abs(radius - .14f) < .001f, "Weapon inherited the model's 100x import scale");

            foreach (ActionDefinition action in source.Definition.BasicAttacks)
            {
                source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                source.Motor.ResetMotion();
                target.transform.position = new Vector3(0f, 0f, 2f);
                Physics.SyncTransforms();
                float before = target.Health.Current;
                Require(source.Actions.TryStart(action, target, out _), "Could not start " + action.name);
                for (int step = 0; source.Actions.Current != null && step < 300; step++)
                    simulation.AdvanceFrame(1f / 60f, false);
                Require(source.Actions.Current == null, action.name + " did not finish");
                float expected = DamageCalculator.Calculate(source.Stats.Attack, action.hitWindows[0].damageMultiplier, target.Stats.Defense, simulation.Rules.defenseConstant);
                Require(Mathf.Abs(before - target.Health.Current - expected) < .001f, action.name + " should hit once with the mounted blade; damage=" + (before - target.Health.Current));
            }

            // 禁用根运动后，真实骨骼动画仍应独立产生挥击命中
            ActionDefinition stationary = Instantiate(source.Actions.BasicAttack);
            stationary.useRootMotion = false;
            stationary.warpWindows = Array.Empty<MotionWarpWindow>();
            source.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            source.Motor.ResetMotion();
            target.transform.position = new Vector3(0f, 0f, 1.4f);
            Physics.SyncTransforms();
            float health = target.Health.Current;
            Require(source.Actions.TryStart(stationary, target, out _), "Could not start stationary swing");
            for (int step = 0; source.Actions.Current != null && step < 300; step++)
                simulation.AdvanceFrame(1f / 60f, false);
            Require(source.Actions.Current == null, "Stationary swing did not finish");
            Require(target.Health.Current < health, "Real bone animation missed without actor movement");
            Require(new Vector2(source.transform.position.x, source.transform.position.z).magnitude < .001f, "Stationary swing moved actor horizontally");
            Destroy(stationary);
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
            target.transform.position = new Vector3(0f, 3f, 1.4f);
            GameObject weapon = new GameObject("Hit query fixture");
            Hitbox blade = weapon.AddComponent<Hitbox>();
            blade.center = new Vector3(0f, 1f, 1f);
            blade.axis = HitboxAxis.Z;
            blade.length = .8f;
            blade.radius = .12f;
            GameObject second = new GameObject("Second blade");
            second.transform.SetParent(weapon.transform, false);
            Hitbox secondBlade = second.AddComponent<Hitbox>();
            secondBlade.id = 1;
            secondBlade.center = blade.center;
            secondBlade.axis = blade.axis;
            secondBlade.length = blade.length;
            secondBlade.radius = blade.radius;
            source.Weapon.Equip(weapon, source.transform);
            Destroy(weapon);
            ActionDefinition definition = Instantiate(source.Actions.BasicAttack);
            definition.useRootMotion = false;
            definition.warpWindows = Array.Empty<MotionWarpWindow>();
            definition.hitWindows = new[] { new HitWindow { start = 0.1f, end = 0.12f, group = 0, damageMultiplier = 0.8f, hitboxIds = new[] { 0, 1 } } };
            Require(source.Actions.TryStart(definition, target, out long id), "Could not start test action");
            ActionRuntime runtime = source.Actions.Current;
            runtime.PreviousTime = 0.09f;
            runtime.LocalTime = 0.13f;
            source.Weapon.CaptureStart();
            source.Weapon.CaptureEnd();
            Physics.SyncTransforms();
            var requests = new List<HitRequest>();
            var detection = new HitDetection();
            detection.Collect(source, simulation.Rules, requests);
            Require(requests.Count == 1, "Short window or multiple Hurtbox collection failed: " + requests.Count);
            Require(runtime.hits.Count == 0, "Detection prematurely registered hit");
            ValidateSweeps(source, target, simulation.Rules, detection, requests);
            float before = target.Health.Current;
            int notifications = 0;
            target.Health.Changed += _ => notifications++;
            Require(simulation.Resolver.Resolve(requests[0]), "First legal hit was rejected");
            float expected = DamageCalculator.Calculate(source.Stats.Attack, 0.8f, target.Stats.Defense, simulation.Rules.defenseConstant);
            Require(Math.Abs(before - target.Health.Current - expected) < 0.001f, "Incorrect damage");
            Require(!simulation.Resolver.Resolve(requests[0]), "Duplicate request damaged twice");
            Require(notifications == 0, "Health result published inside settlement");
            var secondWindow = new HitWindow { group = 1, damageMultiplier = 0.8f };
            Require(simulation.Resolver.Resolve(new HitRequest(source, target, runtime, secondWindow, requests[0].position)), "Different hit group was incorrectly deduplicated");

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
            Require(source.Actions.Current.hits.Count == 0, "New execution inherited hit records");
            source.Health.ApplyDamage(float.MaxValue);
            Require(source.Actions.Current == null, "Death did not cancel action immediately");
            foreach (Hurtbox hurtbox in source.GetComponentsInChildren<Hurtbox>())
                Require(!hurtbox.GetComponent<Collider>().enabled, "Death left Hurtbox active");

            simulation.AdvanceFrame(1f / 60f, false);
            Require(notifications == 2, "Settlement did not publish captured health results");
            Destroy(definition);
        }

        private static void ValidateSweeps(Combatant source, Combatant target,
            CombatRulesDefinition rules, HitDetection detection, List<HitRequest> requests)
        {
            Hitbox blade = source.Weapon.GetHitbox(0);
            source.Weapon.GetHitbox(1).enabled = false;
            blade.transform.localRotation = Quaternion.Euler(0f, -75f, 0f);
            source.Weapon.CaptureStart();
            blade.transform.localRotation = Quaternion.Euler(0f, 75f, 0f);
            source.Weapon.CaptureEnd();
            requests.Clear();
            detection.Collect(source, rules, requests);
            Require(requests.Count == 1, "Stationary character's rotating blade missed the middle of a short window");
            Require(source.transform.position == new Vector3(0f, 3f, 0f), "Sweep test moved character");

            target.transform.position = new Vector3(1.35f, 3f, .4f);
            Physics.SyncTransforms();
            requests.Clear();
            detection.Collect(source, rules, requests);
            Require(requests.Count == 0, "Short window included the swing outside its active interval");

            blade.transform.localRotation = Quaternion.identity;
            source.Weapon.CaptureStart();
            source.Weapon.CaptureEnd();
            target.transform.position = new Vector3(.9f, 3f, 1f);
            Physics.SyncTransforms();
            requests.Clear();
            detection.Collect(source, rules, requests);
            Require(requests.Count == 0, "Old actor-front sphere still determines weapon hits");

            target.transform.position = new Vector3(0f, 3f, 1.4f);
            blade.shape = HitboxShape.Sphere;
            blade.radius = .2f;
            blade.transform.localPosition = Vector3.left * 2f;
            source.Weapon.CaptureStart();
            blade.transform.localPosition = Vector3.right * 2f;
            source.Weapon.CaptureEnd();
            Physics.SyncTransforms();
            requests.Clear();
            detection.Collect(source, rules, requests);
            Require(requests.Count == 1, "Fast translating sphere missed swept contact");

            // 不重新采样时，多次查询同一窗口必须读取完全相同的轨迹
            requests.Clear();
            detection.Collect(source, rules, requests);
            Require(requests.Count == 1, "Query mutated the shared step poses");
            source.Weapon.CaptureStart();
            source.Weapon.CaptureEnd();
            requests.Clear();
            detection.Collect(source, rules, requests);
            Require(requests.Count == 0, "New step inherited the previous sweep after teleport");

            blade.shape = HitboxShape.Capsule;
            blade.radius = .12f;
            blade.transform.localPosition = Vector3.zero;
            source.Weapon.GetHitbox(1).enabled = true;
            source.Weapon.CaptureStart();
            source.Weapon.CaptureEnd();
            requests.Clear();
            detection.Collect(source, rules, requests);
            Require(requests.Count == 1, "Multiple weapon hitboxes bypassed group deduplication");
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
