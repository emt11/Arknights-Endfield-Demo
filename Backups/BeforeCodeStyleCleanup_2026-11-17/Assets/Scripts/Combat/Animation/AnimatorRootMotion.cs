using UnityEngine;

namespace Endfield.Combat
{
    [ExecuteAlways]
    public sealed class AnimatorRootMotion : MonoBehaviour
    {
        private Animator animator;
        public Vector3 DeltaPosition { get; private set; }
        public Quaternion DeltaRotation { get; private set; } = Quaternion.identity;

        private void Awake() => animator = GetComponent<Animator>();

        private void OnAnimatorMove()
        {
            DeltaPosition = animator.deltaPosition;
            DeltaRotation = animator.deltaRotation;
        }

        public void Clear()
        {
            DeltaPosition = Vector3.zero;
            DeltaRotation = Quaternion.identity;
        }
    }
}
