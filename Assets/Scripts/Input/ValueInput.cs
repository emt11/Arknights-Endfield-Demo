using UnityEngine.InputSystem;

namespace Endfield.Controller
{
    /// <summary>
    /// 封装数值输入的读取，未启用时返回默认值。
    /// </summary>
    /// <typeparam name="TValue">输入值类型，必须是非可空值类型。</typeparam>
    public sealed class ValueInput<TValue> : InputBase where TValue : struct
    {
        public ValueInput(InputAction action) : base(action) { }

        public TValue ReadValue() => IsEnabled ? Action.ReadValue<TValue>() : default;
    }
}
