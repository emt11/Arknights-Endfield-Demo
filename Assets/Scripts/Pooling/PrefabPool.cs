using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;

namespace Endfield.Pooling
{
    internal sealed class PrefabPool : IDisposable
    {
        private readonly PooledObject prefab;
        private readonly Transform activeRoot;
        private readonly Transform inactiveRoot;
        private readonly ObjectPool<PooledObject> items;
        private readonly HashSet<PooledObject> rented = new HashSet<PooledObject>();

        public bool IsDisposed { get; private set; }
        public int RentedCount => rented.Count;
        public int InactiveCount => items.CountInactive;

        public PrefabPool(PooledObject prefab, Transform activeRoot, Transform inactiveRoot, int prewarmCount, int maxRetained)
        {
            this.prefab = prefab;
            this.activeRoot = activeRoot;
            this.inactiveRoot = inactiveRoot;

            items = new ObjectPool<PooledObject>(
                createFunc: Create,
                actionOnGet: null,
                actionOnRelease: item => item.ResetForPool(inactiveRoot),
                actionOnDestroy: DestroyItem,
                collectionCheck: true,
                defaultCapacity: Mathf.Max(1, prewarmCount),
                maxSize: maxRetained);

            var warmup = new PooledObject[prewarmCount];
            for (int i = 0; i < warmup.Length; i++)
                warmup[i] = items.Get();
            for (int i = 0; i < warmup.Length; i++)
                items.Release(warmup[i]);
        }

        private PooledObject Create()
        {
            PooledObject item = Object.Instantiate(prefab, inactiveRoot);
            item.gameObject.SetActive(false);
            item.Bind(this);
            return item;
        }

        public PooledObject Rent(Vector3 position, Quaternion rotation)
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(PrefabPool));

            PooledObject item = items.Get();
            rented.Add(item);
            item.PrepareForRent(activeRoot, position, rotation);
            return item;
        }

        public void Return(PooledObject item)
        {
            if (IsDisposed || !rented.Remove(item))
                return;

            items.Release(item);
        }

        public void ReturnAll()
        {
            var snapshot = new List<PooledObject>(rented);
            foreach (PooledObject item in snapshot)
                Return(item);
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            foreach (PooledObject item in rented)
                DestroyItem(item);
            rented.Clear();
            items.Clear();
        }

        private static void DestroyItem(PooledObject item)
        {
            if (item == null)
                return;

            item.Invalidate();
            item.gameObject.SetActive(false);
            Object.Destroy(item.gameObject);
        }
    }
}
