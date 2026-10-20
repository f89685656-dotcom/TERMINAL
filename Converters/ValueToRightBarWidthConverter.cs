using System;
using System.Globalization;
using System.Windows.Data;

namespace TERMINAL.Converters
{
    /// <summary>
    /// Ширина полоски для положительных значений.
    /// Для отрицательных и нуля → 0.
    /// </summary>
    public class ValueToRightBarWidthConverter : IValueConverter
    {
        public double MaxWidth { get; set; } = 30;
        public double MaxAbsValue { get; set; } = 0.5;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double? d = null;
            if (value is double dv) d = dv;
            else if (value is float fv) d = fv;
            else if (value is int iv) d = iv;
            else if (value is string sv && double.TryParse(sv,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double p))
                d = p;

            if (!d.HasValue || d.Value <= 0) return 0.0;

            if (MaxAbsValue <= 1e-12) return 0.0;      // ★ защита
            double ratio = Math.Min(d.Value / MaxAbsValue, 1.0);
            return ratio * MaxWidth;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}