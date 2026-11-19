#if UNITY_EDITOR
using UnityEngine;

namespace Endfield.Validation
{
    [ExecuteAlways]
    public sealed class ProbeRootMotion : MonoBehaviour
    {
        public int Calls { get; private set; }
        public Vector3 Delta { get; private set; }
        public Quaternion Rotation { get; private set; }

        private void OnAnimatorMove()
        {
            Animator animator = GetComponent<Animator>();
            Calls++;
            Delta = animator.deltaPosition;
            Rotation = animator.deltaRotation;
        }
    }
}
#endif
