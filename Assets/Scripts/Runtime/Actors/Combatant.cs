using System;
using Endfield.CharacterBuild;
using Endfield.CombatContracts;
using Endfield.Effects;
using Endfield.Resources;
using Endfield.Stats;
using Endfield.Status;
using UnityEngine;

namespace Endfield.Actors
{
    public enum Faction { Player, Enemy }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterBuildComponent), typeof(CharacterStatus))]
    public sealed class Combatant : MonoBehaviour
    {
        [SerializeField] private string displayName;
        [SerializeField] private Faction faction;
        [SerializeField] private Animator modelAnimator;
        private SceneActors owner;

        public string DisplayName => displayName;
        public Faction Faction => faction;
        public Animator ModelAnimator => modelAnimator;
        public ActorRuntime Runtime { get; private set; }

        internal void Bind(SceneActors sceneActors, ActorId id)
        {
            if (string.IsNullOrWhiteSpace(displayName)) throw new InvalidOperationException($"{name} 缺少角色显示名");
            if (!modelAnimator) throw new InvalidOperationException($"{name} 缺少模型 Animator");
            var build = GetComponent<CharacterBuildComponent>().State;
            var stats = GetComponent<CharacterStats>().Stats;
            var resources = GetComponent<CharacterResources>().Container;
            var effects = GetComponent<CharacterEffects>().Controller;
            var status = GetComponent<CharacterStatus>().Controller;
            if (build == null || stats == null || resources == null || effects == null || status == null)
                throw new InvalidOperationException($"{name} 的角色组件尚未完成初始化");

            Runtime = new ActorRuntime(id, build, stats, resources, effects, status);
            owner = sceneActors;
        }

        internal void Unbind()
        {
            Runtime.Life.Dispose();
            Runtime = null;
            owner = null;
        }

        private void OnDestroy()
        {
            if (owner) owner.Unregister(this);
        }
    }
}
