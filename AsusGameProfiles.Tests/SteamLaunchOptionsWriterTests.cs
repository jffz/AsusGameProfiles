using System.Text;
using System.Threading;
using AsusGameProfiles.Services;

namespace AsusGameProfiles.Tests;

/// <summary>
/// Fixture localconfig.vdf réaliste (plusieurs jeux, blocs imbriqués) utilisée par la plupart des
/// tests ci-dessous. Deux "LaunchOptions" leurres sont volontairement enfouies plus profond dans
/// l'arbre (dans "cloud" de l'appid 730, et dans "autocloud" de l'appid 570) : elles ne doivent
/// JAMAIS être trouvées/modifiées par le writer, qui ne doit toucher que la clé "LaunchOptions"
/// directement enfant du bloc de l'appid ciblé. C'est le test de régression pour un bug de
/// comptage de profondeur dans l'ancienne implémentation basée sur IndexOf.
/// </summary>
public class SteamLaunchOptionsWriterTests : IDisposable
{
    // Index (0-based) de quelques lignes clés dans BaseLines, pour construire les contenus "attendus"
    // par simple substitution/insertion plutôt qu'en réécrivant le fichier entier à la main.
    private const int AppsBraceOpenIndex = 10;      // "{" du bloc "apps"
    private const int App730LaunchOptionsIndex = 13; // "LaunchOptions" direct de l'appid 730
    private const int App570BraceOpenIndex = 23;     // "{" du bloc de l'appid 570

    private static readonly (int Tabs, string Text)[] BaseLines =
    {
        (0, "\"UserLocalConfigStore\""),
        (0, "{"),
        (1, "\"Software\""),
        (1, "{"),
        (2, "\"Valve\""),
        (2, "{"),
        (3, "\"Steam\""),
        (3, "{"),
        (4, "\"disableAcf\"\t\t\"0\""),
        (4, "\"apps\""),
        (4, "{"),                                                            // 10
        (5, "\"730\""),                                                      // 11
        (5, "{"),                                                            // 12
        (6, "\"LaunchOptions\"\t\t\"-novid -high\""),                        // 13
        (6, "\"cloud\""),                                                    // 14
        (6, "{"),                                                            // 15
        (7, "\"9999\""),                                                     // 16
        (7, "{"),                                                            // 17
        (8, "\"LaunchOptions\"\t\t\"DECOY-A ne doit jamais etre touchee\""),  // 18
        (7, "}"),                                                            // 19
        (6, "}"),                                                            // 20
        (5, "}"),                                                            // 21 (ferme 730)
        (5, "\"570\""),                                                      // 22
        (5, "{"),                                                            // 23
        (6, "\"autocloud\""),                                                // 24
        (6, "{"),                                                            // 25
        (7, "\"lastexit\"\t\t\"1700000000\""),                               // 26
        (7, "\"LaunchOptions\"\t\t\"DECOY-B ne doit jamais etre touchee\""),  // 27
        (6, "}"),                                                            // 28
        (6, "\"Installed\"\t\t\"1\""),                                       // 29
        (5, "}"),                                                            // 30 (ferme 570)
        (5, "\"440\""),                                                      // 31
        (5, "{"),                                                            // 32
        (5, "}"),                                                            // 33 (ferme 440, bloc vide)
        (4, "}"),                                                            // 34 (ferme apps)
        (3, "}"),                                                            // 35 (ferme Steam)
        (2, "}"),                                                            // 36 (ferme Valve)
        (1, "}"),                                                            // 37 (ferme Software)
        (0, "}"),                                                            // 38 (ferme UserLocalConfigStore)
    };

    /// <summary>Variante sans le bloc "apps" du tout, pour le test d'échec propre.</summary>
    private static readonly (int Tabs, string Text)[] NoAppsKeyLines =
    {
        (0, "\"UserLocalConfigStore\""),
        (0, "{"),
        (1, "\"Software\""),
        (1, "{"),
        (2, "\"Valve\""),
        (2, "{"),
        (3, "\"Steam\""),
        (3, "{"),
        (4, "\"disableAcf\"\t\t\"0\""),
        (3, "}"),
        (2, "}"),
        (1, "}"),
        (0, "}"),
    };

