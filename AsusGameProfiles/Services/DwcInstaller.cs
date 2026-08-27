using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

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

    /// <summary>
    /// SHA256 de dwc_win.zip, calcule et verifie le 2026-08-27 contre l'URL ci-dessus (voir NOTICE/
    /// SECURITY.md -- avant ca, ce téléchargement automatique n'avait aucune verification
    /// d'integrite). Si ASUS publie une nouvelle version de dwc.exe sur main, ce hash deviendra
    /// perime et l'install echouera proprement (message clair, avec le repli "Locate dwc.exe
    /// manually" deja present dans l'UI) plutot que d'accepter silencieusement un fichier different
    /// de celui verifie -- dans ce cas, retelecharger le zip, recalculer son SHA256
    /// (`sha256sum dwc_win.zip` ou `Get-FileHash` sous PowerShell), et remplacer la valeur ci-dessous
    /// apres avoir revu ce qui a change.
    /// </summary>
    private const string ExpectedSha256 = "846b0d2ac3d8390d5c7c6ab4f5e52fe127ad16b118a2e427aed885b7e1a7da46";

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

            var actualHash = await ComputeSha256Async(zipPath);
            if (!string.Equals(actualHash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new DwcInstallResult(false,
                    "Downloaded dwc.exe doesn't match the version this app verified -- ASUS may have " +
                    "published an update, or the download was corrupted/tampered with. Not installing it " +
                    "automatically. You can download dwc.exe yourself from ASUS's repository and use " +
                    "\"Locate dwc.exe manually\" instead.", null);
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

    private static async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
