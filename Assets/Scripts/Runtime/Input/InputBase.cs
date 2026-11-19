using System;
using UnityEngine.InputSystem;

namespace Endfield.Controller
{
    public abstract class InputBase : IDisposable
    {
        protected InputAction Action { get; private set; }
        public bool IsEnabled => Action != null && Action.enabled;

        protected InputBase(InputAction action) => Action = action;

        public void Enable() => Action.Enable();

        public void Disable() => Action?.Disable();

        public void Dispose()
        {
            Disable();
            Action = null;
        }
    }
}
