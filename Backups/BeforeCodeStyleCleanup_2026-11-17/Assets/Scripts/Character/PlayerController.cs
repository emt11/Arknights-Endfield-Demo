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
        public Combatant InitialCharacter => initialCharacter;
        public Vector3 WorldMove { get; private set; }

        private void OnEnable() => cameraTarget.Initialize(Character.transform);

        private void Update()
        {
            GameplayInputFrame frame = inputManager.Frame;
            // 1. cameraTarget 应用旋转
            cameraTarget.ApplyLook(frame.Look);
            // 2. 将二维移动输入转换为相对于镜头朝向的世界空间移动方向
            WorldMove = cameraTarget.PlanarRotation * new Vector3(frame.Move.x, 0f, frame.Move.y);
            // 3. 准备移动所需数据，交给 Motor 执行位移
            Character.Motor.SetIntent(WorldMove, frame.RunHeld);
            // 4. 检查攻击
            if (frame.AttackPressed)
                simulation.QueueAttack(Time.frameCount);
        }

        public void SubmitAction()
        {
            if (simulation.PlayerInputs.TryPeek(CombatInputKind.Attack, out CombatInput input) && Character.Combo.TryAttack(out long executionId))
            {
                simulation.PlayerInputs.Consume(input.Sequence);
                Character.Actions.ProcessInitialPoints(executionId);
                return;
            }

            ActionRuntime current = Character.Actions.Current;
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
