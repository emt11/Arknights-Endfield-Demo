using System;
using Endfield.Stats;
using UnityEngine;

namespace Endfield.Resources
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterStats))]
    [DefaultExecutionOrder(-50)]
    public sealed class CharacterResources : MonoBehaviour
    {
        [SerializeField] private CharacterResourceProfile profile;

        public CharacterResourceContainer Container { get; private set; }

        private void Awake()
        {
            if (!profile) throw new InvalidOperationException($"{name} 缺少角色资源配置");
            Container = profile.CreateContainer(GetComponent<CharacterStats>().Stats);
        }

        private void OnDestroy()
        {
            Container?.Dispose();
        }
    }
}
