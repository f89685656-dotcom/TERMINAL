using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TERMINAL.Converters
{
    /// <summary>
    /// Конвертирует double? в Brush:
    /// отрицательное → красный, положительное → зелёный, ноль/пусто → чёрный.
    /// </summary>
    public class ValueToBrushConverter : IValueConverter
    {
        private static readonly Brush RedBrush = CreateFrozen(Colors.Red);
        private static readonly Brush GreenBrush = CreateFrozen(Colors.Green);
        private static readonly Brush BlackBrush = CreateFrozen(Colors.Black);

        private static Brush CreateFrozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return BlackBrush;

            double? d = null;

            // Приведение любых числовых типов
            if (value is double dv) d = dv;
            else if (value is float fv) d = fv;
            else if (value is int iv) d = iv;
            else if (value is string sv && double.TryParse(sv, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double parsed))
                d = parsed;

            if (!d.HasValue) return BlackBrush;

            if (d.Value > 0.0001) return GreenBrush;
            if (d.Value < -0.0001) return RedBrush;
            return BlackBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}