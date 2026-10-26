
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using NexusOptimizer.Services;

namespace NexusOptimizer.Converters;

public sealed class GameIconConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return GameIconHelper.GetIcon(value as string)
               ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class GameIconVisibilityConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return GameIconHelper.GetIcon(value as string) is null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
