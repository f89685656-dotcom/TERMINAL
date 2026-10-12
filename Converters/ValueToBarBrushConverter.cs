using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TERMINAL.Converters
{
    /// <summary>
    /// Возвращает цвет полоски: красный для отрицательных, зелёный для положительных.
    /// </summary>
    public class ValueToBarBrushConverter : IValueConverter
    {
        private static readonly Brush Red = CreateFrozen(Color.FromRgb(0xE0, 0x60, 0x60));
        private static readonly Brush Green = CreateFrozen(Color.FromRgb(0x60, 0xC0, 0x60));
        private static readonly Brush Transparent = CreateFrozen(Colors.Transparent);

        private static Brush CreateFrozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Transparent;

            double? d = null;
            if (value is double dv) d = dv;
            else if (value is float fv) d = fv;
            else if (value is int iv) d = iv;
            else if (value is string sv && double.TryParse(sv,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double p))
                d = p;

            if (!d.HasValue) return Transparent;

            if (Math.Abs(d.Value) < 0.001) return Transparent;

            return d.Value < 0 ? Red : Green;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}