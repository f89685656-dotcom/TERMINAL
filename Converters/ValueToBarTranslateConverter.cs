using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TERMINAL.Converters
{
    /// <summary>
    /// Сдвигает полоску по оси X относительно центра ячейки:
    ///   • положительное значение → вправо на половину ширины полоски;
    ///   • отрицательное значение → влево на половину ширины полоски;
    ///   • ноль → без сдвига.
    ///
    /// Работает в паре с ValueToBarWidthConverter:
    ///   Width       = Abs(value) * MaxWidth / MaxAbsValue
    ///   TranslateX  = Sign(value) * Width / 2
    ///
    /// В DataTemplate полоску выравнивают HorizontalAlignment="Center".
    /// </summary>
    public class ValueToBarTranslateConverter : IValueConverter
    {
        /// <summary>Максимальная ширина полоски (та же, что у ValueToBarWidthConverter).</summary>
        public double MaxWidth { get; set; } = 60;

        /// <summary>Значение, при котором полоска достигает MaxWidth.</summary>
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

            if (!d.HasValue)
                return new TranslateTransform(0, 0);

            double v = d.Value;

            if (Math.Abs(v) < 1e-9)
                return new TranslateTransform(0, 0);

            // Ширина полоски — точно как в ValueToBarWidthConverter
            double abs = Math.Abs(v);
            double ratio = Math.Min(abs / MaxAbsValue, 1.0);
            double width = ratio * MaxWidth;

            // Сдвиг: половина ширины, знак — по значению
            double offset = (v > 0 ? 1 : -1) * (width / 2.0);

            return new TranslateTransform(offset, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}