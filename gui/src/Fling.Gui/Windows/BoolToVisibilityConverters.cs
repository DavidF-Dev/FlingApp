using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Fling.Gui.Windows;

/// <summary>
/// Collapses the element when the bound value is true.
/// </summary>
/// <remarks>
/// The framework's BooleanToVisibilityConverter ignores its ConverterParameter, so
/// inverting requires a separate converter rather than a flag.
/// </remarks>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
