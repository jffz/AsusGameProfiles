using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace AsusGameProfiles.Services;

public record DwcResult(bool Success, int ExitCode, string Output);

/// <summary>Un moniteur détecté par dwc.exe (bloc "Monitor N:" de <c>dwc.exe info</c>).</summary>
public record DwcMonitorInfo(string Model, string SerialNumber, string DeviceId);

/// <summary>
/// Fine encapsulation de dwc.exe (ASUS Display Control CLI, https://github.com/ASUS-Display/asus-display-control).
/// Ne fait rien de plus que ton script .bat actuel : un appel process externe, en lecture/écriture DDC/CI
/// vers le moniteur -- aucune interaction avec un autre processus.
/// </summary>
public static class DwcService
{
    /// <summary>Exécute <c>dwc.exe set &lt;property&gt; &lt;value&gt;</c> (ex: "GameVisual" "6").</summary>
    public static DwcResult Set(string dwcExePath, string property, string value)
        => Run(dwcExePath, $"set {property} {value}");

    /// <summary>Exécute <c>dwc.exe info</c>, utilisé pour vérifier que le CLI répond.</summary>
    public static DwcResult Info(string dwcExePath)
        => Run(dwcExePath, "info");

    /// <summary>
    /// Exécute <c>dwc.exe get &lt;property&gt;</c> et renvoie la valeur du premier moniteur (ex: "10"
    /// pour GameVisual, "45" pour UsageTime en heures). Null si dwc.exe est injoignable ou si la
    /// propriété n'est pas supportée par ce moniteur.
    /// </summary>
    public static string? GetPropertyValue(string dwcExePath, string property)
    {
        var result = Run(dwcExePath, $"get {property}");
        if (!result.Success) return null;

        var match = Regex.Match(result.Output, @"Monitor\s+\d+:\s*(.+)");
        var value = (match.Success ? match.Groups[1].Value : result.Output).Trim();
        return value.Length > 0 ? value : null;
    }

    /// <summary>
    /// Essaie de confirmer que dwc.exe est joignable : d'abord via <paramref name="configuredPath"/>
    /// (chemin complet déjà connu, ou juste "dwc.exe" en comptant sur le PATH), puis via "dwc.exe" seul
    /// si ce n'était pas déjà le cas. Ne devine aucun chemin d'installation ASUS non vérifié -- en cas
    /// d'échec, laisse l'utilisateur le localiser manuellement plutôt que d'affirmer un chemin au hasard.
    /// </summary>
    public static bool TryAutoDetect(string configuredPath, out string resolvedPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && Info(configuredPath).Success)
        {
            resolvedPath = ResolveFullPath(configuredPath);
            return true;
        }

        if (!string.Equals(configuredPath, "dwc.exe", StringComparison.OrdinalIgnoreCase) && Info("dwc.exe").Success)
        {
            resolvedPath = ResolveFullPath("dwc.exe");
            return true;
        }

        resolvedPath = configuredPath;
        return false;
    }

    /// <summary>
    /// Si <paramref name="path"/> est deja un chemin complet, le renvoie tel quel. Si c'est juste
    /// "dwc.exe" (trouve via le PATH), cherche son chemin complet en parcourant le PATH -- pour que
    /// AppConfig.DwcExePath pointe vers un fichier reel plutot que de rester la chaine ambigue
    /// "dwc.exe", utile par exemple pour ouvrir la boite de dialogue "Locate dwc.exe" directement au
    /// bon dossier au lieu de l'emplacement par defaut de Windows.
    /// </summary>
    private static string ResolveFullPath(string path)
    {
        if (Path.IsPathRooted(path)) return path;

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var candidate = Path.Combine(dir.Trim(), path);
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // Entree PATH invalide (caracteres illegaux, etc.) : on l'ignore et on continue.
            }
        }

        return path; // Pas trouve sur le PATH (improbable vu que Info() vient de reussir) : on garde tel quel.
    }

    /// <summary>
    /// Moniteurs détectés (via <c>dwc.exe info</c>, un bloc "Monitor N:" par moniteur avec son modèle,
    /// numéro de série et Device ID Windows). Liste vide si dwc.exe est injoignable ou ne détecte aucun
    /// moniteur compatible.
    /// </summary>
    public static List<DwcMonitorInfo> GetDetectedMonitors(string dwcExePath)
    {
        var result = Info(dwcExePath);
        if (!result.Success) return new List<DwcMonitorInfo>();

        var monitors = new List<DwcMonitorInfo>();
        foreach (var block in Regex.Split(result.Output, @"(?=Monitor \d+:)"))
        {
            var model = Regex.Match(block, @"Model Name:\s*(.+)").Groups[1].Value.Trim();
            if (model.Length == 0) continue;

            var serial = Regex.Match(block, @"Serial Number:\s*(.+)").Groups[1].Value.Trim();
            var deviceId = Regex.Match(block, @"Device ID:\s*(.+)").Groups[1].Value.Trim();
            monitors.Add(new DwcMonitorInfo(model, serial, deviceId));
        }
        return monitors;
    }

    private static DwcResult Run(string dwcExePath, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = dwcExePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return new DwcResult(false, -1, "Could not start dwc.exe (path not found?).");

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            var combined = string.IsNullOrWhiteSpace(error) ? output : $"{output}\n{error}";
            return new DwcResult(process.ExitCode == 0, process.ExitCode, combined.Trim());
        }
        catch (Exception ex)
        {
            return new DwcResult(false, -1, ex.Message);
        }
    }
}
