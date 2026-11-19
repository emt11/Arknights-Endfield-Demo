using UnityEngine.InputSystem;

namespace Endfield.Controller
{
    public sealed class ButtonInput : InputBase
    {
        public ButtonInput(InputAction action) : base(action)
        {
        }

        public bool IsPressed => IsEnabled && Action.IsPressed();
        public bool WasPressedThisFrame => IsEnabled && Action.WasPressedThisFrame();
        public bool WasReleasedThisFrame => IsEnabled && Action.WasReleasedThisFrame();
    }
}
