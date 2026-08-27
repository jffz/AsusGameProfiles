using System.IO;

namespace AsusGameProfiles.Services;

/// <summary>
/// Devine l'exécutable principal d'un jeu Steam installé, pour éviter de demander à l'utilisateur
/// de le pointer manuellement à chaque ajout. Best-effort uniquement : le jeu est toujours lancé
/// normalement (Steam ou tout autre raccourci) -- cet exe sert seulement à afficher une icône dans
/// l'UI et à detecter le jeu via <see cref="ProcessWatcherService"/>, une erreur ici n'empêche donc
/// pas le jeu lui-même de se lancer, seulement l'icône/la détection automatique.
/// </summary>
public static class GameExecutableFinder
{
    // Exécutables d'installeurs, d'anti-triche, d'outils SDK/modding ou de redistribuables --
    // jamais le vrai jeu, calibré sur des bibliothèques Steam réelles (CS2, Cyberpunk 2077,
    // Battlefield 6, 3DMark, etc.) qui embarquent tous ce genre d'exécutables annexes.
    private static readonly string[] ExcludeKeywords =
    {
        "unins", "redist", "directx", "dxsetup", "dxwebsetup", "crashpad", "crashreport",
        "errorreporter", "anticheat", "battleye", "eac_", "dotnetfx", "oalinst", "prereq",
        "setup", "import", "workshop", "resource", "updater", "patcher", "vconsole", "vrad", "vbsp"
    };

    /// <summary>
    /// Cherche le meilleur candidat sous <paramref name="installPath"/> : exclut les outils connus,
    /// puis priorise le nom le plus proche de <paramref name="gameName"/>, la taille de fichier
    /// (les vrais jeux sont presque toujours bien plus gros que les utilitaires annexes) et la
    /// faible profondeur. Retourne null si le dossier n'existe pas ou si aucun .exe n'est trouvé.
    /// </summary>
    public static string? TryFindMainExecutable(string installPath, string gameName)
    {
        if (!Directory.Exists(installPath)) return null;

        List<string> exeFiles;
        try
        {
            exeFiles = Directory.EnumerateFiles(installPath, "*.exe", SearchOption.AllDirectories)
                .Take(2000)
                .ToList();
        }
        catch
        {
            return null;
        }

        var normalizedName = Normalize(gameName);
        var normalizedInstallDirName = Normalize(new DirectoryInfo(installPath).Name);

        string? best = null;
        double bestScore = double.MinValue;

        foreach (var exePath in exeFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(exePath);
            var lowerFileName = fileName.ToLowerInvariant();

            if (ExcludeKeywords.Any(k => lowerFileName.Contains(k)))
                continue;

            long size;
            try { size = new FileInfo(exePath).Length; }
            catch { continue; }
            if (size <= 0) continue;

            double score = Math.Log(size);

            var normalizedFileName = Normalize(fileName);
            if (normalizedFileName.Length > 0)
            {
                if (normalizedFileName == normalizedName || normalizedFileName == normalizedInstallDirName)
                    score += 50; // nom exactement identique au jeu/dossier d'installation : signal tres fort
                else if (normalizedName.Contains(normalizedFileName) || normalizedFileName.Contains(normalizedName) ||
                         normalizedInstallDirName.Contains(normalizedFileName) || normalizedFileName.Contains(normalizedInstallDirName))
                    score += 15;
            }

            if (lowerFileName.Contains("launcher"))
                score -= 5;

            var relativeDepth = Path.GetRelativePath(installPath, exePath)
                .Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
            score -= relativeDepth * 0.5;

            if (score > bestScore)
            {
                bestScore = score;
                best = exePath;
            }
        }

        return best;
    }

    private static string Normalize(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
