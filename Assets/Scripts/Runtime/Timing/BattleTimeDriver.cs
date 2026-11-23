using UnityEngine;

namespace Endfield.Timing
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public sealed class BattleTimeDriver : MonoBehaviour
    {
        public BattleClock Clock { get; private set; }

        private void Awake() => Clock = new BattleClock();

        private void Update() => Clock.Advance(Time.unscaledDeltaTime);
    }
}
