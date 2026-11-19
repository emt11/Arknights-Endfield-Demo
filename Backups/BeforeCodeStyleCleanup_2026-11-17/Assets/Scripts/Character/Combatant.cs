using System;
using Endfield.Combat;
using Endfield.Controller;
using UnityEngine;

namespace Endfield.Character
{
    [DisallowMultipleComponent]
    public sealed class Combatant : MonoBehaviour
    {
        [SerializeField] private CharacterDefinition characterAsset;
        [SerializeField] private int team;
        private Hurtbox[] hurtboxes;

        public CharacterStats Stats { get; private set; }
        public CharacterHealth Health { get; private set; }
        public string DisplayName => characterAsset.DisplayName;
        public int UnitId { get; internal set; }
        public CharacterMotor Motor { get; private set; }
        public AnimationPlayer Animation { get; private set; }
        public ActionController Actions { get; private set; }
        public ComboController Combo { get; internal set; }
        public TargetingController Targeting { get; internal set; }
        [SerializeField, Min(.01f)] private float bodyRadius = .4f;
        public float BodyRadius => bodyRadius;
        public CharacterDefinition Definition => characterAsset;
        public int Team => team;
        public Vector3 StepStartPosition { get; internal set; }
        public Quaternion StepStartRotation { get; internal set; }

        private void Awake()
        {
            if (characterAsset == null)
                throw new InvalidOperationException($"{name} requires a CharacterDefinition");

            Stats = new CharacterStats(characterAsset.Attack, characterAsset.MaxHP, characterAsset.Defense);
            Health = new CharacterHealth(Stats);
            Motor = GetComponent<CharacterMotor>();
            Animation = GetComponent<AnimationPlayer>();
            Actions = GetComponent<ActionController>();
            hurtboxes = GetComponentsInChildren<Hurtbox>(true);
            Health.DiedInternally += OnDeath;
        }

        private void OnDeath()
        {
            if (Actions != null && Actions.Current != null)
                Actions.Cancel(Actions.Current.ExecutionId, ActionEndReason.Interrupted);

            foreach (Hurtbox hurtbox in hurtboxes)
                hurtbox.GetComponent<Collider>().enabled = false;
        }

        private void OnDestroy() => Health?.Dispose();
    }
}
