using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace XivConfigSync.App;

/// <summary>True when an integer equals the parameter; used for the navigation buttons.</summary>
public sealed class IndexToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int index && int.TryParse(parameter as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wanted) && index == wanted;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && int.TryParse(parameter as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wanted) ? wanted : Binding.DoNothing;
}

/// <summary>Visible when an integer equals the parameter; used for the pages.</summary>
public sealed class IndexToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int index && int.TryParse(parameter as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wanted) && index == wanted
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible when a text is not empty.</summary>
public sealed class TextToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text && text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Negates a boolean.</summary>
public sealed class NotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
