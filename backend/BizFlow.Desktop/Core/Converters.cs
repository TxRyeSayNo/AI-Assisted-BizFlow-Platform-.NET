using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BizFlow.Desktop.Core;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;
        return !flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : false;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is not null && (value is not string str || !string.IsNullOrWhiteSpace(str));
        if (parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class EqualToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var actual = value?.ToString() ?? "";
        var expected = parameter?.ToString() ?? "";
        var matches = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        return matches ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class StatusToBackgroundBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBg = CreateFrozen("#ECFDF5");
    private static readonly SolidColorBrush BlueBg = CreateFrozen("#EAF4FF");
    private static readonly SolidColorBrush AmberBg = CreateFrozen("#FEF3C7");
    private static readonly SolidColorBrush RedBg = CreateFrozen("#FEF2F2");
    private static readonly SolidColorBrush SlateBg = CreateFrozen("#F1F5F9");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString()?.ToUpperInvariant() ?? "";
        return status switch
        {
            "COMPLETED" or "CONFIRMED" or "RESOLVED" or "CLOSED" or "ACTIVE" or "PUBLISHED" or "APPROVED" or "READY" or "OPTIMAL" => GreenBg,
            "IN_PROGRESS" or "ACCEPTED" or "ASSIGNED" or "ROUTED" or "RECEIVED" or "SUBMITTED" => BlueBg,
            "PENDING" or "WAITING_FOR_INFORMATION" or "DRAFT" or "WARNING" => AmberBg,
            "OVERDUE" or "REJECTED" or "LOCKED" or "SUSPENDED" or "BREACHED" or "ERROR" => RedBg,
            _ => SlateBg
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

    private static SolidColorBrush CreateFrozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}

public sealed class StatusToForegroundBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenFg = CreateFrozen("#047857");
    private static readonly SolidColorBrush BlueFg = CreateFrozen("#1D4ED8");
    private static readonly SolidColorBrush AmberFg = CreateFrozen("#B45309");
    private static readonly SolidColorBrush RedFg = CreateFrozen("#B91C1C");
    private static readonly SolidColorBrush SlateFg = CreateFrozen("#475569");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString()?.ToUpperInvariant() ?? "";
        return status switch
        {
            "COMPLETED" or "CONFIRMED" or "RESOLVED" or "CLOSED" or "ACTIVE" or "PUBLISHED" or "APPROVED" or "READY" or "OPTIMAL" => GreenFg,
            "IN_PROGRESS" or "ACCEPTED" or "ASSIGNED" or "ROUTED" or "RECEIVED" or "SUBMITTED" => BlueFg,
            "PENDING" or "WAITING_FOR_INFORMATION" or "DRAFT" or "WARNING" => AmberFg,
            "OVERDUE" or "REJECTED" or "LOCKED" or "SUSPENDED" or "BREACHED" or "ERROR" => RedFg,
            _ => SlateFg
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

    private static SolidColorBrush CreateFrozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}

public sealed class PriorityToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush CriticalBrush = CreateFrozen("#DC2626");
    private static readonly SolidColorBrush HighBrush = CreateFrozen("#EA580C");
    private static readonly SolidColorBrush MediumBrush = CreateFrozen("#2563EB");
    private static readonly SolidColorBrush LowBrush = CreateFrozen("#64748B");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var priority = value?.ToString()?.ToUpperInvariant() ?? "";
        return priority switch
        {
            "CRITICAL" => CriticalBrush,
            "HIGH" => HighBrush,
            "MEDIUM" => MediumBrush,
            _ => LowBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;

    private static SolidColorBrush CreateFrozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
