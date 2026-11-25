using System;

namespace Endfield.CharacterControl
{
    [Flags]
    public enum CharacterCommand
    {
        None = 0,
        Attack = 1,
        Dodge = 2
    }
}