    private readonly string _tempRoot;

    public SteamLaunchOptionsWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "AsusGameProfilesTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ---------- Aides de fixture ----------

    private static string Render(IEnumerable<(int Tabs, string Text)> lines) =>
        string.Join("\n", lines.Select(l => new string('\t', l.Tabs) + l.Text)) + "\n";

    private static List<(int Tabs, string Text)> WithReplaced(int index, (int Tabs, string Text) replacement)
    {
        var list = BaseLines.ToList();
        list[index] = replacement;
        return list;
    }

    private static List<(int Tabs, string Text)> WithInserted(int beforeIndex, params (int Tabs, string Text)[] toInsert)
    {
        var list = BaseLines.ToList();
        list.InsertRange(beforeIndex, toInsert);
        return list;
    }

    /// <summary>Crée l'arborescence steamPath/userdata/&lt;accountId&gt;/config/localconfig.vdf et y écrit le contenu donné.</summary>
    private string CreateSteamHome(string content, string accountId = "76561198000000001", bool withBom = false)
    {
        var configDir = Path.Combine(_tempRoot, "userdata", accountId, "config");
        Directory.CreateDirectory(configDir);
        var localConfigPath = Path.Combine(configDir, "localconfig.vdf");
        File.WriteAllText(localConfigPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));
        return localConfigPath;
    }

    private string SteamPath => _tempRoot;

    private static IEnumerable<string> BackupFiles(string localConfigPath) =>
        Directory.GetFiles(Path.GetDirectoryName(localConfigPath)!, "localconfig.vdf.bak-*");

    private static IEnumerable<string> TempFiles(string localConfigPath) =>
        Directory.GetFiles(Path.GetDirectoryName(localConfigPath)!, "localconfig.vdf.tmp-*");

    // ---------- 1. Modifier une LaunchOptions déjà existante ----------

    [Fact]
    public void SetLaunchOptions_ModifiesExistingValue_ForExistingAppId()
    {
        var original = Render(BaseLines);
        var localConfigPath = CreateSteamHome(original);

        var result = SteamLaunchOptionsWriter.SetLaunchOptions(SteamPath, "730", "-high -allow_third_party_software", () => false);

        Assert.True(result.Success, result.Message);

        var expected = Render(WithReplaced(App730LaunchOptionsIndex,
            (6, "\"LaunchOptions\"\t\t\"-high -allow_third_party_software\"")));

        Assert.Equal(expected, File.ReadAllText(localConfigPath));
        Assert.Single(BackupFiles(localConfigPath));
        Assert.Equal(original, File.ReadAllText(BackupFiles(localConfigPath).Single()));
        Assert.Empty(TempFiles(localConfigPath));
    }

    // ---------- 2. Ajouter LaunchOptions à un bloc d'appid existant qui n'en a pas ----------

    [Fact]
    public void SetLaunchOptions_AddsKey_ToExistingAppIdBlockWithoutOne()
    {
        var original = Render(BaseLines);
        var localConfigPath = CreateSteamHome(original);

        var result = SteamLaunchOptionsWriter.SetLaunchOptions(SteamPath, "570", "-newoption 1", () => false);

        Assert.True(result.Success, result.Message);

        var expected = Render(WithInserted(App570BraceOpenIndex + 1,
            (6, "\"LaunchOptions\"\t\t\"-newoption 1\"")));

        Assert.Equal(expected, File.ReadAllText(localConfigPath));
    }

    // ---------- 3. Créer un nouveau bloc d'appid complet quand l'appid n'existe pas encore ----------

    [Fact]
    public void SetLaunchOptions_CreatesNewAppIdBlock_WhenAppIdIsAbsent()
    {
        var original = Render(BaseLines);
        var localConfigPath = CreateSteamHome(original);

        var result = SteamLaunchOptionsWriter.SetLaunchOptions(SteamPath, "12345", "-brand-new-game", () => false);

        Assert.True(result.Success, result.Message);

        var expected = Render(WithInserted(AppsBraceOpenIndex + 1,
            (5, "\"12345\""),
            (5, "{"),
            (6, "\"LaunchOptions\"\t\t\"-brand-new-game\""),
            (5, "}")));

        Assert.Equal(expected, File.ReadAllText(localConfigPath));
    }

    // ---------- Régression : un appid identique trouvé plus profond dans l'arbre (pas enfant direct
    //            de "apps") ne doit pas être confondu avec un vrai bloc top-level -> un nouveau bloc
    //            top-level doit être créé, et le leurre imbriqué (sous 730/cloud/9999) rester intact. ----------

    [Fact]
    public void SetLaunchOptions_IgnoresAppIdLookalike_NestedDeeperInTree()
    {
        var original = Render(BaseLines);
        var localConfigPath = CreateSteamHome(original);

        // "9999" existe déjà, mais uniquement sous 730/cloud/9999 (pas comme enfant direct de "apps").
        var result = SteamLaunchOptionsWriter.SetLaunchOptions(SteamPath, "9999", "-top-level-9999", () => false);

        Assert.True(result.Success, result.Message);

        var expected = Render(WithInserted(AppsBraceOpenIndex + 1,
            (5, "\"9999\""),
            (5, "{"),
            (6, "\"LaunchOptions\"\t\t\"-top-level-9999\""),
            (5, "}")));

        var actual = File.ReadAllText(localConfigPath);
        Assert.Equal(expected, actual);

        // Le leurre imbriqué original doit être totalement inchangé.
        Assert.Contains("\"DECOY-A ne doit jamais etre touchee\"", actual);
    }

    // ---------- Régression : une clé "LaunchOptions" plus profonde dans l'arbre (pas enfant direct
    //            du bloc de l'appid) ne doit jamais être celle modifiée. ----------

    [Fact]
    public void SetLaunchOptions_IgnoresLaunchOptionsLookalike_NestedInSiblingSubBlock()
    {
        var original = Render(BaseLines);
        var localConfigPath = CreateSteamHome(original);

        var result = SteamLaunchOptionsWriter.SetLaunchOptions(SteamPath, "570", "-only-top-level", () => false);
        Assert.True(result.Success, result.Message);

        var actual = File.ReadAllText(localConfigPath);

        // La vraie LaunchOptions top-level de 570 a été ajoutée...
        Assert.Contains("\"LaunchOptions\"\t\t\"-only-top-level\"", actual);
        // ...et le leurre imbriqué dans "autocloud" est resté strictement inchangé.
        Assert.Contains("\"DECOY-B ne doit jamais etre touchee\"", actual);
    }

    // ---------- 4. Effacer une LaunchOptions existante ----------

    [Fact]
    public void ClearLaunchOptions_ErasesExistingValue()
    {
        var original = Render(BaseLines);
        var localConfigPath = CreateSteamHome(original);

        var result = SteamLaunchOptionsWriter.ClearLaunchOptions(SteamPath, "730", () => false);

        Assert.True(result.Success, result.Message);

        var expected = Render(WithReplaced(App730LaunchOptionsIndex, (6, "\"LaunchOptions\"\t\t\"\"")));
        Assert.Equal(expected, File.ReadAllText(localConfigPath));
    }

    // ---------- 4bis. Effacer quand la clé n'existait déjà pas sur un bloc d'appid existant : no-op ----------

    [Fact]
    public void ClearLaunchOptions_IsNoOp_WhenAppIdBlockExistsButHasNoLaunchOptions()
    {
        var original = Render(BaseLines);
        var localConfigPath = CreateSteamHome(original);

        // Le bloc "570" existe (autocloud, Installed) mais n'a pas de "LaunchOptions" à lui.
        var result = SteamLaunchOptionsWriter.ClearLaunchOptions(SteamPath, "570", () => false);

        Assert.True(result.Success, result.Message);
        Assert.Equal(original, File.ReadAllText(localConfigPath));
        Assert.Empty(BackupFiles(localConfigPath));
    }

    // ---------- 4ter. Effacer quand l'appid n'a aucun bloc du tout : no-op ----------

    [Fact]
    public void ClearLaunchOptions_IsNoOp_WhenAppIdHasNoBlockAtAll()
    {
        var original = Render(BaseLines);
        var localConfigPath = CreateSteamHome(original);

        var result = SteamLaunchOptionsWriter.ClearLaunchOptions(SteamPath, "77777", () => false);

        Assert.True(result.Success, result.Message);
        Assert.Equal(original, File.ReadAllText(localConfigPath));
        Assert.Empty(BackupFiles(localConfigPath));
    }

    // ---------- 5. Fichier réaliste multi-jeux : seul le bloc du bon appid change, le reste ressort identique ----------

    [Fact]
    public void SetLaunchOptions_OnlyTouchesTargetAppIdBlock_RestOfFileIsByteIdentical()
    {
        var original = Render(BaseLines);
        var localConfigPath = CreateSteamHome(original);

        var result = SteamLaunchOptionsWriter.SetLaunchOptions(SteamPath, "730", "-untouched-elsewhere-check", () => false);
        Assert.True(result.Success, result.Message);

        var actualLines = File.ReadAllText(localConfigPath).Split('\n');
        var originalLines = original.Split('\n');

        Assert.Equal(originalLines.Length, actualLines.Length);
        for (int i = 0; i < originalLines.Length; i++)
        {
            if (i == App730LaunchOptionsIndex)
            {
                Assert.NotEqual(originalLines[i], actualLines[i]);
                continue;
            }
            Assert.Equal(originalLines[i], actualLines[i]);
        }
    }

    // ---------- 6. Clé "apps" absente -> échec propre, aucune écriture ----------

    [Fact]
    public void SetLaunchOptions_FailsCleanly_WhenAppsKeyIsMissing()
    {
        var original = Render(NoAppsKeyLines);
        var localConfigPath = CreateSteamHome(original);

        var result = SteamLaunchOptionsWriter.SetLaunchOptions(SteamPath, "730", "-anything", () => false);

        Assert.False(result.Success);
        Assert.Contains("apps", result.Message);
        Assert.Equal(original, File.ReadAllText(localConfigPath));
        Assert.Empty(BackupFiles(localConfigPath));
        Assert.Empty(TempFiles(localConfigPath));
    }

    [Fact]
    public void ClearLaunchOptions_FailsCleanly_WhenAppsKeyIsMissing()
    {
        var original = Render(NoAppsKeyLines);
        var localConfigPath = CreateSteamHome(original);

        var result = SteamLaunchOptionsWriter.ClearLaunchOptions(SteamPath, "730", () => false);

        Assert.False(result.Success);
        Assert.Equal(original, File.ReadAllText(localConfigPath));
    }

    // ---------- Refus d'écrire pendant que Steam tourne (détecteur mockable, sans dépendre d'un vrai Steam) ----------

    [Fact]
    public void SetLaunchOptions_RefusesToWrite_WhenSteamIsRunning()
    {
        var original = Render(BaseLines);
        var localConfigPath = CreateSteamHome(original);

        var result = SteamLaunchOptionsWriter.SetLaunchOptions(SteamPath, "730", "-should-not-be-written", isSteamRunning: () => true);

        Assert.False(result.Success);
        Assert.Contains("Steam", result.Message);
        Assert.Equal(original, File.ReadAllText(localConfigPath));
        Assert.Empty(BackupFiles(localConfigPath));
    }

    [Fact]
    public void ClearLaunchOptions_RefusesToWrite_WhenSteamIsRunning()
    {
        var original = Render(BaseLines);
        var localConfigPath = CreateSteamHome(original);

        var result = SteamLaunchOptionsWriter.ClearLaunchOptions(SteamPath, "730", isSteamRunning: () => true);

        Assert.False(result.Success);
        Assert.Equal(original, File.ReadAllText(localConfigPath));
    }

    [Fact]
    public void IsSteamRunning_RealDetector_DoesNotThrow()
    {
        // Pas de mock ici : juste une vérification que l'appel réel à Process.GetProcessesByName
        // ne lève pas, quel que soit l'état de Steam sur la machine qui exécute les tests.
        var exception = Record.Exception(() => SteamLaunchOptionsWriter.IsSteamRunning());
        Assert.Null(exception);
    }

    // ---------- Fichier introuvable ----------

    [Fact]
    public void SetLaunchOptions_FailsCleanly_WhenUserDataDirectoryIsMissing()
    {
        Directory.CreateDirectory(_tempRoot); // steamPath existe, mais pas de sous-dossier userdata

        var result = SteamLaunchOptionsWriter.SetLaunchOptions(SteamPath, "730", "-anything", () => false);

        Assert.False(result.Success);
        Assert.Contains("localconfig.vdf", result.Message);
    }

    // ---------- Choix du bon compte quand plusieurs localconfig.vdf existent ----------

    [Fact]
    public void SetLaunchOptions_UsesMostRecentlyWrittenAccount_WhenMultipleExist()
    {
        var older = Render(BaseLines);
        var olderPath = CreateSteamHome(older, accountId: "111111111");

        Thread.Sleep(200); // s'assurer d'un LastWriteTime strictement postérieur
        var newer = Render(BaseLines);
        var newerPath = CreateSteamHome(newer, accountId: "222222222");

        var result = SteamLaunchOptionsWriter.SetLaunchOptions(SteamPath, "730", "-goes-to-newest-account", () => false);
        Assert.True(result.Success, result.Message);

        Assert.Equal(older, File.ReadAllText(olderPath)); // compte le plus ancien : totalement intact
        Assert.NotEqual(newer, File.ReadAllText(newerPath)); // compte le plus récent : modifié
        Assert.Contains("-goes-to-newest-account", File.ReadAllText(newerPath));
    }

    // ---------- Préservation de l'encodage (BOM UTF-8 conservé, caractères non-ASCII intacts) ----------

    [Fact]
    public void SetLaunchOptions_PreservesUtf8Bom_AndNonAsciiContentElsewhere()
    {
        var linesWithAccents = BaseLines.ToList();
        linesWithAccents.Insert(9, (4, "\"NomAffichage\"\t\t\"Écran étendu à côté\""));
        var original = Render(linesWithAccents);

        var localConfigPath = CreateSteamHome(original, withBom: true);

        var result = SteamLaunchOptionsWriter.SetLaunchOptions(SteamPath, "730", "-bom-preserved", () => false);
        Assert.True(result.Success, result.Message);

        var rawBytes = File.ReadAllBytes(localConfigPath);
        Assert.True(rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF,
            "Le BOM UTF-8 du fichier original aurait dû être conservé.");

        var textAfterBom = new UTF8Encoding(false).GetString(rawBytes, 3, rawBytes.Length - 3);
        Assert.Contains("\"NomAffichage\"\t\t\"Écran étendu à côté\"", textAfterBom);
        Assert.Contains("-bom-preserved", textAfterBom);
    }

    [Fact]
    public void SetLaunchOptions_DoesNotAddBom_WhenOriginalHadNone()
    {
        var original = Render(BaseLines);
        var localConfigPath = CreateSteamHome(original, withBom: false);

        var result = SteamLaunchOptionsWriter.SetLaunchOptions(SteamPath, "730", "-no-bom-expected", () => false);
        Assert.True(result.Success, result.Message);

        var rawBytes = File.ReadAllBytes(localConfigPath);
        var startsWithBom = rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF;
        Assert.False(startsWithBom, "Aucun BOM ne devait être ajouté : le fichier original n'en avait pas.");
    }

    // ---------- Pas de fichier temporaire résiduel après une écriture réussie ----------

    [Fact]
    public void SetLaunchOptions_LeavesNoTempFile_AfterSuccessfulWrite()
    {
        var localConfigPath = CreateSteamHome(Render(BaseLines));

        var result = SteamLaunchOptionsWriter.SetLaunchOptions(SteamPath, "730", "-cleanup-check", () => false);

        Assert.True(result.Success, result.Message);
        Assert.Empty(TempFiles(localConfigPath));
    }
}
