using System;
using Endfield.Effects;
using Endfield.Resources;
using Endfield.Stats;
using UnityEngine;

namespace Endfield.Status
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterEffects))]
    [DefaultExecutionOrder(-30)]
    public sealed class CharacterStatus : MonoBehaviour
    {
        [SerializeField] private StatusRuleProfile rules;
        public StatusController Controller { get; private set; }

        private void Awake()
        {
            if (!rules) throw new InvalidOperationException($"{name} 缺少战斗状态规则");
            Controller = new StatusController(rules, GetComponent<CharacterResources>().Container,
                GetComponent<CharacterEffects>().Controller, GetComponent<CharacterStats>().Stats);
        }

        private void OnDestroy() => Controller?.Dispose();
    }
}
