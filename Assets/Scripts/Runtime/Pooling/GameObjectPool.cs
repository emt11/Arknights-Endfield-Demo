using System;
using System.Collections.Generic;
using UnityEngine;

namespace Endfield.Pooling
{
    [DefaultExecutionOrder(-200), DisallowMultipleComponent]
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
            Transform activeRoot = CreateRoot("Active", true);
            Transform inactiveRoot = CreateRoot("Inactive", false);
            foreach (Entry entry in entries)
                pools.Add(entry.prefab, new PrefabPool(entry.prefab, activeRoot, inactiveRoot,
                    entry.prewarmCount, entry.maxRetained));
        }

        public T Rent<T>(T prefab, Vector3 position, Quaternion rotation) where T : PooledObject
            => (T)pools[prefab].Rent(position, rotation);

        public void ReturnAll()
        {
            foreach (PrefabPool pool in pools.Values)
                pool.ReturnAll();
        }

        public void GetCounts(PooledObject prefab, out int rented, out int inactive)
        {
            PrefabPool pool = pools[prefab];
            rented = pool.RentedCount;
            inactive = pool.InactiveCount;
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
