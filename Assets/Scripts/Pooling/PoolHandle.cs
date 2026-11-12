namespace Endfield.Pooling
{
    public readonly struct PoolHandle
    {
        private readonly PooledObject instance;
        private readonly ulong version;

        internal PoolHandle(PooledObject instance, ulong version)
        {
            this.instance = instance;
            this.version = version;
        }

        public bool IsValid => instance != null && instance.Matches(version);

        public void Return()
        {
            if (IsValid)
                instance.Return(version);
        }
    }
}