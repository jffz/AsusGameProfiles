using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AsusGameProfiles.Services;

/// <summary>
/// Extrait et met en cache l'icône associée à un exécutable (celle que l'Explorateur affiche),
/// pour l'afficher à côté de chaque profil dans l'UI. Best-effort : un exe introuvable/déplacé ou
/// sans icône ne provoque jamais d'exception, juste l'absence d'icône pour ce profil.
/// </summary>
public static class IconCache
{
    private static readonly Dictionary<string, ImageSource?> Cache = new();

    /// <summary>Retourne l'icône (mise en cache) de l'exécutable donné, ou null si indisponible.</summary>
    public static ImageSource? GetIcon(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;

        if (Cache.TryGetValue(exePath, out var cached)) return cached;

        ImageSource? result = ExtractIcon(exePath);
        Cache[exePath] = result;
        return result;
    }

    private static ImageSource? ExtractIcon(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return null;

            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) return null;

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze(); // utilisable depuis un DataTemplate independamment du thread d'extraction
            return source;
        }
        catch
        {
            return null;
        }
    }
}
