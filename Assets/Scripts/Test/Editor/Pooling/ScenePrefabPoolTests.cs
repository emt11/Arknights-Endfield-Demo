using System;
using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using Endfield.Presentation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Endfield.Pooling.Tests
{
    public sealed class ScenePrefabPoolTests
    {
        private GameObject fixture;
        private GameObject template;
        private const string SparkPath = "Assets/Prefabs/Effects/HitSpark.prefab";

        [UnitySetUp]
        public IEnumerator EnterGame()
        {
            yield return new EnterPlayMode();
            fixture = new GameObject("对象池测试");
            template = new GameObject("测试模板");
            template.transform.SetParent(fixture.transform, false);
            template.SetActive(false);
            template.transform.localPosition = new Vector3(2, 3, 4);
            template.transform.localRotation = Quaternion.Euler(0, 35, 0);
            template.transform.localScale = new Vector3(1, 2, 3);
        }

        [UnityTearDown]
        public IEnumerator LeaveGame()
        {
            if (fixture) Object.Destroy(fixture);
            yield return null;
            yield return new ExitPlayMode();
        }

        private ScenePrefabPool CreatePool(GameObject prefab, int capacity = 1, int prewarm = 0)
        {
            var host = new GameObject("测试池");
            host.SetActive(false);
            host.transform.SetParent(fixture.transform, false);
            host.transform.localPosition = new Vector3(10, 0, -5);
            var pool = host.AddComponent<ScenePrefabPool>();
            using (var config = new SerializedObject(pool))
            {
                config.FindProperty("prefab").objectReferenceValue = prefab;
                config.FindProperty("capacity").intValue = capacity;
                config.FindProperty("prewarmCount").intValue = prewarm;
                config.ApplyModifiedPropertiesWithoutUndo();
            }
            host.SetActive(true);
            return pool;
        }

        [UnityTest]
        public IEnumerator CombatDemoPrewarmsReusesAndFinishesHitSparks()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/CombatDemo.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            var pool = GameObject.Find("HitSparkPool").GetComponent<ScenePrefabPool>();
            var warm = pool.GetComponentsInChildren<PooledHitSpark>(true).Single().gameObject;
            Assert.That(warm.activeSelf, Is.False);
            Assert.That(pool.TryRent((instance, lease) =>
            {
                Assert.That(instance, Is.SameAs(warm));
                Assert.That(instance.activeInHierarchy, Is.False);
                Assert.That(lease.IsActive, Is.True);
                instance.transform.position = Vector3.up;
                instance.transform.localScale = Vector3.one * 3;
                instance.GetComponent<PooledHitSpark>().Bind(lease);
            }, out var first), Is.True);
            Assert.That(warm.activeInHierarchy, Is.True);
            var particles = warm.GetComponent<ParticleSystem>();
            particles.Emit(2);
            Assert.That(particles.particleCount, Is.Positive);
            GameObject secondObject = null;
            Assert.That(pool.TryRent((obj, lease) =>
            {
                secondObject = obj;
                obj.GetComponent<PooledHitSpark>().Bind(lease);
            }, out var second), Is.True);
            Assert.That(secondObject, Is.Not.SameAs(warm));
            Assert.That(pool.TryRent((_, _) => Assert.Fail("池满时执行了配置"), out var full), Is.False);
            Assert.That(full.IsActive, Is.False);
            Assert.That(first.TryRelease(), Is.True);
            Assert.That(pool.TryRent((obj, lease) =>
            {
                Assert.That(obj, Is.SameAs(warm));
                Assert.That(obj.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(particles.particleCount, Is.Zero);
                obj.transform.position = Vector3.right;
                obj.GetComponent<PooledHitSpark>().Bind(lease);
            }, out var third), Is.True);
            Assert.That(first.TryRelease(), Is.False);
            Assert.That(warm.activeInHierarchy, Is.True);
            Assert.That(warm.transform.position, Is.EqualTo(Vector3.right));

            double deadline = Time.realtimeSinceStartupAsDouble + 4;
            while ((third.IsActive || second.IsActive) && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(third.IsActive || second.IsActive, Is.False, "两份火花播完后应各自归还");
            Assert.That(warm.activeSelf || secondObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator ReleaseRestoresLocalPoseAndInvalidatesTheOldLease()
        {
            var pool = CreatePool(template, prewarm: 1);
            GameObject firstObject = null;
            Assert.That(pool.TryRent((obj, _) =>
            {
                firstObject = obj;
                obj.transform.SetPositionAndRotation(Vector3.down, Quaternion.identity);
                obj.transform.localScale = Vector3.one * 5;
            }, out var first), Is.True);
            Assert.That(first.TryRelease(), Is.True);
            Assert.That(first.IsActive || firstObject.activeSelf, Is.False);
            Assert.That(pool.TryRent((obj, lease) =>
            {
                Assert.That(obj, Is.SameAs(firstObject));
                Assert.That(obj.transform.parent, Is.SameAs(pool.transform));
                Assert.That(obj.transform.localPosition, Is.EqualTo(template.transform.localPosition));
                Assert.That(Quaternion.Angle(obj.transform.localRotation, template.transform.localRotation), Is.LessThan(0.01f));
                Assert.That(obj.transform.localScale, Is.EqualTo(template.transform.localScale));
                Assert.That(first.TryRelease(), Is.False);
                Assert.That(lease.IsActive, Is.True);
            }, out var second), Is.True);
            Assert.That(second.TryRelease(), Is.True);
            yield break;
        }

        [UnityTest]
        public IEnumerator ConfigureCanReleaseAndRentAgainOrThrow()
        {
            var pool = CreatePool(template);
            PoolLease replacement = default;
            Assert.That(pool.TryRent((obj, original) =>
            {
                Assert.That(original.TryRelease(), Is.True);
                Assert.That(pool.TryRent(null, out replacement), Is.True);
            }, out var returnedEarly), Is.False);
            Assert.That(returnedEarly.IsActive, Is.False);
            Assert.That(replacement.TryRelease(), Is.True);

            var failure = new InvalidOperationException("配置失败");
            PoolLease captured = default;
            var thrown = Assert.Throws<InvalidOperationException>(() => pool.TryRent((obj, lease) =>
            {
                captured = lease;
                throw failure;
            }, out _));
            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(captured.IsActive, Is.False);
            thrown = Assert.Throws<InvalidOperationException>(() => pool.TryRent((obj, original) =>
            {
                original.TryRelease();
                pool.TryRent(null, out replacement);
                throw failure;
            }, out _));
            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(replacement.IsActive, Is.True);
            yield break;
        }

        [UnityTest]
        public IEnumerator ConfigureErrorSurvivesResetFailure()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SparkPath);
            var pool = CreatePool(prefab);
            var failure = new InvalidOperationException("配置中断");
            PoolLease current = default;
            GameObject broken = null;
            bool closedDuringReset = false;
            void ObserveReset(string message, string trace, LogType type)
            {
                if (type == LogType.Error && message.Contains("PooledHitSpark 重置失败"))
                    closedDuringReset = !current.IsActive && !broken.activeSelf && !pool.TryRent(null, out _);
            }

            LogAssert.Expect(LogType.Error, new Regex("PooledHitSpark 重置失败"));
            Application.logMessageReceived += ObserveReset;
            try
            {
                var thrown = Assert.Throws<InvalidOperationException>(() => pool.TryRent((obj, lease) =>
                {
                    current = lease;
                    broken = obj;
                    RemoveParticleReference(obj);
                    throw failure;
                }, out _));
                Assert.That(thrown, Is.SameAs(failure));
            }
            finally { Application.logMessageReceived -= ObserveReset; }
            Assert.That(closedDuringReset, Is.True);
            Assert.That(pool.TryRent((obj, _) => Assert.That(obj, Is.Not.SameAs(broken)), out _), Is.True);
            yield break;
        }

        private static void RemoveParticleReference(GameObject instance)
        {
            // 只破坏本轮实例的粒子引用，原始 Prefab 与其他实例仍可复用
            using var copy = new SerializedObject(instance.GetComponent<PooledHitSpark>());
            copy.FindProperty("particles").objectReferenceValue = null;
            copy.ApplyModifiedPropertiesWithoutUndo();
        }

        [UnityTest]
        public IEnumerator ResetFailureDiscardsOnlyTheDamagedInstance()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SparkPath);
            var pool = CreatePool(prefab, 2);
            GameObject damaged = null;
            GameObject healthy = null;
            pool.TryRent((obj, lease) =>
            {
                damaged = obj;
                obj.GetComponent<PooledHitSpark>().Bind(lease);
            }, out var first);
            pool.TryRent((obj, _) => healthy = obj, out var second);
            RemoveParticleReference(damaged);
            LogAssert.Expect(LogType.Error, new Regex("PooledHitSpark 重置失败"));
            pool.RecycleAll();
            Assert.That(first.IsActive || second.IsActive, Is.False);
            yield return null;
            Assert.That(damaged == null, Is.True);
            Assert.That(healthy.activeSelf, Is.False);
            GameObject a = null;
            GameObject b = null;
            Assert.That(pool.TryRent((obj, _) => a = obj, out _), Is.True);
            Assert.That(pool.TryRent((obj, _) => b = obj, out _), Is.True);
            Assert.That(a == healthy || b == healthy, Is.True);
            Assert.That(a, Is.Not.SameAs(b));
        }

        [UnityTest]
        public IEnumerator DestroyedBorrowedAndIdleInstancesCanBeReplaced()
        {
            var pool = CreatePool(template);
            GameObject instance = null;
            pool.TryRent((obj, _) => instance = obj, out var first);
            Object.Destroy(instance);
            yield return null;
            Assert.That(first.IsActive, Is.False);
            Assert.That(pool.TryRent((obj, _) => instance = obj, out var second), Is.True);
            Assert.That(first.TryRelease(), Is.False);
            Assert.That(second.TryRelease(), Is.True);
            Object.Destroy(instance);
            yield return null;
            Assert.That(pool.TryRent((obj, _) => instance = obj, out var third), Is.True);
            Assert.That(instance.activeInHierarchy, Is.True);
            Assert.That(second.TryRelease(), Is.False);
            Assert.That(third.IsActive, Is.True);
        }

        [UnityTest]
        public IEnumerator DisableRecyclesAndRemovingTheComponentCleansItsChildren()
        {
            var pool = CreatePool(template, prewarm: 1);
            GameObject instance = null;
            pool.TryRent((obj, _) => instance = obj, out var first);
            var host = pool.gameObject;
            pool.enabled = false;
            Assert.That(first.IsActive || instance.activeSelf, Is.False);
            Assert.That(pool.TryRent(null, out _), Is.False);
            pool.RecycleAll();
            pool.enabled = true;
            Assert.That(pool.TryRent((obj, _) => Assert.That(obj, Is.SameAs(instance)), out var second), Is.True);
            Object.Destroy(pool);
            yield return null;
            yield return null;
            Assert.That(second.TryRelease(), Is.False);
            Assert.That(instance == null, Is.True);
            Assert.That(host.transform.childCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator InvalidSettingsFailBeforePrewarming()
        {
            var cases = new (GameObject prefab, int capacity, int prewarm, string error)[]
            {
                (null, 1, 0, "缺少对象池 Prefab"),
                (template, 0, 0, "capacity"),
                (template, 1, -1, "prewarmCount"),
                (template, 1, 2, "prewarmCount")
            };
            foreach (var settings in cases)
            {
                LogAssert.Expect(LogType.Exception, new Regex(settings.error));
                var pool = CreatePool(settings.prefab, settings.capacity, settings.prewarm);
                Assert.That(pool.transform.childCount, Is.Zero);
                Assert.That(pool.TryRent(null, out var lease), Is.False);
                Assert.That(lease.TryRelease(), Is.False);
            }
            yield break;
        }
    }
}
