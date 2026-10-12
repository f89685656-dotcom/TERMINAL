using System;
using System.Globalization;
using System.Windows.Data;

namespace TERMINAL.Converters
{
    /// <summary>
    /// Возвращает ширину полоски в пикселях (от 0 до MaxWidth),
    /// пропорционально абсолютному значению.
    /// </summary>
    public class ValueToBarWidthConverter : IValueConverter
    {
        // Максимальная ширина полоски в пикселях (примерно ширина колонки минус padding)
        public double MaxWidth { get; set; } = 60;

        // Значение, при котором полоска достигает MaxWidth
        public double MaxAbsValue { get; set; } = 0.5;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return 0.0;

            double? d = null;
            if (value is double dv) d = dv;
            else if (value is float fv) d = fv;
            else if (value is int iv) d = iv;
            else if (value is string sv && double.TryParse(sv,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out double p))
                d = p;

            if (!d.HasValue) return 0.0;

            double abs = Math.Abs(d.Value);
            double ratio = Math.Min(abs / MaxAbsValue, 1.0);
            return ratio * MaxWidth;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}