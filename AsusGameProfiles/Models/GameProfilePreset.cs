namespace AsusGameProfiles.Models;

/// <summary>
/// Un état d'affichage dwc.exe réutilisable, indépendant de tout jeu précis -- on l'associe ensuite à
/// un ou plusieurs jeux, pour le lancement et/ou la sortie (<see cref="GameProfile.OnLaunchPresetId"/>/
/// <see cref="GameProfile.OnExitPresetId"/>), au lieu de dupliquer les mêmes réglages sur chacun (ex:
/// un preset "Competitive FPS" utilisé au lancement de CS2, Valorant, Apex).
/// </summary>
public class GameProfilePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Un preset est juste un etat d'affichage (GameVisual + Frame Rate Boost + proprietes avancees) --
    /// pas de notion de "lancement" ou "sortie" en lui-meme (2026-08-27 : la distinction on-launch/
    /// on-exit a ete deplacee au niveau du jeu, voir <see cref="GameProfile.OnLaunchPresetId"/> et
    /// <see cref="GameProfile.OnExitPresetId"/>, pour qu'un meme preset soit assignable indifferemment
    /// a l'un ou l'autre, ou aux deux sur des jeux differents).
    /// </summary>
    public GameVisualMode Mode { get; set; } = GameVisualMode.Fps;
    public bool FrameRateBoost { get; set; } = true;

    /// <summary>Propriétés dwc.exe additionnelles (ex: Brightness, ColorTemp...) appliquées en plus de Mode/FrameRateBoost.</summary>
    public List<DwcPropertyValue> ExtraProperties { get; set; } = new();

    // Requis pour l'affichage dans les ComboBox themees de cette app (voir CLAUDE.md,
    // "Every record used as a ComboBox item...") -- DisplayMemberPath seul ne suffit pas ici.
    public override string ToString() => Name;
}
