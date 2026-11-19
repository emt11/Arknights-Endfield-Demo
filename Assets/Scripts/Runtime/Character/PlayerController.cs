using System;
using Endfield.Character;
using UnityEngine;

namespace Endfield.Controller
{
    [DefaultExecutionOrder(-50)]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private GameInputManager inputManager;
        [SerializeField] private CustomThirdPersonCamera playerCamera;
        [SerializeField] private Combatant initialCharacter;

        public Combatant Character => initialCharacter;
        public Vector3 WorldMove { get; private set; }
        internal event Action<int> AttackRequested;
        internal event Action Disabled;

        private void OnEnable() => playerCamera.Initialize(Character.transform);

        private void Update()
        {
            GameplayInputFrame frame = inputManager.Frame;
            playerCamera.ApplyLook(frame.look);
            WorldMove = playerCamera.PlanarRotation * new Vector3(frame.move.x, 0f, frame.move.y);
            Character.Motor.SetIntent(WorldMove, frame.runHeld);
            if (frame.attackPressed)
                AttackRequested?.Invoke(Time.frameCount);
        }

        private void OnDisable()
        {
            WorldMove = Vector3.zero;
            Character.Motor.SetIntent(Vector3.zero, false);
            Disabled?.Invoke();
        }
    }
}
