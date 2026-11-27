using System;
using Endfield.MotionWarping;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Endfield.Animation
{
    [RequireComponent(typeof(Animator)), DisallowMultipleComponent]
    public sealed class AnimationSource : MonoBehaviour
    {
        [SerializeField] private RuntimeAnimatorController locomotionController;
        private Animator model;
        private PlayableGraph graph;
        private AnimatorControllerPlayable controller;
        private int activeStateHash;
        private double clipSpeed;
        private Quaternion naturalYaw;
        private bool captureMotion;
        private (Vector3 Position, Quaternion Rotation) rootMotion;

        public Animator Model => model;
        public RuntimeAnimatorController LocomotionController => locomotionController;
        internal AnimationClip ActiveClip { get; private set; }

        public void Initialize()
        {
            if (graph.IsValid()) return;
            model = GetComponent<Animator>();
            model.applyRootMotion = true;
            graph = PlayableGraph.Create(model.name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            controller = AnimatorControllerPlayable.Create(graph, locomotionController);
            AnimationPlayableOutput.Create(graph, "角色姿态", model).SetSourcePlayable(controller);
            graph.Play();
            Advance(0);
        }

        internal bool HasState(int stateHash) => controller.HasState(0, stateHash);

        internal void SetBool(int parameter, bool value) => controller.SetBool(parameter, value);

        internal void SetFloat(int parameter, float value) => controller.SetFloat(parameter, value);

        public void Advance(double actionSeconds)
        {
            // 手动让 Unity 动画图前进 actionSeconds 秒，并计算这一步的动画输出，更新角色模型的姿态
            graph.Evaluate((float)actionSeconds);
        }

        // 先应用状态转换，再绑定基础层实际选中的单次片段
        internal void BeginClip(int stateHash)
        {
            ActiveClip = null;
            Advance(0);
            var state = ReadState(stateHash, out bool next);
            double speed = (double)state.speed * state.speedMultiplier;
            if (state.fullPathHash != stateHash || !(speed > 0) || double.IsInfinity(speed)) return;
            var clips = next ? controller.GetNextAnimatorClipInfo(0) : controller.GetCurrentAnimatorClipInfo(0);
            if (clips.Length != 1) return;
            var clip = clips[0].clip;
            if (!clip || !clip.humanMotion || clip.isLooping || clip.length <= 0) return;
            activeStateHash = stateHash;
            clipSpeed = speed;
            naturalYaw = Quaternion.identity;
            ActiveClip = clip;
        }

        internal (double ActionSeconds, double ClipTime, PlanarMotion Motion) EvaluateSegment(
            double from, double remaining, bool splitAtFrame, bool readMotion, Quaternion rootRotation)
        {
            double duration = ActiveClip.length;
            double boundary = duration;
            if (splitAtFrame)
            {
                double frameRate = ActiveClip.frameRate;
                double nextFrame = Math.Floor(from * frameRate + 1e-9) + 1;
                boundary = Math.Min(nextFrame / frameRate, duration);
            }
            double actionSeconds = Math.Min(remaining, (boundary - from) / clipSpeed);
            if (readMotion) rootMotion = (Vector3.zero, Quaternion.identity);
            captureMotion = readMotion;
            try { Advance(actionSeconds); }
            finally { captureMotion = false; }
            double clipTime = (double)Mathf.Clamp01(ReadState(activeStateHash, out _).normalizedTime) * duration;
            // 在帧边界对齐 Controller 单精度进度的浮点误差
            if (Math.Abs(clipTime - boundary) <= 1e-6) clipTime = boundary;

            if (!readMotion) return (actionSeconds, clipTime, default);
            Quaternion worldYaw = Quaternion.Euler(0, rootRotation.eulerAngles.y, 0);
            // 还原到动作起点坐标系，保留动画本身累计的偏航
            Vector3 displacement = naturalYaw * (Quaternion.Inverse(worldYaw) * rootMotion.Position);
            displacement.y = 0;
            naturalYaw = Quaternion.Euler(0, (naturalYaw * rootMotion.Rotation).eulerAngles.y, 0);
            return (actionSeconds, clipTime, new PlanarMotion(displacement, naturalYaw));
        }

        private AnimatorStateInfo ReadState(int stateHash, out bool next)
        {
            // 退出混合中再次进入时，两侧可以是同一个状态，目标侧才是本次播放
            var destination = controller.GetNextAnimatorStateInfo(0);
            next = destination.fullPathHash == stateHash;
            return next ? destination : controller.GetCurrentAnimatorStateInfo(0);
        }

        private void OnAnimatorMove()
        {
            if (captureMotion) rootMotion = (model.deltaPosition, model.deltaRotation);
        }

        internal void EndClip()
        {
            ActiveClip = null;
            Advance(0);
        }

        private void OnEnable()
        {
            if (graph.IsValid()) Advance(0);
        }

        private void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
