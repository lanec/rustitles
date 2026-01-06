using System.Globalization;
using System.Windows;
using System.Windows.Data;
using WinTitles.Core.Models;

namespace WinTitles.App.Converters;

/// <summary>
/// Converts WorkflowState to Visibility based on parameter.
/// </summary>
public class WorkflowStateToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is WorkflowState state && parameter is string paramStr)
        {
            if (Enum.TryParse<WorkflowState>(paramStr, out var targetState))
            {
                return state == targetState ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
