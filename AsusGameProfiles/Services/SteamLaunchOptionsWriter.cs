using System.Diagnostics;
using System.IO;
using System.Text;

namespace AsusGameProfiles.Services;

public record VdfWriteResult(bool Success, string Message);

/// <summary>
/// Écrit/efface directement la clé "LaunchOptions" d'un jeu dans localconfig.vdf, pour éviter
/// tout copier-coller manuel dans Steam. C'est le seul fichier touché, et seule la valeur
/// LaunchOptions du bloc de l'appid concerné est modifiée -- tout le reste du fichier
/// (cloud saves, autres réglages, autres jeux) est recopié tel quel, byte pour byte.
///
/// Garde-fous : Steam DOIT être fermé pendant l'écriture (sinon il peut réécrire le fichier
/// depuis sa copie en mémoire et effacer notre changement, voire corrompre le fichier en cas
/// d'accès concurrent), et une sauvegarde horodatée est créée avant chaque écriture (via
/// <see cref="File.Replace(string, string, string)"/>, pour que remplacement + sauvegarde soient
/// aussi atomiques que le permet le système de fichiers -- pas de fenêtre où le fichier serait
/// tronqué si le processus est interrompu en cours d'écriture).
/// </summary>
public static class SteamLaunchOptionsWriter
{
    /// <summary>Vrai si un processus "steam.exe" tourne actuellement sur la machine.</summary>
    public static bool IsSteamRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    /// <summary>
    /// Définit (ou remplace) la valeur "LaunchOptions" de l'appid donné dans localconfig.vdf.
    /// </summary>
    /// <param name="isSteamRunning">
    /// Permet d'injecter un détecteur de remplacement pour les tests ; utilise <see cref="IsSteamRunning"/> par défaut.
    /// </param>
    public static VdfWriteResult SetLaunchOptions(string steamPath, string appId, string launchOptions, Func<bool>? isSteamRunning = null)
        => EditLaunchOptions(steamPath, appId, launchOptions, isSteamRunning ?? IsSteamRunning);

    /// <summary>
    /// Efface la valeur "LaunchOptions" de l'appid donné dans localconfig.vdf (no-op propre si elle
    /// n'existait déjà pas, ou si l'appid n'a pas de bloc du tout).
    /// </summary>
    /// <param name="isSteamRunning">
    /// Permet d'injecter un détecteur de remplacement pour les tests ; utilise <see cref="IsSteamRunning"/> par défaut.
    /// </param>
    public static VdfWriteResult ClearLaunchOptions(string steamPath, string appId, Func<bool>? isSteamRunning = null)
        => EditLaunchOptions(steamPath, appId, "", isSteamRunning ?? IsSteamRunning);

