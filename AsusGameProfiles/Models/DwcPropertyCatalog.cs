namespace AsusGameProfiles.Models;

/// <summary>Type d'éditeur le plus adapté à une propriété dwc.exe, pour guider l'utilisateur au maximum.</summary>
public enum DwcPropertyInputKind
{
    /// <summary>Deux valeurs possibles (0/1) : affiché comme une case à cocher.</summary>
    Boolean,
    /// <summary>Plage documentée 0-100 : affiché comme un slider.</summary>
    Range0To100,
    /// <summary>Ensemble fermé de valeurs nommées (ex: ColorTemp, InputSource...) : affiché comme un menu déroulant guidé.</summary>
    Enum,
    /// <summary>Plage "0-Max" dont le maximum dépend du moniteur (non documenté/vérifiable sans écrire sur le moniteur), ou champ encodé par bits : champ texte libre.</summary>
    FreeText
}

/// <summary>Une valeur possible d'une propriété <see cref="DwcPropertyInputKind.Enum"/> (ex: Value="17", Label="HDMI-1").</summary>
public record DwcEnumOption(string Value, string Label)
{
    public override string ToString() => Label;
}

public record DwcPropertyInfo(string Name, string Description, DwcPropertyInputKind Kind, DwcEnumOption[]? Options = null);

/// <summary>
/// Liste des propriétés supportées par <c>dwc.exe set &lt;prop&gt; &lt;value&gt;</c>, relevée directement
/// depuis <c>dwc.exe --help</c> (ASUS Display Control CLI) -- descriptions, bornes de valeurs et listes
/// d'options nommées incluses, pour que chaque propriété ait un éditeur pleinement guidé (case à cocher,
/// slider ou menu déroulant) plutôt qu'un champ texte libre, sauf pour les quelques propriétés dont la
/// plage réelle ("0-Max") dépend du moniteur et n'est pas documentée, ou qui sont encodées par bits.
/// Utilisée aussi pour peupler la liste de suggestions du sélecteur de propriété -- l'utilisateur reste
/// libre de taper un autre nom de propriété (traité en texte libre) si une future version de dwc.exe en
/// ajoute. Les propriétés en lecture seule (UsageTime, SelfCalFWVersion) et celles qui ont déjà un
/// contrôle dédié (GameVisual, FrameRateBoost) sont volontairement exclues pour éviter les doublons.
/// </summary>
public static class DwcPropertyCatalog
{
    private static DwcEnumOption[] Opts(params (string Value, string Label)[] items) =>
        items.Select(i => new DwcEnumOption(i.Value, i.Label)).ToArray();

