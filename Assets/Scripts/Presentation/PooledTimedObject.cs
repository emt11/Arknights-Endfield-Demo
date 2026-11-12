using Endfield.Pooling;
using UnityEngine;

namespace Endfield.Presentation
{
    public sealed class PooledTimedObject : PooledObject
    {
        private float remaining;

        protected override void OnRent()
        {
            remaining = 0f;
        }

        public PoolHandle Show(float duration)
        {
            if (duration <= 0f)
            {
                ReturnToPool();
                return default;
            }

            remaining = duration;
            return Activate();
        }

        private void Update()
        {
            remaining -= Time.deltaTime;
            if (remaining <= 0f)
                ReturnToPool();
        }

        protected override void OnReturn()
        {
            remaining = 0f;
        }
    }
}