    private static VdfWriteResult EditLaunchOptions(string steamPath, string appId, string newValue, Func<bool> isSteamRunning)
    {
        if (isSteamRunning())
            return new VdfWriteResult(false,
                "Steam is open. Fully close Steam (including the system tray icon) and try again -- " +
                "editing this file while Steam is running risks losing the change or corrupting your Steam config.");

        var localConfigPath = FindLocalConfigPath(steamPath);
        if (localConfigPath is null)
            return new VdfWriteResult(false, "Could not find localconfig.vdf (no userdata folder with a config was found).");

        string content;
        Encoding encoding;
        try
        {
            encoding = DetectEncoding(localConfigPath);
            content = File.ReadAllText(localConfigPath, encoding);
        }
        catch (Exception ex)
        {
            return new VdfWriteResult(false, $"Could not read localconfig.vdf: {ex.Message}");
        }

        var appsBlock = FindPath(content, "UserLocalConfigStore", "Software", "Valve", "Steam", "apps");
        if (appsBlock is null)
            return new VdfWriteResult(false, "Unexpected structure in localconfig.vdf (\"apps\" block not found) -- aborting, nothing was changed.");

        string newContent;
        var appBlock = FindChildBlock(content, appsBlock.Value.BraceOpen + 1, appsBlock.Value.BraceClose, appId);

        if (appBlock is not null)
        {
            var kv = FindKeyValue(content, appBlock.Value.BraceOpen + 1, appBlock.Value.BraceClose, "LaunchOptions");
            if (kv is not null)
            {
                // Remplace uniquement le texte entre les guillemets de la valeur existante.
                newContent = content[..kv.Value.ValueStart] + EscapeVdf(newValue) + content[kv.Value.ValueEnd..];
            }
            else if (newValue.Length == 0)
            {
                // Rien à effacer, la clé n'existe déjà pas : rien à faire.
                return new VdfWriteResult(true, "No launch options were set for this game.");
            }
            else
            {
                string indent = DetectChildIndent(content, appBlock.Value.BraceOpen);
                string insertion = $"\n{indent}\"LaunchOptions\"\t\t\"{EscapeVdf(newValue)}\"";
                newContent = content.Insert(appBlock.Value.BraceOpen + 1, insertion);
            }
        }
        else
        {
            if (newValue.Length == 0)
                return new VdfWriteResult(true, "This game has no block in localconfig.vdf, nothing to clear.");

            string indent = DetectChildIndent(content, appsBlock.Value.BraceOpen);
            string childIndent = indent + "\t";
            string insertion = $"\n{indent}\"{appId}\"\n{indent}{{\n{childIndent}\"LaunchOptions\"\t\t\"{EscapeVdf(newValue)}\"\n{indent}}}";
            newContent = content.Insert(appsBlock.Value.BraceOpen + 1, insertion);
        }

        var tempPath = localConfigPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, newContent, encoding);
            var backupPath = $"{localConfigPath}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Replace(tempPath, localConfigPath, backupPath);
        }
        catch (Exception ex)
        {
            return new VdfWriteResult(false, $"Write failed (nothing was changed): {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }

        return new VdfWriteResult(true, "Steam launch options updated.");
    }

    private static string? FindLocalConfigPath(string steamPath)
    {
        var userDataDir = Path.Combine(steamPath, "userdata");
        if (!Directory.Exists(userDataDir)) return null;

        string? best = null;
        DateTime bestTime = DateTime.MinValue;

        foreach (var accountDir in Directory.GetDirectories(userDataDir))
        {
            var candidate = Path.Combine(accountDir, "config", "localconfig.vdf");
            if (!File.Exists(candidate)) continue;

            var writeTime = File.GetLastWriteTimeUtc(candidate);
            if (best is null || writeTime > bestTime)
            {
                best = candidate;
                bestTime = writeTime;
            }
        }
        // S'il y a plusieurs comptes Windows ayant utilisé Steam sur cette machine, on prend le
        // fichier localconfig.vdf modifié le plus récemment (= le compte actif en pratique).
        return best;
    }

    /// <summary>
    /// Détecte si le fichier commence par un BOM UTF-8, pour ré-écrire avec exactement la même
    /// présence/absence de BOM que l'original (Steam génère ses .vdf sans BOM, mais on ne devine pas).
    /// </summary>
    private static Encoding DetectEncoding(string path)
    {
        Span<byte> head = stackalloc byte[3];
        using (var fs = File.OpenRead(path))
        {
            int read = fs.Read(head);
            if (read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    // ---------- Utilitaires de navigation VDF (format KeyValues de Valve) ----------

    private readonly record struct VdfBlock(int KeyIndex, int BraceOpen, int BraceClose);

    /// <summary>Un enfant direct (profondeur 0 par rapport à la plage donnée) : soit un sous-bloc, soit une paire clé/valeur.</summary>
    private readonly record struct VdfEntry(string Key, int KeyIndex, bool IsBlock, int BraceOpen, int BraceClose, int ValueStart, int ValueEnd);

    private static VdfBlock? FindPath(string content, params string[] keys)
    {
        int start = 0, end = content.Length;
        VdfBlock? current = null;

        foreach (var key in keys)
        {
            current = FindChildBlock(content, start, end, key);
            if (current is null) return null;
            start = current.Value.BraceOpen + 1;
            end = current.Value.BraceClose;
        }
        return current;
    }

    private static VdfBlock? FindChildBlock(string content, int rangeStart, int rangeEnd, string key)
    {
        foreach (var entry in EnumerateDirectChildren(content, rangeStart, rangeEnd))
        {
            if (entry.IsBlock && string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                return new VdfBlock(entry.KeyIndex, entry.BraceOpen, entry.BraceClose);
        }
        return null;
    }

    private static (int KeyIndex, int ValueStart, int ValueEnd)? FindKeyValue(string content, int rangeStart, int rangeEnd, string key)
    {
        foreach (var entry in EnumerateDirectChildren(content, rangeStart, rangeEnd))
        {
            if (!entry.IsBlock && string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                return (entry.KeyIndex, entry.ValueStart, entry.ValueEnd);
        }
        return null;
    }

    /// <summary>
    /// Énumère uniquement les clés directement enfants de la plage donnée (profondeur 1), en sautant
    /// intégralement le contenu des sous-blocs imbriqués -- c'est ce qui garantit qu'on ne modifie/ne
    /// trouve jamais une clé "LaunchOptions" ou un appid qui se trouverait, par coïncidence, plus profond
    /// dans l'arbre (par ex. dans un sous-bloc "cloud" ou "achievements" d'un autre jeu).
    /// </summary>
    private static IEnumerable<VdfEntry> EnumerateDirectChildren(string content, int rangeStart, int rangeEnd)
    {
        int i = rangeStart;
        while (i < rangeEnd)
        {
            while (i < rangeEnd && char.IsWhiteSpace(content[i])) i++;
            if (i >= rangeEnd) yield break;

            if (content[i] != '"')
            {
                // Ne devrait pas arriver dans un .vdf bien formé : on avance d'un caractère pour ne
                // jamais boucler indéfiniment plutôt que de planter sur un fichier inattendu.
                i++;
                continue;
            }

            int keyStart = i + 1;
            int keyEnd = keyStart;
            while (keyEnd < rangeEnd && content[keyEnd] != '"')
            {
                if (content[keyEnd] == '\\' && keyEnd + 1 < rangeEnd) keyEnd++;
                keyEnd++;
            }
            if (keyEnd >= rangeEnd) yield break; // guillemet de clé non refermé : fichier tronqué/malformé.

            int keyIndex = i;
            string key = content[keyStart..keyEnd];
            i = keyEnd + 1;

            while (i < rangeEnd && char.IsWhiteSpace(content[i])) i++;
            if (i >= rangeEnd) yield break;

            if (content[i] == '{')
            {
                int braceOpen = i;
                int braceClose = FindMatchingBrace(content, braceOpen);
                if (braceClose < 0 || braceClose > rangeEnd) yield break; // bloc mal formé ou dépassant la plage.

                yield return new VdfEntry(key, keyIndex, true, braceOpen, braceClose, -1, -1);
                i = braceClose + 1;
            }
            else if (content[i] == '"')
            {
                int valueStart = i + 1;
                int valueEnd = valueStart;
                while (valueEnd < rangeEnd && content[valueEnd] != '"')
                {
                    if (content[valueEnd] == '\\' && valueEnd + 1 < rangeEnd) valueEnd++;
                    valueEnd++;
                }
                if (valueEnd >= rangeEnd) yield break; // guillemet de valeur non refermé.

                yield return new VdfEntry(key, keyIndex, false, -1, -1, valueStart, valueEnd);
                i = valueEnd + 1;
            }
            else
            {
                // Une clé doit être suivie d'un bloc ou d'une valeur entre guillemets : structure
                // inattendue, on arrête l'énumération proprement plutôt que de mal interpréter la suite.
                yield break;
            }
        }
    }

    /// <summary>Trouve l'accolade fermante correspondant à l'accolade ouvrante en position openIndex,
    /// en ignorant les accolades qui apparaissent à l'intérieur de chaînes entre guillemets.</summary>
    private static int FindMatchingBrace(string s, int openIndex)
    {
        int depth = 0;
        bool inString = false;

        for (int i = openIndex; i < s.Length; i++)
        {
            char c = s[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < s.Length) { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }

            if (c == '"') { inString = true; continue; }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static string DetectChildIndent(string content, int braceOpenIndex)
    {
        int lineStart = braceOpenIndex + 1;
        while (lineStart < content.Length && (content[lineStart] == '\r' || content[lineStart] == '\n')) lineStart++;

        int i = lineStart;
        while (i < content.Length && (content[i] == ' ' || content[i] == '\t')) i++;
        if (i > lineStart) return content[lineStart..i];

        // Bloc vide : indentation de la ligne de la clé elle-même + un niveau.
        int keyLineStart = braceOpenIndex;
        while (keyLineStart > 0 && content[keyLineStart - 1] != '\n') keyLineStart--;

        int j = keyLineStart;
        while (j < content.Length && (content[j] == ' ' || content[j] == '\t')) j++;
        return content[keyLineStart..j] + "\t";
    }

    private static string EscapeVdf(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
