using System;

namespace Endfield.Common
{
    public static class NumericValidation
    {
        public static bool IsFiniteInRange(double value, double minimum, double maximum)
        {
            return double.IsFinite(value) && value >= minimum && value <= maximum;
        }

        public static void RequireFiniteInRange(double value, double minimum, double maximum, string parameterName)
        {
            if (!IsFiniteInRange(value, minimum, maximum))
                throw new ArgumentOutOfRangeException(parameterName, value, $"数值须为 {minimum} 到 {maximum} 之间的有限数");
        }
    }
}
