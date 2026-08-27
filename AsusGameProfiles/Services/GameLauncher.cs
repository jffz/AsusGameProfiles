using System.Diagnostics;
using System.IO;
using AsusGameProfiles.Models;

namespace AsusGameProfiles.Services;

/// <summary>
/// Logique du mode "lanceur" : lance directement un jeu, applique le profil avant/après, attend sa
/// fin via Process.WaitForExit(). Utilisé par le bouton "Launch now" (jeux non-Steam) --
/// <see cref="AsusGameProfiles.MainWindow"/> l'invoque directement en mémoire avec des arguments
/// synthétiques, pas de nouveau process. Ce mode existait aussi comme cible d'un wrapper d'options
/// de lancement Steam ("AsusGameProfiles.exe" --launch 730 "C:\...\cs2.exe" ...) avant que ce
/// mécanisme ne soit retiré (2026-08-27) -- le point d'entrée <c>--launch</c> reste géré (voir
/// Program.cs) pour rester inoffensif si une installation Steam garde encore une ancienne option de
/// lancement pas encore nettoyée par <see cref="AsusGameProfiles.MainWindow"/>, pas pour un usage
/// courant.
/// </summary>
public static class GameLauncher
{
    /// <summary>
    /// Exécute le mode lanceur : applique le profil au lancement, démarre le jeu et attend sa fin,
    /// puis applique le profil de sortie. Retourne le code de sortie du jeu (ou 1 en cas d'échec de
    /// lancement/usage invalide). <paramref name="args"/> doit commencer directement par "--launch"
    /// (sans le chemin de l'exécutable courant) : <c>{"--launch", appid, chemin_exe, args...}</c>.
    /// </summary>
    public static int RunLaunchMode(string[] args)
    {
        var logPath = Path.Combine(ConfigStore.LogDirectory, $"launch-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };

        void Log(string message) => log.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

        // args[0] = "--launch", args[1] = appid, args[2] = exe du jeu, args[3..] = arguments du jeu
        if (args.Length < 3)
        {
            Log("Expected usage: --launch <appid> <exe_path> [args...]");
            return 1;
        }

        var appId = args[1];
        var targetExe = args[2];
        var targetArgs = args.Skip(3).ToArray();

        Log($"Requested AppID: {appId}");
        Log($"Target executable: {targetExe}");

        var config = ConfigStore.Load();
        var profile = config.Profiles.FirstOrDefault(p => p.Id == appId);

        if (profile is null)
        {
            Log("No profile configured for this AppID -- launching the game without changing display settings.");
            return LaunchAndWait(targetExe, targetArgs, Log);
        }

        Log($"Profile: \"{profile.DisplayName}\"");
        ApplyLaunchProfile(config, profile, Log);

        int exitCode = LaunchAndWait(targetExe, targetArgs, Log);
        Log($"Game exited (exit code {exitCode}).");

        ApplyExitProfile(config, profile, Log);

        return exitCode;
    }

    /// <summary>
    /// Applique le preset assigne au lancement de ce jeu (GameVisual/FrameRateBoost/proprietes
    /// avancees) -- ne fait rien si aucun preset n'est assigne (<see cref="GameProfile.OnLaunchPresetId"/>
    /// vide ou pointant vers un preset supprime). Utilise a la fois par le mode --launch (Steam) et par
    /// <see cref="ProcessWatcherService"/>.
    /// </summary>
    public static void ApplyLaunchProfile(AppConfig config, GameProfile profile, Action<string>? log = null)
    {
        log ??= _ => { };
        var preset = config.Presets.FirstOrDefault(p => p.Id == profile.OnLaunchPresetId);
        if (preset is null)
        {
            log($"No preset assigned to \"{profile.DisplayName}\" -- launch settings not applied.");
            return;
        }

        log($"Launch setting: preset \"{preset.Name}\", GameVisual={(int)preset.Mode} ({preset.Mode}) FrameRateBoost={preset.FrameRateBoost}");
        ApplyProfile(config.DwcExePath, (int)preset.Mode, preset.FrameRateBoost, preset.ExtraProperties, log);
    }

    /// <summary>
    /// Applique le preset assigne a la sortie de ce jeu (<see cref="GameProfile.OnExitPresetId"/>) s'il
    /// existe, sinon le "Default exit profile" global. Utilise a la fois par le mode --launch (Steam) et
    /// par <see cref="ProcessWatcherService"/>.
    /// </summary>
    public static void ApplyExitProfile(AppConfig config, GameProfile profile, Action<string>? log = null)
    {
        log ??= _ => { };
        var preset = config.Presets.FirstOrDefault(p => p.Id == profile.OnExitPresetId);

        GameVisualMode exitMode;
        bool exitFrameRateBoost;
        List<DwcPropertyValue> exitExtraProperties;
        if (preset != null)
        {
            exitMode = preset.Mode;
            exitFrameRateBoost = preset.FrameRateBoost;
            exitExtraProperties = preset.ExtraProperties;
            log($"Exit setting: preset \"{preset.Name}\".");
        }
        else
        {
            exitMode = config.DefaultExitMode;
            exitFrameRateBoost = config.DefaultExitFrameRateBoost;
            exitExtraProperties = config.DefaultExitExtraProperties;
            log("Exit setting: using the default exit profile.");
        }

        log($"Exit setting: GameVisual={(int)exitMode} ({exitMode}) FrameRateBoost={exitFrameRateBoost}");
        ApplyProfile(config.DwcExePath, (int)exitMode, exitFrameRateBoost, exitExtraProperties, log);
    }

    private static void ApplyProfile(string dwcExePath, int gameVisual, bool frameRateBoost,
        List<DwcPropertyValue> extraProperties, Action<string> log)
    {
        var r1 = DwcService.Set(dwcExePath, "GameVisual", gameVisual.ToString());
        log($"  dwc set GameVisual {gameVisual} -> success={r1.Success} code={r1.ExitCode} output=\"{r1.Output}\"");

        var r2 = DwcService.Set(dwcExePath, "FrameRateBoost", frameRateBoost ? "1" : "0");
        log($"  dwc set FrameRateBoost {(frameRateBoost ? 1 : 0)} -> success={r2.Success} code={r2.ExitCode} output=\"{r2.Output}\"");

        foreach (var extra in extraProperties)
        {
            var r = DwcService.Set(dwcExePath, extra.Property, extra.Value);
            log($"  dwc set {extra.Property} {extra.Value} -> success={r.Success} code={r.ExitCode} output=\"{r.Output}\"");
        }
    }

    /// <summary>
    /// Lance le jeu et BLOQUE jusqu'à sa fermeture réelle via Process.WaitForExit() --
    /// contrairement à "start" en .bat, il n'y a ici aucune ambiguïté possible sur le moment du retour.
    /// </summary>
    private static int LaunchAndWait(string exePath, string[] args, Action<string> log)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            log($"Launching: {exePath} {string.Join(' ', args)}");

            using var process = Process.Start(psi);
            if (process is null)
            {
                log("Launch failed: Process.Start returned null.");
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            log($"Exception while launching: {ex}");
            return 1;
        }
    }
}
