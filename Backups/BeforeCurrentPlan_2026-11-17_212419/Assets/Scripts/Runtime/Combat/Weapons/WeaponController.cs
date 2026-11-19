using System;
using System.Collections.Generic;
using UnityEngine;

namespace Endfield.Combat
{
    [DisallowMultipleComponent]
    public sealed class WeaponController : MonoBehaviour
    {
        [SerializeField] private Transform socket;
        [SerializeField] private GameObject weaponPrefab;
        private GameObject instance;
        private Hitbox[] hitboxes = Array.Empty<Hitbox>();
        private readonly Dictionary<int, Hitbox> hitboxesById = new Dictionary<int, Hitbox>();

        public Transform Socket => socket;
        public GameObject WeaponPrefab => weaponPrefab;
        public IReadOnlyList<Hitbox> Hitboxes => hitboxes;

        private void Awake() => Equip(weaponPrefab, socket);

        public void Equip(GameObject prefab, Transform attachment)
        {
            Unequip();
            weaponPrefab = prefab;
            socket = attachment;
            instance = Instantiate(prefab, attachment, false);
            hitboxes = instance.GetComponentsInChildren<Hitbox>(true);
            foreach (Hitbox hitbox in hitboxes)
                hitboxesById.Add(hitbox.id, hitbox);
            CaptureStart();
        }

        public void Unequip()
        {
            if (instance != null)
            {
                instance.SetActive(false);
                Destroy(instance);
                instance = null;
            }

            hitboxes = Array.Empty<Hitbox>();
            hitboxesById.Clear();
        }

        public Hitbox GetHitbox(int id) => hitboxesById[id];

        public void CaptureStart()
        {
            foreach (Hitbox hitbox in hitboxes)
                hitbox.CaptureStart();
        }

        public void CaptureEnd()
        {
            foreach (Hitbox hitbox in hitboxes)
                hitbox.CaptureEnd();
        }

        private void OnDestroy() => Unequip();
    }
}
