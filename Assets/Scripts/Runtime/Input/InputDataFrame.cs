using Endfield.CharacterControl;
using UnityEngine;

namespace Endfield.Input
{
    public readonly struct InputDataFrame
    {
        public readonly Vector2 Move;
        public readonly Vector2 LookDelta;
        public readonly bool SprintHeld;
        public readonly CharacterCommand Pressed;

        public InputDataFrame(Vector2 move, Vector2 lookDelta, bool sprintHeld, CharacterCommand pressed)
        {
            Move = move;
            LookDelta = lookDelta;
            SprintHeld = sprintHeld;
            Pressed = pressed;
        }
    }
}
