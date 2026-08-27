using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AsusGameProfiles.Services;

namespace AsusGameProfiles.Converters;

/// <summary>Convertit un chemin d'exécutable (GameProfile.ExePath) en icône WPF, via <see cref="IconCache"/>.</summary>
public class ExePathToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => IconCache.GetIcon(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
