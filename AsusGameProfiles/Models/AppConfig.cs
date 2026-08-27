namespace AsusGameProfiles.Models;

public class AppConfig
{
    /// <summary>Chemin vers dwc.exe (ASUS Display Control CLI). Détecté automatiquement au démarrage.</summary>
    public string DwcExePath { get; set; } = "dwc.exe";

    public List<GameProfile> Profiles { get; set; } = new();

    /// <summary>Presets reutilisables (voir <see cref="GameProfilePreset"/>), assignes aux jeux via <see cref="GameProfile.OnLaunchPresetId"/>/<see cref="GameProfile.OnExitPresetId"/>.</summary>
    public List<GameProfilePreset> Presets { get; set; } = new();

    /// <summary>
    /// Réglage GameVisual restauré à la fermeture de n'importe quel jeu dont le profil n'a pas coché
    /// "Override the default exit profile". Modifiable en sélectionnant l'entrée "Default exit
    /// profile" épinglée en haut de la liste des jeux, exactement comme un profil de jeu.
    /// </summary>
    public GameVisualMode DefaultExitMode { get; set; } = GameVisualMode.Srgb;

    /// <summary>Frame Rate Boost restauré à la fermeture pour les jeux qui n'overrident pas le profil par défaut.</summary>
    public bool DefaultExitFrameRateBoost { get; set; } = false;

    /// <summary>Propriétés dwc.exe additionnelles (ex: Brightness, ColorTemp...) appliquées avec le profil par défaut, à la sortie de n'importe quel jeu qui n'override pas.</summary>
    public List<DwcPropertyValue> DefaultExitExtraProperties { get; set; } = new();

    /// <summary>Si vrai, fermer la fenêtre principale la minimise dans la zone de notification au lieu de quitter l'app.</summary>
    public bool CloseToTray { get; set; } = false;

    /// <summary>Si vrai (par défaut), une bulle de notification annonce l'application/la restauration d'un preset déclenchée par <see cref="AsusGameProfiles.Services.ProcessWatcherService"/>. Voir <see cref="AsusGameProfiles.MainWindow.OnProfileTriggeredByWatcher"/>.</summary>
    public bool ShowProfileNotifications { get; set; } = true;
}
