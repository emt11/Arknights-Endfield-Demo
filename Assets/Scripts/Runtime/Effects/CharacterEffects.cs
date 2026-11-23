using System;
using Endfield.Resources;
using Endfield.Stats;
using Endfield.Timing;
using UnityEngine;

namespace Endfield.Effects
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterStats), typeof(CharacterResources))]
    [DefaultExecutionOrder(-40)]
    public sealed class CharacterEffects : MonoBehaviour
    {
        public EffectController Controller { get; private set; }
        private IDisposable clockSubscription;
        private IDisposable pulseSubscription;

        private void Awake()
        {
            Controller = new EffectController(GetComponent<CharacterStats>().Stats);
            var resources = GetComponent<CharacterResources>().Container;
            pulseSubscription = Controller.SubscribePulse(pulse =>
            {
                if (pulse.Kind == PulseKind.RestoreUltimate) resources.RestoreUltimate(pulse.Amount);
            });
        }

        private void Start()
        {
            // 场景与角色 Prefab 独立保存，实例进入场景后只绑定本场的驱动
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (!root.TryGetComponent<BattleTimeDriver>(out var driver)) continue;
                clockSubscription = driver.Clock.SubscribeBattleStep(step => Controller.Advance(step.Delta));
                break;
            }
        }

        private void OnDestroy()
        {
            clockSubscription?.Dispose();
            pulseSubscription?.Dispose();
            Controller?.Dispose();
        }
    }
}
