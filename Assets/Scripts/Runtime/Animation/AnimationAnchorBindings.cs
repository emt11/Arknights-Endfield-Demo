using System;
using UnityEngine;

namespace Endfield.AnimationSystem
{
    public enum AnimationAnchor
    {
        ModelRoot = 0,
        LeftHand = 1,
        RightHand = 2,
        LeftFoot = 3,
        RightFoot = 4,
        Head = 5,
        Hips = 6,
        Spine = 7,
        Weapon = 8
    }

    [DisallowMultipleComponent, RequireComponent(typeof(Animator))]
    public sealed class AnimationAnchorBindings : MonoBehaviour
    {
        [Serializable]
        private struct Binding
        {
            public AnimationAnchor point;
            public Transform target;
        }

        [SerializeField, Tooltip("将部位绑定到当前模型的 Transform，未指定的人体部位使用 Humanoid 骨骼")]
        private Binding[] bindings = Array.Empty<Binding>();

        internal void Bind(AnimationAnchor point, Transform target)
        {
            for (int index = 0; index < bindings.Length; index++)
            {
                if (bindings[index].point != point)
                    continue;

                bindings[index].target = target;
                return;
            }
            Array.Resize(ref bindings, bindings.Length + 1);
            bindings[bindings.Length - 1] = new Binding { point = point, target = target };
        }

        public static Transform Resolve(Transform model, AnimationAnchor point)
        {
            if (model.TryGetComponent(out AnimationAnchorBindings anchors))
            {
                foreach (Binding binding in anchors.bindings)
                {
                    if (binding.point == point && binding.target != null)
                        return binding.target;
                }
            }

            if (point == AnimationAnchor.ModelRoot)
                return model;

            HumanBodyBones bone = point switch
            {
                AnimationAnchor.LeftHand => HumanBodyBones.LeftHand,
                AnimationAnchor.RightHand => HumanBodyBones.RightHand,
                AnimationAnchor.LeftFoot => HumanBodyBones.LeftFoot,
                AnimationAnchor.RightFoot => HumanBodyBones.RightFoot,
                AnimationAnchor.Head => HumanBodyBones.Head,
                AnimationAnchor.Hips => HumanBodyBones.Hips,
                AnimationAnchor.Spine => HumanBodyBones.Spine,
                _ => HumanBodyBones.LastBone
            };
            if (bone != HumanBodyBones.LastBone && model.TryGetComponent(out Animator animator) && animator.isHuman)
            {
                Transform target = animator.GetBoneTransform(bone);
                if (target != null)
                    return target;
            }

            throw new InvalidOperationException($"模型 {model.name} 无法解析挂点 {point}，请在 AnimationAnchorBindings 中绑定 Transform");
        }
    }
}
