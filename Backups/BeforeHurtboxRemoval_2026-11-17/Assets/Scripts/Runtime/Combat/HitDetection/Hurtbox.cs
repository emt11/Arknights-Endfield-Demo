using Endfield.Character;
using UnityEngine;

namespace Endfield.Combat
{
    [RequireComponent(typeof(Collider))]
    public sealed class Hurtbox : MonoBehaviour
    {
        public Combatant Owner { get; private set; }

        private void Awake() => Owner = GetComponentInParent<Combatant>();
    }
}
