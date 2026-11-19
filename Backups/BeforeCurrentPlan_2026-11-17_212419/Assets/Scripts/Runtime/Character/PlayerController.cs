using Endfield.Character;
using Endfield.Combat;
using UnityEngine;

namespace Endfield.Controller
{
    [DefaultExecutionOrder(-50)]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private GameInputManager inputManager;
        [SerializeField] private PlayerCameraTarget cameraTarget;
        [SerializeField] private CombatSimulation simulation;
        [SerializeField] private Combatant initialCharacter;

        public Combatant Character => initialCharacter;
        public Vector3 WorldMove { get; private set; }

        private void OnEnable() => cameraTarget.Initialize(Character.transform);

        private void Update()
        {
            GameplayInputFrame frame = inputManager.Frame;
            cameraTarget.ApplyLook(frame.look);
            WorldMove = cameraTarget.PlanarRotation * new Vector3(frame.move.x, 0f, frame.move.y);
            Character.Motor.SetIntent(WorldMove, frame.runHeld);
            if (frame.attackPressed)
                simulation.QueueAttack(Time.frameCount);
        }

        public void SubmitAction()
        {
            if (simulation.PlayerInputs.TryPeek(CombatInputKind.Attack, out CombatInput input) && Character.Combo.TryAttack(out long executionId))
            {
                simulation.PlayerInputs.Consume(input.sequence);
                Character.Actions.ProcessInitialPoints(executionId);
                return;
            }

            ActionRuntime current = Character.Actions.Current;
            // 移动输入在取消窗口中可以取消这次动作
            if (WorldMove.sqrMagnitude > 0f && current != null && Character.Actions.CanCancel(ActionCategory.Move))
                Character.Actions.Cancel(current.ExecutionId, ActionEndReason.Cancelled);
        }

        private void LateUpdate() => cameraTarget.SyncPosition();

        private void OnDisable()
        {
            WorldMove = Vector3.zero;
            Character.Motor.SetIntent(Vector3.zero, false);
        }
    }
}
