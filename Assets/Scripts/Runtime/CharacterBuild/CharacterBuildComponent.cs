using System;
using Endfield.Stats;
using UnityEngine;

namespace Endfield.CharacterBuild
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterStats))]
    [DefaultExecutionOrder(-75)]
    public sealed class CharacterBuildComponent : MonoBehaviour
    {
        [SerializeField] private CharacterBuildProfile profile;

        public CharacterBuildState State { get; private set; }

        private void Awake()
        {
            if (!profile) throw new InvalidOperationException($"{name} 缺少角色构筑配置");
            State = CharacterBuildState.Create(GetComponent<CharacterStats>().Stats, profile);
        }
    }
}
