using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MultronWinCleaner.Processes
{
    public sealed class StatusGlyphConverter : IValueConverter
    {
        private const string Check = "";
        private const string Info = "";
        private const string Warning = "";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not SolidColorBrush brush)
                return Info;
            var color = brush.Color;
            if (color.G > color.R && color.G >= color.B)
                return Check;
            if (color.B > color.R && color.B > color.G)
                return Info;
            return Warning;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
