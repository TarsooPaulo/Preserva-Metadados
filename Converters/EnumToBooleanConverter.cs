using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace PreservaMetadados.Converters;

/// <summary>
/// Converte um valor enum para booleano com base no parâmetro fornecido, útil para RadioButtons.
/// </summary>
public class EnumToBooleanConverter : IValueConverter
{
    public static readonly EnumToBooleanConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        return value.Equals(parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter != null)
        {
            return parameter;
        }

        return BindingOperations.DoNothing;
    }
}
