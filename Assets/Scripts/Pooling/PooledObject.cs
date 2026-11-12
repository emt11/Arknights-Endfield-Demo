using System;
using UnityEngine;

namespace Endfield.Pooling
{
    [DisallowMultipleComponent]
    public class PooledObject : MonoBehaviour
    {
        private PrefabPool owner;
        private Vector3 initialScale;
        private ulong version;

        public bool IsRented { get; private set; }
        public PoolHandle Handle => new PoolHandle(this, version);

        internal void Bind(PrefabPool pool)
        {
            owner = pool;
            initialScale = transform.localScale;
            OnCreated();
        }

        internal void PrepareForRent(Transform activeRoot, Vector3 position, Quaternion rotation)
        {
            transform.SetParent(activeRoot, false);
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = initialScale;
            version++;
            IsRented = true;
            OnRent();
        }

        public PoolHandle Activate()
        {
            if (!IsRented)
                throw new InvalidOperationException("Rent the object before activating it");

            gameObject.SetActive(true);
            return Handle;
        }

        internal bool Matches(ulong expectedVersion)
        {
            return IsRented && version == expectedVersion && owner != null && !owner.IsDisposed;
        }

        internal void Return(ulong expectedVersion)
        {
            if (Matches(expectedVersion))
                owner.Return(this);
        }

        internal void ResetForPool(Transform inactiveRoot)
        {
            IsRented = false;
            StopAllCoroutines();
            CancelInvoke();
            OnReturn();
            gameObject.SetActive(false);
            transform.SetParent(inactiveRoot, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = initialScale;
        }

        internal void Invalidate()
        {
            IsRented = false;
            owner = null;
        }

        protected void ReturnToPool() => Handle.Return();
        protected virtual void OnCreated() { }
        protected virtual void OnRent() { }
        protected virtual void OnReturn() { }
    }
}
