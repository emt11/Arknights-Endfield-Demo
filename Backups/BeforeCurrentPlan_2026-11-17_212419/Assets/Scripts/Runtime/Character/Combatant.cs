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
        [SerializeField, Min(.01f)] private float bodyRadius = .4f;
        [SerializeField] private Vector3 hitOrigin = Vector3.up;

        public CharacterStats Stats { get; private set; }
        public CharacterHealth Health { get; private set; }
        public string DisplayName => characterAsset.DisplayName;
        public int UnitId { get; internal set; }
        public CharacterMotor Motor { get; private set; }
        public AnimationPlayer Animation { get; private set; }
        public ActionController Actions { get; private set; }
        public WeaponController Weapon { get; private set; }
        public ComboController Combo { get; internal set; }
        public TargetingController Targeting { get; internal set; }
        public float BodyRadius => bodyRadius;
        public CharacterDefinition Definition => characterAsset;
        public int Team => team;
        public Vector3 HitOrigin => transform.TransformPoint(hitOrigin);

        private void Awake()
        {
            Stats = new CharacterStats(characterAsset.Attack, characterAsset.MaxHP, characterAsset.Defense);
            Health = new CharacterHealth(Stats);
            Motor = GetComponent<CharacterMotor>();
            Animation = GetComponent<AnimationPlayer>();
            Actions = GetComponent<ActionController>();
            Weapon = GetComponent<WeaponController>();
            Health.DiedInternally += OnDeath;
        }

        private void OnDeath()
        {
            if (Actions != null && Actions.Current != null)
                Actions.Cancel(Actions.Current.ExecutionId, ActionEndReason.Interrupted);
        }

        private void OnDestroy() => Health?.Dispose();
    }
}
