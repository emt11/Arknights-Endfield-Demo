using UnityEngine;

namespace Endfield.CharacterMotion
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class CharacterMotor : MonoBehaviour
    {
        private CharacterController capsule;

        private void Awake() => capsule = GetComponent<CharacterController>();

        public Vector3 MovePlanar(Vector3 delta, Vector3 facing, float maxTurnDegrees)
        {
            facing.y = 0;
            if (facing.sqrMagnitude > 0)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), maxTurnDegrees);

            delta.y = 0;
            Vector3 before = transform.position;
            if (delta.sqrMagnitude > 0) capsule.Move(delta);
            Vector3 actual = transform.position - before;
            actual.y = 0;
            return actual;
        }
    }
}
