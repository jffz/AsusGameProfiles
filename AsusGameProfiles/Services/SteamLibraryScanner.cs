using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AsusGameProfiles.Services;

/// <summary>Un jeu Steam installé détecté depuis un appmanifest_*.acf.</summary>
public record SteamGameInfo(string AppId, string Name, string InstallDir, string InstallPath);

/// <summary>
/// Lecture directe des fichiers locaux de Steam (registre + .vdf/.acf) pour lister les jeux installés.
/// N'utilise aucune API Steam, ne lance rien, ne touche à aucun processus -- lecture seule de fichiers texte.
/// </summary>
public static class SteamLibraryScanner
{
    /// <summary>Localise le dossier d'installation de Steam via le registre, avec repli sur le chemin par défaut.</summary>
    public static string? FindSteamPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
            var path = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var normalized = path.Replace('/', '\\');
                if (Directory.Exists(normalized)) return normalized;
            }
        }
        catch
        {
            // Clé de registre absente ou inaccessible : on retombe sur le chemin par défaut.
        }

        const string fallback = @"C:\Program Files (x86)\Steam";
        return Directory.Exists(fallback) ? fallback : null;
    }

    /// <summary>
    /// Liste tous les dossiers de bibliothèque Steam (le dossier Steam lui-même plus ceux déclarés
    /// dans steamapps/libraryfolders.vdf, ex: une bibliothèque secondaire sur un autre disque).
    /// </summary>
    public static List<string> FindLibraryFolders(string steamPath)
    {
        var libraries = new List<string> { steamPath };
        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath)) return libraries;

        try
        {
            var content = File.ReadAllText(vdfPath);
            foreach (Match m in Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\""))
            {
                var path = m.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(path) && !libraries.Contains(path, StringComparer.OrdinalIgnoreCase))
                    libraries.Add(path);
            }
        }
        catch
        {
            // libraryfolders.vdf illisible : on se contente de la bibliothèque principale.
        }

        return libraries;
    }

    /// <summary>
    /// Parcourt toutes les bibliothèques Steam trouvées et lit chaque appmanifest_*.acf pour lister
    /// les jeux installés, triés par nom. Un .acf corrompu ou verrouillé est ignoré sans interrompre le scan.
    /// </summary>
    public static List<SteamGameInfo> ScanInstalledGames(string steamPath)
    {
        var games = new List<SteamGameInfo>();

        foreach (var library in FindLibraryFolders(steamPath))
        {
            var appsDir = Path.Combine(library, "steamapps");
            if (!Directory.Exists(appsDir)) continue;

            foreach (var acfFile in Directory.GetFiles(appsDir, "appmanifest_*.acf"))
            {
                try
                {
                    var content = File.ReadAllText(acfFile);
                    var appId = Regex.Match(content, "\"appid\"\\s*\"(\\d+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
                    var name = Regex.Match(content, "\"name\"\\s*\"([^\"]+)\"").Groups[1].Value;
                    var installDir = Regex.Match(content, "\"installdir\"\\s*\"([^\"]+)\"").Groups[1].Value;

                    if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(installDir))
                        continue;

                    var installPath = Path.Combine(appsDir, "common", installDir);

                    games.Add(new SteamGameInfo(
                        appId,
                        string.IsNullOrWhiteSpace(name) ? installDir : name,
                        installDir,
                        installPath));
                }
                catch
                {
                    // Un .acf corrompu ou verrouillé ne doit pas interrompre le scan des autres jeux.
                }
            }
        }

        return games
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
