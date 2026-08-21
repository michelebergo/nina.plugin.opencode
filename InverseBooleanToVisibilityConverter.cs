using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NINA.Plugin.OpenCode {

    /// <summary>
    /// Shows an element when a flag is false. Used so one transcript template can carry both
    /// shapes of entry - a plain line and a headline with something behind it - without the
    /// view model having to describe its own presentation.
    /// </summary>
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
