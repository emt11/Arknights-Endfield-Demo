using Endfield.CombatSession;
using Endfield.Controller;
using Endfield.Input;
using UnityEngine;

namespace Endfield.CharacterControl
{
    /// <summary>读取本地输入帧，更新相机并将操控指令提交给场景指定的角色</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-250)]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private GameInputManager inputManager;
        [SerializeField] private ThirdPersonCamera view;
        [SerializeField] private PartyMemberControl controlledMember;
        private ActorStepBinding binding;

        private void Awake() => binding = controlledMember.GetComponent<ActorStepBinding>();

        private void OnEnable()
        {
            inputManager.GameplayInputCleared += ClearCharacterInput;
            binding.ActivationChanged += inputManager.ClearPendingPresses;
            inputManager.ClearPendingPresses();
            view.Initialize(controlledMember.transform);
        }

        /// <summary>
        /// 读取输入管理器本帧采样的结果，在战斗时钟推进前将输入提交给角色
        /// </summary>
        private void Update()
        {
            if (!binding.isActiveAndEnabled) return;
            var frame = inputManager.Frame;
            // 先更新镜头，再用本帧更新后的水平朝向把移动输入换算成世界方向
            view.ApplyLook(frame.LookDelta);
            Vector3 world = view.PlanarRotation * new Vector3(frame.Move.x, 0f, frame.Move.y);
            controlledMember.SetControlInput(world, frame.SprintHeld, frame.Pressed);
        }

        private void ClearCharacterInput()
        {
            if (controlledMember) controlledMember.ClearControlInput();
        }

        private void OnDisable()
        {
            inputManager.GameplayInputCleared -= ClearCharacterInput;
            binding.ActivationChanged -= inputManager.ClearPendingPresses;
            ClearCharacterInput();
            if (view) view.Initialize(null);
        }
    }
}
