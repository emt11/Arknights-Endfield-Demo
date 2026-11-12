using System;
using System.Collections.Generic;
using UnityEngine;

namespace Endfield.Pooling
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class GameObjectPool : MonoBehaviour
    {
        [Serializable]
        private sealed class Entry
        {
            public PooledObject prefab;
            [Min(0)] public int prewarmCount = 4;
            [Min(1)] public int maxRetained = 16;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        private readonly Dictionary<PooledObject, PrefabPool> pools = new Dictionary<PooledObject, PrefabPool>();

        private void Awake()
        {
            var seen = new HashSet<PooledObject>();
            foreach (Entry entry in entries)
            {
                if (entry == null || entry.prefab == null ||
                    entry.prefab.transform.parent != null ||
                    entry.prefab.gameObject.scene.IsValid() ||
                    !seen.Add(entry.prefab))
                    throw new InvalidOperationException("Pool entries need unique prefab root components");

                if (entry.maxRetained < 1 || entry.prewarmCount < 0 ||
                    entry.prewarmCount > entry.maxRetained)
                    throw new InvalidOperationException("Pool capacity requires 0 <= prewarm <= maxRetained");
            }

            Transform activeRoot = CreateRoot("Active", true);
            Transform inactiveRoot = CreateRoot("Inactive", false);
            foreach (Entry entry in entries)
            {
                pools.Add(entry.prefab, new PrefabPool(entry.prefab, activeRoot, inactiveRoot, entry.prewarmCount, entry.maxRetained));
            }
        }

        public T Rent<T>(T prefab, Vector3 position, Quaternion rotation) where T : PooledObject
        {
            return (T)GetPool(prefab).Rent(position, rotation);
        }

        public void ReturnAll()
        {
            foreach (PrefabPool pool in pools.Values)
                pool.ReturnAll();
        }

        public void GetCounts(PooledObject prefab, out int rented, out int inactive)
        {
            PrefabPool pool = GetPool(prefab);
            rented = pool.RentedCount;
            inactive = pool.InactiveCount;
        }

        private PrefabPool GetPool(PooledObject prefab)
        {
            if (prefab == null || !pools.TryGetValue(prefab, out PrefabPool pool))
                throw new InvalidOperationException("Prefab is not registered in this scene pool");

            return pool;
        }

        private Transform CreateRoot(string rootName, bool active)
        {
            var root = new GameObject(rootName);
            root.SetActive(active);
            root.transform.SetParent(transform, false);
            return root.transform;
        }

        private void OnDestroy()
        {
            foreach (PrefabPool pool in pools.Values)
                pool.Dispose();
            pools.Clear();
        }
    }
}
