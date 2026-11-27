using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Endfield.Timing
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public sealed class BattleTimeDriver : MonoBehaviour
    {
        public BattleClock Clock { get; private set; }

        private void Awake() => Clock = new BattleClock();

        public static IDisposable SubscribeBattleStep(Scene scene, Action<TimeStep> listener)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent<BattleTimeDriver>(out var driver))
                    return driver.Clock.SubscribeBattleStep(listener);
            return null;
        }

        private void Update() => Clock.Advance(Time.unscaledDeltaTime);
    }
}
