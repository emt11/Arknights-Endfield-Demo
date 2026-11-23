using System;
using System.Collections.Generic;
using Endfield.Common;
using UnityEngine;

namespace Endfield.Pooling
{
    [DisallowMultipleComponent]
    public sealed class ScenePrefabPool : MonoBehaviour
    {
        [SerializeField] private GameObject prefab;
        [SerializeField, Min(1)] private int capacity = 1;
        [SerializeField, Min(0)] private int prewarmCount;

        private sealed class Slot
        {
            public GameObject Instance;
            public IPoolReset[] Resets;
            public ulong Version;
            public bool Rented;
        }

        private readonly Stack<int> available = new();
        private Slot[] slots;
        private int assignedCount; // 已分配的槽位数
        private Transform creationRoot;
        private bool accepting;
        private bool recycling;

        private void Awake()
        {
            if (!prefab) throw new InvalidOperationException($"{name} 缺少对象池 Prefab");
            NumericValidation.RequireFiniteInRange(capacity, 1, int.MaxValue, nameof(capacity));
            NumericValidation.RequireFiniteInRange(prewarmCount, 0, capacity, nameof(prewarmCount));

            var staging = new GameObject("创建节点");
            staging.SetActive(false);
            creationRoot = staging.transform;
            creationRoot.SetParent(transform, false); // 对象根节点在 ScenePrefabPool 挂载对象下
            slots = new Slot[capacity];
            for (int i = 0; i < prewarmCount; i++)
            {
                var slot = new Slot();
                slots[i] = slot;
                CreateInstance(slot);
                assignedCount++;
                available.Push(i);
            }
        }

        private void OnEnable()
        {
            accepting = slots != null;
        }

        private void OnDisable()
        {
            accepting = false;
            RecycleAll();
        }

        public bool TryRent(Action<GameObject, PoolLease> configure, out PoolLease lease)
        {
            lease = default;
            if (!accepting || recycling) return false;

            int index = TakeSlot();
            if (index < 0) return false;

            var slot = slots[index];
            if (!slot.Instance)
            {
                try { CreateInstance(slot); }
                catch
                {
                    available.Push(index);
                    throw;
                }
            }

            slot.Rented = true;
            var current = new PoolLease(this, index, ++slot.Version);
            try
            {
                configure?.Invoke(slot.Instance, current);
                if (!current.IsActive) return false;
                slot.Instance.SetActive(true);
            }
            catch
            {
                current.TryRelease();
                throw;
            }

            // 配置和激活都可能同步归还租约，再次借出的槽位属于新持有人
            if (!current.IsActive) return false;
            lease = current;
            return true;
        }

        private int TakeSlot()
        {
            if (available.Count > 0) return available.Pop();
            if (assignedCount < slots.Length)
            {
                slots[assignedCount] = new Slot();
                return assignedCount++;
            }

            // 正常归还会直接入栈，仅在池满时寻找借出期间被外部销毁的实例
            for (int i = 0; i < assignedCount; i++)
            {
                var slot = slots[i];
                if (slot.Rented && !slot.Instance)
                {
                    slot.Rented = false;
                    slot.Resets = null;
                    return i;
                }
            }
            return -1;
        }

        private void CreateInstance(Slot slot)
        {
            var instance = Instantiate(prefab, creationRoot, false); // 先挂载到这里是因为这里默认是未激活状态
            instance.SetActive(false);
            instance.transform.SetParent(transform, false); // 之后再移动到 ScenePrefabPool 组件下
            slot.Resets = instance.GetComponentsInChildren<IPoolReset>(true);
            slot.Instance = instance;
        }

        internal bool IsCurrent(int index, ulong version)
        {
            var slot = slots[index];
            return slot.Rented && slot.Version == version && slot.Instance;
        }

        internal bool TryRelease(int index, ulong version)
        {
            if (!IsCurrent(index, version)) return false;

            var slot = slots[index];
            slot.Rented = false;
            var instance = slot.Instance;
            instance.SetActive(false);

            bool reusable = true;
            foreach (var reset in slot.Resets)
            {
                try { reset.ResetForReuse(); }
                catch (Exception error)
                {
                    reusable = false;
                    Debug.LogError($"{name} 的 {reset.GetType().Name} 重置失败，丢弃实例 {instance.name}\n{error}", this);
                }
            }

            if (reusable && instance)
            {
                var root = instance.transform;
                root.SetParent(transform, false);
                root.SetLocalPositionAndRotation(prefab.transform.localPosition, prefab.transform.localRotation);
                root.localScale = prefab.transform.localScale;
            }
            else
            {
                if (instance) Destroy(instance);
                slot.Instance = null;
                slot.Resets = null;
            }
            available.Push(index);
            return true;
        }

        public void RecycleAll()
        {
            if (recycling) return;
            recycling = true;
            try
            {
                for (int i = 0; i < assignedCount; i++)
                {
                    var slot = slots[i];
                    if (!slot.Rented) continue;
                    if (!slot.Instance)
                    {
                        slot.Rented = false;
                        slot.Resets = null;
                        available.Push(i);
                    }
                    else TryRelease(i, slot.Version);
                }
            }
            finally { recycling = false; }
        }

        private void OnDestroy()
        {
            // 只移除池组件时，宿主节点依然存活，池创建的子节点也要清理
            for (int i = 0; i < assignedCount; i++)
            {
                if (slots[i].Instance) Destroy(slots[i].Instance);
            }
            if (creationRoot) Destroy(creationRoot.gameObject);
        }
    }
}
