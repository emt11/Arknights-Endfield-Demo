using UnityEngine;

namespace Endfield.Controller
{
    [RequireComponent(typeof(CharacterMotor))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private GameInputManager inputManager;
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerCameraTarget cameraTarget;
        [SerializeField, Min(0.01f)] private float walkSpeed = 2f;
        [SerializeField, Min(0.01f)] private float runSpeed = 5f;
        [SerializeField, Min(0.01f)] private float walkReferenceSpeed = 2f;
        [SerializeField, Min(0.01f)] private float runReferenceSpeed = 5f;
        private static readonly int moveAmountHash = Animator.StringToHash("MoveAmount");
        private static readonly int turnAngleHash = Animator.StringToHash("TurnAngle");
        private static readonly int playbackRateHash = Animator.StringToHash("PlaybackRate");
        private CharacterMotor motor;
        private LocomotionStateMachine stateMachine;
        private float targetSpeed;

        public float WalkSpeed => walkSpeed;
        public float RunSpeed => runSpeed;
        public LocomotionStateId CurrentState => stateMachine.Current.Id;
        public float MoveAmount { get; private set; }
        public float PlaybackRate { get; private set; } = 1f;
        public Vector3 WorldMove { get; private set; }
        public Vector3 LocalMove { get; private set; }
        public float TurnAngle { get; private set; }

        private void Awake()
        {
            motor = GetComponent<CharacterMotor>();
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            animator.speed = 1f;
            stateMachine = new LocomotionStateMachine(this);
        }

        private void OnEnable()
        {
            ResetLocomotion();
            motor.ResetMotion();
            cameraTarget.Initialize(transform);
            stateMachine.Start();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
                return;

            GameplayInputFrame frame = inputManager.Frame;
            cameraTarget.ApplyLook(frame.Look);
            CalculateInputDirection(frame.Move);
            stateMachine.Tick(frame.Move.sqrMagnitude > 0f, frame.RunHeld);
            motor.Tick(targetSpeed, TurnAngle, deltaTime);
            SetupAnimator();
            cameraTarget.SyncPosition();
        }

        private void CalculateInputDirection(Vector2 moveInput)
        {
            WorldMove = cameraTarget.PlanarRotation * new Vector3(moveInput.x, 0f, moveInput.y);
            LocalMove = transform.InverseTransformDirection(WorldMove);
            TurnAngle = LocalMove.sqrMagnitude > 0f ? Mathf.Atan2(LocalMove.x, LocalMove.z) : 0f;
        }

        public void SetTargetSpeed(float value) => targetSpeed = value;

        private void SetupAnimator()
        {
            float speed = motor.CurrentSpeed;
            if (speed == 0f)
            {
                MoveAmount = 0f;
                PlaybackRate = 1f;
            }
            else if (speed <= walkSpeed)
            {
                MoveAmount = speed / walkSpeed;
                PlaybackRate = walkSpeed / walkReferenceSpeed;
            }
            else
            {
                float runWeight = Mathf.InverseLerp(walkSpeed, runSpeed, speed);
                MoveAmount = 1f + runWeight;
                float referenceSpeed = Mathf.Lerp(walkReferenceSpeed, runReferenceSpeed, runWeight);
                PlaybackRate = speed / referenceSpeed;
            }

            // 同一代码速度驱动混合与倍率，不再使用 SetFloat 的阻尼重载
            animator.SetFloat(moveAmountHash, MoveAmount);
            animator.SetFloat(turnAngleHash, TurnAngle);
            animator.SetFloat(playbackRateHash, PlaybackRate);
        }

        private void OnDisable()
        {
            stateMachine.Stop();
            ResetLocomotion();
            motor.ResetMotion();
        }

        private void ResetLocomotion()
        {
            targetSpeed = 0f;
            MoveAmount = 0f;
            PlaybackRate = 1f;
            WorldMove = Vector3.zero;
            LocalMove = Vector3.zero;
            TurnAngle = 0f;
            animator.SetFloat(moveAmountHash, 0f);
            animator.SetFloat(turnAngleHash, 0f);
            animator.SetFloat(playbackRateHash, 1f);
        }

        private void OnValidate()
        {
            walkSpeed = Mathf.Max(0.01f, walkSpeed);
            runSpeed = Mathf.Max(walkSpeed + 0.01f, runSpeed);
            walkReferenceSpeed = Mathf.Max(0.01f, walkReferenceSpeed);
            runReferenceSpeed = Mathf.Max(0.01f, runReferenceSpeed);
        }
    }
}
