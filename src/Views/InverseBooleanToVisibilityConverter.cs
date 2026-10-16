using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ChatHub.Views;

/// <summary>bool → Visibility（true 折叠），用于“没有数据时显示提示”这类场景。</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}
