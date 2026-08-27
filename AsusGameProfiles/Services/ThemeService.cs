using System.Windows;
using Microsoft.Win32;

namespace AsusGameProfiles.Services;

/// <summary>
/// Choisit le theme (clair/sombre) de l'app en fonction du reglage systeme Windows, une fois au
/// demarrage. App.xaml merge Themes/Dark.xaml par defaut (pour que les styles puissent se charger
/// sans erreur) ; si l'OS est en theme clair, on remplace ce merge par Themes/Light.xaml. Les styles
/// de App.xaml utilisent DynamicResource (pas StaticResource), donc ce remplacement met a jour
/// immediatement tous les elements deja construits.
/// </summary>
public static class ThemeService
{
    public static bool IsDarkTheme { get; private set; } = true;

    public static void ApplyOsTheme()
    {
        bool useLightTheme = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
                useLightTheme = value != 0;
        }
        catch
        {
            // Cle absente/inaccessible (ancienne version de Windows, strategie de groupe, etc.) :
            // on garde le theme sombre par defaut plutot que de planter.
        }

        IsDarkTheme = !useLightTheme;
        if (!useLightTheme) return;

        var light = new ResourceDictionary { Source = new Uri("Themes/Light.xaml", UriKind.Relative) };
        Application.Current.Resources.MergedDictionaries.Clear();
        Application.Current.Resources.MergedDictionaries.Add(light);
    }
}
