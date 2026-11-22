using System;
using UnityEngine;

namespace Endfield.Stats
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class CharacterStats : MonoBehaviour
    {
        [SerializeField] private CharacterStatProfile profile;

        public StatSet Stats { get; private set; }
        public string DisplayName => profile.DisplayName;

        private void Awake()
        {
            if (!profile) throw new InvalidOperationException($"{name} 缺少角色属性配置");
            Stats = profile.CreateSet();
        }
    }
}
