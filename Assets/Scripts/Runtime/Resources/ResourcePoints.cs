using System;

namespace Endfield.Resources
{
    internal static class ResourcePoints
    {
        internal static void Validate(double value, string parameterName, double maximum = double.MaxValue)
        {
            if (!double.IsFinite(value) || value < 0 || value > maximum)
                throw new ArgumentOutOfRangeException(parameterName, value, $"点数须为 0 到 {maximum} 之间的有限数值");
        }

        internal static double AfterGain(double current, double amount, double maximum)
        {
            // 先比较剩余容量，封顶时直接取上限
            return amount >= maximum - current ? maximum : current + amount;
        }
    }
}
