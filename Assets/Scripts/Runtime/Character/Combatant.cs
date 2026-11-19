using Endfield.AnimationSystem;
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

        [SerializeField, Min(.01f)] private float walkReferenceSpeed = 2f;
        [SerializeField, Min(.01f)] private float runReferenceSpeed = 5f;

        public CharacterStats Stats { get; private set; }
        public CharacterHealth Health { get; private set; }
        public string DisplayName => characterAsset.DisplayName;
        public int UnitId { get; internal set; }
        public CharacterMotor Motor { get; private set; }
        public AnimationPlayer Animation { get; private set; }
        internal AnimationPlayback EvaluatedPlayback { get; set; }
        public ActionController Actions { get; private set; }
        public WeaponController Weapon { get; private set; }
        public ComboController Combo { get; internal set; }
        public TargetingController Targeting { get; internal set; }
        public float BodyRadius => bodyRadius;
        public CharacterDefinition Definition => characterAsset;
        public int Team => team;
        public Vector3 HitOrigin => transform.TransformPoint(hitOrigin);
        internal bool CanSimulateMovement => isActiveAndEnabled && Motor != null && Motor.isActiveAndEnabled && Animation.isActiveAndEnabled;
        internal bool CanExecuteAction => CanSimulateMovement && Actions != null && Actions.isActiveAndEnabled && !Health.IsDead;

        private void Awake()
        {
            Stats = new CharacterStats(characterAsset.Attack, characterAsset.MaxHP, characterAsset.Defense);
            Health = new CharacterHealth(Stats);
            Motor = GetComponent<CharacterMotor>();
            Animation = GetComponent<AnimationPlayer>();
            Actions = GetComponent<ActionController>();
            Weapon = GetComponent<WeaponController>();
            Health.DiedInternally += InterruptAction;
            if (Motor != null)
                Motor.Disabled += InterruptAction;
            if (Animation != null)
                Animation.Disabled += OnAnimationDisabled;
        }

        private void InterruptAction()
        {
            if (Actions != null && Actions.Current != null)
                Actions.End(Actions.Current.ExecutionId, ActionEndReason.Interrupted);
        }

        internal void UpdateLocomotionParameters()
        {
            float speed = Motor.LocomotionSpeed;
            float weight = Mathf.InverseLerp(Motor.WalkSpeed, Motor.RunSpeed, speed);
            float moveAmount = speed <= Motor.WalkSpeed ? speed / Motor.WalkSpeed : 1f + weight;
            float rate = speed <= Motor.WalkSpeed ? Motor.WalkSpeed / walkReferenceSpeed :
                speed / Mathf.Lerp(walkReferenceSpeed, runReferenceSpeed, weight);
            Animation.SetLocomotion(moveAmount, speed == 0f ? 1f : rate);
        }

        private void OnAnimationDisabled()
        {
            InterruptAction();
            Motor?.ResetMotion();
        }

        private void OnDestroy()
        {
            if (Motor != null)
                Motor.Disabled -= InterruptAction;
            if (Animation != null)
                Animation.Disabled -= OnAnimationDisabled;
            Health?.Dispose();
        }

        private void OnDisable()
        {
            InterruptAction();
            Motor?.ResetMotion();
        }
    }
}
