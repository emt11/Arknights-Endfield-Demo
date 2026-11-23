namespace Endfield.Pooling
{
    public readonly struct PoolLease
    {
        private readonly ScenePrefabPool owner;
        private readonly int slotIndex;
        private readonly ulong version;

        internal PoolLease(ScenePrefabPool owner, int slotIndex, ulong version)
        {
            this.owner = owner;
            this.slotIndex = slotIndex;
            this.version = version;
        }

        public bool IsActive => owner && owner.IsCurrent(slotIndex, version);

        public bool TryRelease() => owner && owner.TryRelease(slotIndex, version);
    }
}
