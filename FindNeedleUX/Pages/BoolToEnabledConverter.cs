using Microsoft.UI.Xaml.Data;
using System;

namespace FindNeedleUX.Pages
{
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
}
