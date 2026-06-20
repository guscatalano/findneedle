using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace FindNeedleUX.Pages;

/// <summary>True → Visible, false → Collapsed. Used so a hidden banner contributes no layout space
/// (including its margin), unlike an InfoBar with IsOpen=false which stays Visible at 0 height.</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Visible;
}

public class BoolToEnabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
            return b ? "Enabled" : "Disabled";
        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