    public static readonly DwcPropertyInfo[] KnownProperties =
    {
        // Image & Color
        new("Brightness", "Brightness level (0-100).", DwcPropertyInputKind.Range0To100),
        new("BlueLightFilter", "Blue light filter level (0:OFF, 1-Max -- maximum depends on the monitor).", DwcPropertyInputKind.FreeText),
        new("Contrast", "Contrast level (0-100).", DwcPropertyInputKind.Range0To100),
        new("ColorTemp", "Color temperature.", DwcPropertyInputKind.Enum, Opts(
            ("3", "4000K"), ("4", "5000K"), ("5", "6500K"), ("6", "7500K"), ("7", "8200K"),
            ("8", "9300K"), ("9", "10000K"), ("11", "User"))),
        new("Gamma", "Gamma curve.", DwcPropertyInputKind.Enum, Opts(
            ("1.8", "1.8"), ("2.0", "2.0"), ("2.2", "2.2"), ("2.4", "2.4"), ("2.6", "2.6"))),
        new("Hue", "Color hue (0-100).", DwcPropertyInputKind.Range0To100),
        new("Saturation", "Color saturation (0-100).", DwcPropertyInputKind.Range0To100),
        new("Sharpness", "Sharpness (0-100).", DwcPropertyInputKind.Range0To100),
        new("RedGain", "Red channel gain (0-100).", DwcPropertyInputKind.Range0To100),
        new("GreenGain", "Green channel gain (0-100).", DwcPropertyInputKind.Range0To100),
        new("BlueGain", "Blue channel gain (0-100).", DwcPropertyInputKind.Range0To100),
        new("RedOffset", "Red channel offset (0-100).", DwcPropertyInputKind.Range0To100),
        new("GreenOffset", "Green channel offset (0-100).", DwcPropertyInputKind.Range0To100),
        new("BlueOffset", "Blue channel offset (0-100).", DwcPropertyInputKind.Range0To100),
        new("Overdrive", "Improve pixel response time / Trace Free (0:OFF, 1-Max -- maximum depends on the monitor).", DwcPropertyInputKind.FreeText),
        new("ShadowBoost", "Brightens dark scene detail (0:OFF, 1-Max -- maximum depends on the monitor).", DwcPropertyInputKind.FreeText),
        new("DynamicDimming", "Dynamic local dimming (0:OFF, 1-Max -- maximum depends on the monitor).", DwcPropertyInputKind.FreeText),
        new("ASCR", "ASUS Smart Contrast Ratio.", DwcPropertyInputKind.Boolean),
        new("Uniformity", "Screen uniformity compensation.", DwcPropertyInputKind.Boolean),

        // Display Modes
        new("Splendid", "Splendid preset, for ZenScreen/Business models.", DwcPropertyInputKind.Enum, Opts(
            ("1", "Theater"), ("2", "Scenery"), ("3", "sRGB"), ("4", "Standard"), ("5", "Game"),
            ("6", "Night View"), ("7", "Reading"), ("8", "Darkroom"))),
        new("ProArtPreset", "ProArt preset, for ProArt models.", DwcPropertyInputKind.Enum, Opts(
            ("0", "Native"), ("1", "sRGB"), ("2", "AdobeRGB"), ("3", "Rec.2020"),
            ("4", "DCI-P3"), ("5", "DICOM"), ("6", "Rec.709"))),
        new("InputRange", "Input signal range.", DwcPropertyInputKind.Enum, Opts(
            ("0", "Auto"), ("1", "Full"), ("2", "Limited 16-235"), ("3", "Limited 16-254"), ("4", "SDI Full"))),
        new("DualDisplay", "ZenScreen Dual Display mode.", DwcPropertyInputKind.Enum, Opts(
            ("1", "Mirror"), ("2", "Extend"), ("3", "Split"), ("4", "Independent"))),
        new("PxP", "Picture-in-Picture / Picture-by-Picture mode.", DwcPropertyInputKind.Enum, Opts(
            ("0", "OFF"), ("1", "PIP Top-Right"), ("2", "PIP Top-Left"), ("3", "PIP Bottom-Right"), ("4", "PIP Bottom-Left"),
            ("5", "PBP Left/Right Equal"), ("6", "PBP Left Large"), ("7", "PBP Right Large"),
            ("8", "Frame x4 (2x2)"), ("9", "Frame x3 (Left Large)"), ("10", "Frame x3 (Right Large)"))),
        new("PIPSize", "PIP window size.", DwcPropertyInputKind.Enum, Opts(
            ("0", "OFF"), ("1", "Small"), ("2", "Medium"), ("3", "Large"))),

        // System Setup
        new("AudioVolume", "Built-in speaker volume (0-100).", DwcPropertyInputKind.Range0To100),
        new("PowerSaving", "Power mode.", DwcPropertyInputKind.Enum, Opts(
            ("0", "Standard/Performance"), ("1", "Power Saving"))),
        new("PowerIndicator", "Power LED indicator.", DwcPropertyInputKind.Boolean),
        new("PowerKeyLock", "Locks the physical power key.", DwcPropertyInputKind.Boolean),
        new("KeyLock", "Locks the physical OSD keys.", DwcPropertyInputKind.Boolean),
        new("SoundMute", "Mutes the built-in speakers.", DwcPropertyInputKind.Boolean),
        new("InputDetection", "Automatic input source detection.", DwcPropertyInputKind.Boolean),
        new("InputSource", "Active video input.", DwcPropertyInputKind.Enum, Opts(
            ("1", "VGA"), ("15", "DP-1"), ("16", "DP-2"), ("17", "HDMI-1"), ("18", "HDMI-2"), ("19", "HDMI-3"),
            ("21", "TB-1"), ("22", "TB-2"), ("26", "USBC-1"), ("27", "USBC-2"), ("30", "SDI-1"), ("31", "SDI-2"))),
        new("OSDTransparency", "On-screen display menu transparency (0-Max -- maximum depends on the monitor).", DwcPropertyInputKind.FreeText),
        new("OSDTimeout", "On-screen display menu timeout, in seconds (0-Max -- maximum depends on the monitor).", DwcPropertyInputKind.FreeText),
        new("OSDLanguage", "On-screen display menu language.", DwcPropertyInputKind.Enum, Opts(
            ("1", "Chinese Traditional"), ("2", "English"), ("3", "French"), ("4", "German"), ("5", "Italian"),
            ("6", "Japanese"), ("7", "Korean"), ("8", "Portuguese"), ("9", "Russian"), ("10", "Spanish"),
            ("12", "Turkish"), ("13", "Chinese Simplified"), ("17", "Croatian"), ("18", "Czech"), ("20", "Dutch"),
            ("26", "Hungarian"), ("30", "Polish"), ("31", "Romanian"), ("35", "Thai"), ("36", "Ukrainian"),
            ("37", "Vietnamese"), ("38", "Persian"), ("39", "Indonesian"))),
        new("EZOSD", "Remote OSD menu control.", DwcPropertyInputKind.Enum, Opts(
            ("0", "Close Menu"), ("1", "Show Menu"), ("2", "Up"), ("3", "Down"),
            ("4", "Right"), ("5", "Left"), ("6", "Enter"), ("7", "Back"))),

        // GamePlus & QuickFit
        new("FPS", "GamePlus FPS counter overlay.", DwcPropertyInputKind.Enum, Opts(
            ("0", "OFF"), ("1", "Numerical"), ("2", "Bar Graph"))),
        new("Timer", "GamePlus timer overlay.", DwcPropertyInputKind.Enum, Opts(
            ("0", "OFF"), ("1", "30s"), ("2", "40s"), ("3", "50s"), ("4", "60s"), ("5", "90s"))),
        new("Crosshair", "GamePlus crosshair overlay.", DwcPropertyInputKind.Enum, Opts(
            ("0", "OFF"), ("7", "Blue dot"), ("8", "Green dot"), ("9", "Blue target"),
            ("10", "Green target"), ("11", "Blue crosshair"), ("12", "Green crosshair"))),
        new("DisplayAlignment", "Shows alignment marks for lining up multiple monitors.", DwcPropertyInputKind.Boolean),

        // Aura Lighting
        new("AuraColor", "Aura RGB lighting color.", DwcPropertyInputKind.Enum, Opts(
            ("0", "OFF"), ("1", "Red"), ("2", "Green"), ("3", "Blue"), ("4", "Cyan"), ("5", "Magenta"), ("6", "Yellow"))),
        new("AuraMode", "Aura RGB lighting effect.", DwcPropertyInputKind.Enum, Opts(
            ("0", "OFF"), ("1", "Aura Sync (Armoury Crate)"), ("2", "Rainbow"), ("3", "Color Cycle"),
            ("4", "Static"), ("5", "Breathing"), ("6", "Strobing"))),

        // ProArt Self-calibration
        new("SelfCalStart", "Starts ProArt self-calibration.", DwcPropertyInputKind.Enum, Opts(
            ("0", "OFF"), ("1", "Start with warm up"), ("2", "Start without warm up"))),
        new("SelfCalTarget", "Self-calibration target color space.", DwcPropertyInputKind.Enum, Opts(
            ("0", "sRGB"), ("1", "Adobe RGB"), ("2", "Rec.2020"), ("3", "DCI-P3"),
            ("4", "DICOM"), ("5", "Rec.709"), ("6", "HDR_PQ"), ("7", "HDR_HLG"))),
        new("SelfCalRoutine", "Self-calibration repeat schedule.", DwcPropertyInputKind.Enum, Opts(
            ("0", "Off"), ("1", "Single"), ("2", "Daily"), ("3", "Every 7 days"), ("4", "Every 14 days"), ("5", "Every 28 days"))),
        new("SelfCalSystemDate", "Self-calibration MCU system date, encoded by bits ([4:0]:Day, [8:5]:Month, [15:9]:Years since 2000).", DwcPropertyInputKind.FreeText),
        new("SelfCalSystemClock", "Self-calibration MCU system clock, encoded by bits ([5:0]:Minute, [10:6]:Hour).", DwcPropertyInputKind.FreeText),
        new("SelfCalApptDate", "Self-calibration scheduled appointment date, encoded by bits (same layout as SelfCalSystemDate).", DwcPropertyInputKind.FreeText),
        new("SelfCalApptClock", "Self-calibration scheduled appointment time, encoded by bits (same layout as SelfCalSystemClock).", DwcPropertyInputKind.FreeText),

        // OLED Features
        new("OledAntiFlicker", "OLED anti-flicker mode.", DwcPropertyInputKind.Boolean),
        new("OledScreenMove", "OLED pixel-shift screen move amount, to reduce burn-in (0-Max -- maximum depends on the monitor).", DwcPropertyInputKind.FreeText),
        new("OledWarningTimer", "Hours of use before an OLED pixel-cleaning reminder appears (0-Max -- maximum depends on the monitor).", DwcPropertyInputKind.FreeText),
        new("OledPixelClean", "Runs the OLED pixel-cleaning routine.", DwcPropertyInputKind.Enum, Opts(
            ("0", "Stop"), ("1", "Start"))),
        new("OledDisplaySaver", "OLED screen saver.", DwcPropertyInputKind.Boolean),
        new("OledScreenDimming", "OLED screen saver: dims the screen.", DwcPropertyInputKind.Boolean),
        new("OledUniformBrightness", "OLED uniform brightness compensation.", DwcPropertyInputKind.Boolean),
        new("OledOuterDimming", "OLED screen saver: dims the outer edges of the screen.", DwcPropertyInputKind.Boolean),
        new("OledGlobalDimming", "OLED screen saver: dims the entire screen globally.", DwcPropertyInputKind.Boolean),
        new("OledLogoDetection", "Auto logo-brightness: detects static logos to reduce their brightness.", DwcPropertyInputKind.Boolean),
        new("OledTaskbarDetection", "Auto logo-brightness: detects the taskbar to reduce its brightness.", DwcPropertyInputKind.Boolean),
        new("OledBoundaryDetection", "Auto logo-brightness: detects static UI boundaries to reduce their brightness.", DwcPropertyInputKind.Boolean)
    };

    public static DwcPropertyInfo? Find(string propertyName) =>
        KnownProperties.FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
}
