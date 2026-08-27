namespace AsusGameProfiles.Models;

/// <summary>Une propriété dwc.exe arbitraire (ex: Property="Brightness", Value="80"), appliquée via <c>dwc.exe set &lt;Property&gt; &lt;Value&gt;</c>.</summary>
public record DwcPropertyValue(string Property, string Value);

/// <summary>
/// Une entrée "jeu" suivie par l'app : juste un pointeur (exécutable / AppID Steam) plus les presets à
/// appliquer -- les réglages dwc.exe eux-mêmes vivent dans les <see cref="GameProfilePreset"/> référencés
/// par <see cref="OnLaunchPresetId"/>/<see cref="OnExitPresetId"/> (2026-08-27 : deux slots indépendants,
/// plutôt qu'un preset unique qui devait lui-même porter un réglage "exit" optionnel -- confus quand un
/// jeu voulait un preset au lancement et un preset différent à la sortie, ce que l'ancien modèle ne
/// permettait pas directement). N'importe quel preset peut être assigné à l'un ou l'autre slot, ou aux
/// deux, sur des jeux différents, sans dupliquer les réglages.
///
/// Le déclenchement se fait uniquement par surveillance de processus (voir
/// <see cref="AsusGameProfiles.Services.ProcessWatcherService"/>) -- l'ancien mécanisme optionnel via les
/// options de lancement Steam (wrapper <c>--launch</c> autour de <c>%command%</c>) a été retiré (2026-08-27,
/// à la demande de l'utilisateur) au profit d'un seul mécanisme simple et uniforme, Steam ou non-Steam.
/// </summary>
public class GameProfile
{
    /// <summary>
    /// AppID Steam (ex: "730" pour CS2) pour un jeu Steam,
    /// ou un identifiant "manual:&lt;guid&gt;" pour une entrée ajoutée manuellement.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Chemin complet vers l'exécutable réel du jeu -- utilisé à la fois pour l'affichage et pour la détection par <see cref="AsusGameProfiles.Services.ProcessWatcherService"/>.</summary>
    public string ExePath { get; set; } = string.Empty;

    public bool IsSteamGame { get; set; }

    /// <summary>Id du <see cref="GameProfilePreset"/> appliqué au lancement de ce jeu (vide = rien n'est changé au lancement).</summary>
    public string OnLaunchPresetId { get; set; } = string.Empty;

    /// <summary>Id du <see cref="GameProfilePreset"/> appliqué à la fermeture de ce jeu (vide = le "Default exit profile" global s'applique à la place, voir <see cref="AppConfig.DefaultExitMode"/>).</summary>
    public string OnExitPresetId { get; set; } = string.Empty;
}
