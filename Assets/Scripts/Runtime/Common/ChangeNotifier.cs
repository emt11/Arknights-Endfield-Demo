using System;
using UnityEngine;

namespace Endfield.Common
{
    public sealed class ChangeNotifier<T>
    {
        private event Action<T> changed; // 用来保存注册好的通知回调

        public bool HasSubscribers => changed != null;

        public IDisposable Subscribe(Action<T> listener)
        {
            return Subscribe(listener, static (callback, change) => callback(change));
        }

        // 允许调用方保留原有回调签名，复合委托中的每个回调也单独处理异常
        public IDisposable Subscribe<TListener>(TListener listener, Action<TListener, T> invoke) where TListener : Delegate
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            if (invoke == null) throw new ArgumentNullException(nameof(invoke));
            var callbacks = listener.GetInvocationList();

            void Notify(T change)
            {
                foreach (TListener callback in callbacks)
                {
                    try { invoke(callback, change); }
                    catch (Exception error) { Debug.LogException(error); }
                }
            }

            changed += Notify;
            return new Subscription(() => changed -= Notify);
        }

        public void Publish(T change)
        {
            changed?.Invoke(change);
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
