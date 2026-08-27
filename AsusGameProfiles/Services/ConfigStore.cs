using System.IO;
using System.Text.Json;
using AsusGameProfiles.Models;

namespace AsusGameProfiles.Services;

/// <summary>Lecture/écriture de la config de l'app (%AppData%\AsusGameProfiles\config.json) et des logs de lancement.</summary>
public static class ConfigStore
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AsusGameProfiles");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>Charge la config depuis le disque, ou retourne une config vierge si absente/corrompue.</summary>
    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config != null)
                {
                    var changed = MigrateLegacyInlineProfiles(config, json);
                    changed |= MigrateBundledPresetSplit(config, json);
                    if (changed) Save(config);
                    return config;
                }
            }
        }
        catch
        {
            // Config corrompue ou illisible : on repart d'une config vierge plutôt que de planter
            // (important en mode lanceur silencieux -- mieux vaut lancer le jeu sans profil que ne pas le lancer du tout).
        }
        return new AppConfig();
    }

    /// <summary>
    /// Migration ponctuelle (introduite 2026-08-27, mise a jour le meme jour pour ecrire directement le
    /// format actuel) : avant les GameProfilePreset, chaque GameProfile portait ses propres LaunchMode/
    /// ExitMode/etc. directement. Ces propriétés n'existent plus sur la classe, donc la désérialisation
    /// normale les ignore silencieusement (et perdrait les réglages déjà enregistrés par l'utilisateur)
    /// -- on les relit ici depuis le JSON brut pour fabriquer un preset "on launch" équivalent (et un
    /// second preset "on exit" si l'ancien OverrideExitSettings était coché), nommés d'après le jeu, et
    /// les assigner à <see cref="GameProfile.OnLaunchPresetId"/>/<see cref="GameProfile.OnExitPresetId"/>.
    /// Vrai si une migration a eu lieu (l'appelant doit alors sauvegarder pour ne pas la refaire au
    /// prochain démarrage).
    /// </summary>
    private static bool MigrateLegacyInlineProfiles(AppConfig config, string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (!doc.RootElement.TryGetProperty("Profiles", out var profilesJson)) return false;

            var migrated = false;
            var i = 0;
            foreach (var profileJson in profilesJson.EnumerateArray())
            {
                if (i >= config.Profiles.Count) break;
                var profile = config.Profiles[i];
                i++;

                // Deja migre, ou jamais eu l'ancien format (pas de LaunchMode inline) : rien a faire.
                if (!string.IsNullOrEmpty(profile.OnLaunchPresetId)) continue;
                if (!profileJson.TryGetProperty("LaunchMode", out var launchModeJson)) continue;

                var launchPreset = new GameProfilePreset
                {
                    Name = string.IsNullOrEmpty(profile.DisplayName) ? "Migrated profile" : $"{profile.DisplayName} profile",
                    Mode = (GameVisualMode)launchModeJson.GetInt32(),
                    FrameRateBoost = profileJson.TryGetProperty("LaunchFrameRateBoost", out var lfrb) && lfrb.GetBoolean()
                };
                if (profileJson.TryGetProperty("LaunchExtraProperties", out var lep))
                    launchPreset.ExtraProperties = JsonSerializer.Deserialize<List<DwcPropertyValue>>(lep.GetRawText(), JsonOptions) ?? new();

                config.Presets.Add(launchPreset);
                profile.OnLaunchPresetId = launchPreset.Id;

                if (profileJson.TryGetProperty("OverrideExitSettings", out var ov) && ov.GetBoolean())
                {
                    var exitPreset = new GameProfilePreset
                    {
                        Name = $"{launchPreset.Name} (exit)",
                        Mode = profileJson.TryGetProperty("ExitMode", out var em) ? (GameVisualMode)em.GetInt32() : GameVisualMode.Srgb,
                        FrameRateBoost = profileJson.TryGetProperty("ExitFrameRateBoost", out var efrb) && efrb.GetBoolean()
                    };
                    if (profileJson.TryGetProperty("ExitExtraProperties", out var eep))
                        exitPreset.ExtraProperties = JsonSerializer.Deserialize<List<DwcPropertyValue>>(eep.GetRawText(), JsonOptions) ?? new();

                    config.Presets.Add(exitPreset);
                    profile.OnExitPresetId = exitPreset.Id;
                }

                migrated = true;
            }
            return migrated;
        }
        catch
        {
            return false; // Meilleur effort : une migration ratee ne doit pas empecher l'app de demarrer.
        }
    }

    /// <summary>
    /// Migration ponctuelle (2026-08-27, séparation on-launch/on-exit) : la génération précédente de
    /// presets portait à la fois LaunchMode/LaunchFrameRateBoost/LaunchExtraProperties ET un
    /// OverrideExitSettings/ExitMode/ExitFrameRateBoost/ExitExtraProperties optionnel sur le MÊME objet,
    /// et GameProfile référençait un seul preset via PresetId. Ces champs n'existent plus (GameProfilePreset
    /// est maintenant un simple état d'affichage Mode/FrameRateBoost/ExtraProperties, et GameProfile a
    /// deux slots indépendants OnLaunchPresetId/OnExitPresetId) -- sans cette migration, la désérialisation
    /// normale donnerait des presets aux réglages par défaut (perte silencieuse) et des profils sans
    /// preset du tout. Le preset existant devient le preset "on launch" (même Id, donc déjà référencé
    /// correctement par tout profil qui pointait dessus une fois OnLaunchPresetId réécrit) ; si
    /// l'ancien OverrideExitSettings était coché, un second preset "&lt;nom&gt; (exit)" est créé pour
    /// porter les anciens réglages de sortie et assigné à OnExitPresetId. Vrai si une migration a eu
    /// lieu (l'appelant doit alors sauvegarder pour ne pas la refaire au prochain démarrage).
    /// </summary>
    private static bool MigrateBundledPresetSplit(AppConfig config, string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (!doc.RootElement.TryGetProperty("Presets", out var presetsJson)) return false;

            var launchIdByOldId = new Dictionary<string, string>();
            var exitIdByOldId = new Dictionary<string, string>();
            var migrated = false;

            var i = 0;
            foreach (var presetJson in presetsJson.EnumerateArray())
            {
                if (i >= config.Presets.Count) break;
                var preset = config.Presets[i];
                i++;

                // Deja au nouveau format (pas de LaunchMode sur l'ancien objet) : rien a faire.
                if (!presetJson.TryGetProperty("LaunchMode", out var launchModeJson)) continue;

                var oldId = preset.Id;
                preset.Mode = (GameVisualMode)launchModeJson.GetInt32();
                preset.FrameRateBoost = presetJson.TryGetProperty("LaunchFrameRateBoost", out var lfrb) && lfrb.GetBoolean();
                if (presetJson.TryGetProperty("LaunchExtraProperties", out var lep))
                    preset.ExtraProperties = JsonSerializer.Deserialize<List<DwcPropertyValue>>(lep.GetRawText(), JsonOptions) ?? new();
                launchIdByOldId[oldId] = preset.Id;
                migrated = true;

                if (presetJson.TryGetProperty("OverrideExitSettings", out var ov) && ov.GetBoolean())
                {
                    var exitPreset = new GameProfilePreset
                    {
                        Name = $"{preset.Name} (exit)",
                        Mode = presetJson.TryGetProperty("ExitMode", out var em) ? (GameVisualMode)em.GetInt32() : GameVisualMode.Srgb,
                        FrameRateBoost = presetJson.TryGetProperty("ExitFrameRateBoost", out var efrb) && efrb.GetBoolean()
                    };
                    if (presetJson.TryGetProperty("ExitExtraProperties", out var eep))
                        exitPreset.ExtraProperties = JsonSerializer.Deserialize<List<DwcPropertyValue>>(eep.GetRawText(), JsonOptions) ?? new();

                    config.Presets.Add(exitPreset);
                    exitIdByOldId[oldId] = exitPreset.Id;
                }
            }

            if (!migrated) return false;

            if (doc.RootElement.TryGetProperty("Profiles", out var profilesJson))
            {
                var j = 0;
                foreach (var profileJson in profilesJson.EnumerateArray())
                {
                    if (j >= config.Profiles.Count) break;
                    var profile = config.Profiles[j];
                    j++;

                    if (!profileJson.TryGetProperty("PresetId", out var presetIdJson)) continue;
                    var oldPresetId = presetIdJson.GetString() ?? "";
                    if (oldPresetId.Length == 0) continue;

                    if (launchIdByOldId.TryGetValue(oldPresetId, out var newLaunchId))
                        profile.OnLaunchPresetId = newLaunchId;
                    if (exitIdByOldId.TryGetValue(oldPresetId, out var newExitId))
                        profile.OnExitPresetId = newExitId;
                }
            }

            return true;
        }
        catch
        {
            return false; // Meilleur effort : une migration ratee ne doit pas empecher l'app de demarrer.
        }
    }

    /// <summary>Sérialise et écrit la config sur le disque, en créant le dossier si besoin.</summary>
    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>Dossier des logs de lancement (un fichier par exécution en mode <c>--launch</c>), créé s'il n'existe pas.</summary>
    public static string LogDirectory
    {
        get
        {
            var dir = Path.Combine(ConfigDir, "logs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
