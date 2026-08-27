namespace AsusGameProfiles.Models;

/// <summary>
/// Correspond exactement aux index utilisés par la propriété GameVisual de dwc.exe (vérifié via
/// <c>dwc.exe --help</c> sur un ROG Swift PG27UCWM) : 1:Cinema, 2:Scenery, 3:sRGB, 4:User, 5:Racing,
/// 6:RTS/RPG, 7:FPS, 8:MOBA, 9:Night Vision, 10:sRGB Calibration.
/// </summary>
public enum GameVisualMode
{
    Cinema = 1,
    Scenery = 2,
    Srgb = 3,
    User = 4,
    Racing = 5,
    RtsRpg = 6,
    Fps = 7,
    Moba = 8,
    NightVision = 9,
    SrgbCalibration = 10
}

public static class GameVisualModeExtensions
{
    public static string ToDisplayName(this GameVisualMode mode) => mode switch
    {
        GameVisualMode.Cinema => "Cinema",
        GameVisualMode.Scenery => "Scenery",
        GameVisualMode.Srgb => "sRGB",
        GameVisualMode.User => "User",
        GameVisualMode.Racing => "Racing",
        GameVisualMode.RtsRpg => "RTS / RPG",
        GameVisualMode.Fps => "FPS",
        GameVisualMode.Moba => "MOBA",
        GameVisualMode.NightVision => "Night Vision",
        GameVisualMode.SrgbCalibration => "sRGB Calibration",
        _ => mode.ToString()
    };

    public static IEnumerable<GameVisualMode> All() => Enum.GetValues<GameVisualMode>();

    /// <summary>Convertit une valeur GameVisual brute renvoyée par <c>dwc.exe get GameVisual</c> (ex: "10") en enum ; null si non reconnue.</summary>
    public static GameVisualMode? FromDwcValue(string? raw) =>
        int.TryParse(raw, out var n) && Enum.IsDefined(typeof(GameVisualMode), n) ? (GameVisualMode)n : null;
}

/// <summary>Item d'affichage pour peupler un ComboBox (valeur enum + libellé lisible).</summary>
public record GameVisualModeItem(GameVisualMode Value, string Label)
{
    public override string ToString() => Label;
}

public static class GameVisualModeCatalog
{
    public static List<GameVisualModeItem> All { get; } =
        Enum.GetValues<GameVisualMode>()
            .Select(m => new GameVisualModeItem(m, m.ToDisplayName()))
            .ToList();
}
