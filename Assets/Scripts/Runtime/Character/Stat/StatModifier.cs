namespace Endfield.Character
{
    public enum ModifierOperation
    {
        Flat,
        PercentAdd
    }

    public sealed class StatModifier
    {
        public ModifierOperation Operation { get; }
        public float Value { get; }
        public object Source { get; }

        internal StatModifier(ModifierOperation operation, float value, object source)
        {
            Operation = operation;
            Value = value;
            Source = source;
        }
    }
}
