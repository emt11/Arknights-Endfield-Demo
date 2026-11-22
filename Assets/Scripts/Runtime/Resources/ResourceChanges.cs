using System;
using UnityEngine;

namespace Endfield.Resources
{
    internal sealed class ResourceChanges<T> where T : struct
    {
        private event Action<T, T> changed;

        internal IDisposable Subscribe(Action<T, T> listener)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            changed += listener;
            return new Subscription(() => changed -= listener);
        }

        internal void Publish(T previous, T current)
        {
            if (changed == null) return;
            // 资源已提交，单个订阅者报错不应中断其余通知或扣费结果
            foreach (Action<T, T> listener in changed.GetInvocationList())
            {
                try { listener(previous, current); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }

        private sealed class Subscription : IDisposable
        {
            private Action unsubscribe;

            internal Subscription(Action unsubscribe)
            {
                this.unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                unsubscribe?.Invoke();
                unsubscribe = null;
            }
        }
    }
}
