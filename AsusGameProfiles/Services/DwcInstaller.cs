using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace AsusGameProfiles.Services;

public record DwcInstallResult(bool Success, string Message, string? ResolvedPath);

/// <summary>
/// Télécharge et installe dwc.exe pour les utilisateurs qui n'ont pas déjà DisplayWidget Center
/// (dont dwc.exe fait normalement partie). Source vérifiée manuellement le 2026-08-26 : le dépôt
/// GitHub officiel de l'organisation ASUS-Display (Apache 2.0, couvert par la presse hardware comme
/// étant réellement publié par ASUS), fichier brut sur la branche main -- pas un fork tiers, pas
/// d'installeur DisplayWidget Center complet (qui nécessiterait une élévation et modifierait le
/// système bien plus largement qu'un seul exécutable CLI). Ne réinstalle rien d'autre et ne touche
/// pas au PATH systeme : on place juste l'exe dans le dossier de donnees de l'app et on pointe
/// AppConfig.DwcExePath dessus, exactement comme pour une detection/selection manuelle.
/// </summary>
public static class DwcInstaller
{
    private const string DownloadUrl =
        "https://raw.githubusercontent.com/ASUS-Display/asus-display-control/main/cli/windows/dwc_win.zip";

    public static async Task<DwcInstallResult> InstallAsync(string destDir)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"dwc_win_{Guid.NewGuid():N}.zip");
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            using (var response = await http.GetAsync(DownloadUrl))
            {
                if (!response.IsSuccessStatusCode)
                    return new DwcInstallResult(false, $"Download failed (HTTP {(int)response.StatusCode}).", null);

                await using var fileStream = File.Create(zipPath);
                await response.Content.CopyToAsync(fileStream);
            }

            // Reinstallation propre : on repart d'un dossier vide pour ne jamais melanger une
            // ancienne version partiellement extraite avec la nouvelle.
            if (Directory.Exists(destDir))
                Directory.Delete(destDir, recursive: true);

            ZipFile.ExtractToDirectory(zipPath, destDir);

            var exePath = Directory.GetFiles(destDir, "dwc.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exePath is null)
                return new DwcInstallResult(false, "Downloaded archive did not contain dwc.exe.", null);

            var check = DwcService.Info(exePath);
            if (!check.Success)
                return new DwcInstallResult(false, "dwc.exe was installed but did not respond correctly. Your monitor may not support DDC/CI, or a cable/connection issue is blocking it.", null);

            return new DwcInstallResult(true, "dwc.exe installed.", exePath);
        }
        catch (Exception ex)
        {
            return new DwcInstallResult(false, $"Install failed: {ex.Message}", null);
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* Meilleur effort : fichier temporaire, sans consequence s'il reste. */ }
        }
    }
}
