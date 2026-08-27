using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using AsusGameProfiles.Models;

namespace AsusGameProfiles.Services;

/// <summary>
/// Seul mecanisme de declenchement de l'app (2026-08-27 : l'ancien mode optionnel via les options de
/// lancement Steam a ete retire -- un wrapper qui intercepte le lancement reste suspect pour certains
/// anti-cheats, ex: FACEIT, qui refusent ou echouent silencieusement quand le jeu n'est pas lance
/// directement par leur propre chaine de lancement). Verifie periodiquement si l'executable de chaque
/// profil tourne, et applique le profil de lancement/sortie sur les transitions detectees. Fonctionne
/// aussi bien pour les jeux Steam que non-Steam.
///
/// Polling, pas d'evenement WMI : <c>Win32_ProcessStartTrace</c> exigerait que cette app tourne en
/// permanence avec des droits Administrateur (verifie -- voir CLAUDE.md), ce qui serait plus intrusif
/// que le probleme qu'on essaie de resoudre. Consequence directe : ce mecanisme ne detecte donc rien
/// tant que ce process (l'app elle-meme) ne tourne pas -- il n'a d'utilite reelle qu'avec
/// "Start with Windows" + "Close to tray" actives.
/// </summary>
public sealed class ProcessWatcherService
{
    private readonly DispatcherTimer _timer;
    private readonly Func<AppConfig> _getConfig;
    private readonly HashSet<string> _runningProfileIds = new();

    /// <summary>Notifie l'UI (id du profil, "launch" ou "exit") a chaque application de profil declenchee par ce mecanisme.</summary>
    public event Action<GameProfile, string>? ProfileTriggered;

    public ProcessWatcherService(Func<AppConfig> getConfig, TimeSpan? interval = null)
    {
        _getConfig = getConfig;
        _timer = new DispatcherTimer { Interval = interval ?? TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();

    private void Tick()
    {
        var config = _getConfig();
        var watched = config.Profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.ExePath))
            .ToList();

        if (watched.Count == 0 && _runningProfileIds.Count == 0) return;

        foreach (var profile in watched)
        {
            bool wasRunning = _runningProfileIds.Contains(profile.Id);
            bool isRunning = IsProcessRunning(profile.ExePath);

            if (isRunning && !wasRunning)
            {
                _runningProfileIds.Add(profile.Id);
                GameLauncher.ApplyLaunchProfile(config, profile);
                ProfileTriggered?.Invoke(profile, "launch");
            }
            else if (!isRunning && wasRunning)
            {
                _runningProfileIds.Remove(profile.Id);
                GameLauncher.ApplyExitProfile(config, profile);
                ProfileTriggered?.Invoke(profile, "exit");
            }
        }

        // Un profil supprime (ou son ExePath vide) pendant qu'il etait detecte "en cours" :
        // on oublie son etat plutot que de continuer a le surveiller silencieusement.
        _runningProfileIds.RemoveWhere(id => watched.All(p => p.Id != id));
    }

    /// <summary>
    /// Vrai si un processus dont le chemin complet correspond a <paramref name="exePath"/> tourne
    /// actuellement. Se rabat sur une simple correspondance de nom si le chemin exact du process est
    /// illisible (process eleve/protege) -- un nom de process qui correspond reste un signal correct
    /// dans l'immense majorite des cas, et rater silencieusement la detection serait pire.
    /// </summary>
    private static bool IsProcessRunning(string exePath)
    {
        var targetName = Path.GetFileNameWithoutExtension(exePath);
        if (string.IsNullOrEmpty(targetName)) return false;

        var processes = Process.GetProcessesByName(targetName);
        try
        {
            foreach (var proc in processes)
            {
                try
                {
                    var procPath = proc.MainModule?.FileName;
                    if (procPath is null || string.Equals(procPath, exePath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                    return true;
                }
            }
            return false;
        }
        finally
        {
            foreach (var proc in processes) proc.Dispose();
        }
    }
}
