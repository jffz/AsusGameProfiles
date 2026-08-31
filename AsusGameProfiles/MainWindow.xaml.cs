using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using AsusGameProfiles.Models;
using AsusGameProfiles.Services;
using AsusGameProfiles.Views;

namespace AsusGameProfiles;

public partial class MainWindow : Window
{
    private AppConfig _config = ConfigStore.Load();
    private GameProfile? _selected;
    private GameProfilePreset? _selectedPreset;
    private readonly string? _steamPath = SteamLibraryScanner.FindSteamPath();

    /// <summary>Option "aucun preset" affichee en tete de LaunchPresetCombo -- jamais ajoutee a _config.Presets.</summary>
    private static readonly GameProfilePreset NoLaunchPresetSentinel = new() { Id = "", Name = "No preset" };

    /// <summary>Option "utiliser le profil de sortie global" affichee en tete de ExitPresetCombo -- jamais ajoutee a _config.Presets.</summary>
    private static readonly GameProfilePreset DefaultExitPresetSentinel = new() { Id = "", Name = "Use default exit profile" };

    private readonly TrayIconService _trayIcon;
    private readonly ProcessWatcherService _processWatcher;
    private bool _reallyClosing;

    /// <summary>
    /// Entree epinglee en haut de ProfilesListBox representant le "Default exit profile" -- affichee
    /// et selectionnable exactement comme un vrai GameProfile (meme ItemTemplate, meme zone d'edition
    /// a droite), mais jamais ajoutee a _config.Profiles ni sauvegardee : c'est juste une facade
    /// d'affichage sur AppConfig.DefaultExitMode/DefaultExitFrameRateBoost/DefaultExitExtraProperties.
    /// </summary>
    private static readonly GameProfile DefaultProfileSentinel = new()
    {
        Id = "__default__",
        DisplayName = "Default exit profile",
        ExePath = ""
    };

    /// <summary>
    /// Ligne d'affichage de ProfilesListBox : le GameProfile (ou DefaultProfileSentinel) plus le nom/
    /// couleur du preset assigne, resolus une fois par RefreshProfilesList() puisque GameProfile
    /// lui-meme ne connait que son PresetId, pas le preset entier -- evite de re-chercher dans
    /// _config.Presets a chaque rendu de ligne.
    /// </summary>
    private sealed class GameListRow
    {
        public required GameProfile Profile { get; init; }
        /// <summary>Nom du preset assigne, "No preset" si aucun, ou null pour la ligne "Default exit profile" (pas de sous-titre du tout).</summary>
        public string? SubtitleText { get; init; }
    }

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "AsusGameProfiles";

    /// <summary>Chemin d'installation MSI standard (voir AsusGameProfiles.Setup/Package.wxs, INSTALLFOLDER = ProgramFiles64Folder\AsusGameProfiles).</summary>
    private static readonly string CanonicalInstallPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AsusGameProfiles", "AsusGameProfiles.exe");

    /// <summary>
    /// Chemin vers cet exe a utiliser partout ou on doit figer "ce logiciel" quelque part qui survit
    /// bien au-dela de ce process (options de lancement Steam, demarrage avec Windows) : la copie
    /// installee si elle existe, sinon le process courant. Sans ca, lancer une copie de developpement
    /// (ex: bin\Debug\...) puis sauvegarder un profil grave le chemin ephemere de cette copie dans
    /// Steam/le registre -- ca casse des que ce dossier de build est reconstruit/supprime, meme si
    /// une vraie installation existe par ailleurs sur la machine (bug reel rencontre : un profil Steam
    /// pointait vers bin\Debug\net8.0-windows\win-x64\AsusGameProfiles.exe apres une sauvegarde faite
    /// depuis une build de dev, alors qu'une installation MSI existait deja dans Program Files).
    /// </summary>
    private static string ResolveLauncherExePath()
    {
        var currentPath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
        if (string.Equals(currentPath, CanonicalInstallPath, StringComparison.OrdinalIgnoreCase))
            return currentPath;

        return File.Exists(CanonicalInstallPath) ? CanonicalInstallPath : currentPath;
    }

    // Geometrie des glyphes de statut (memes courbes que la coche de la CheckBox pour rester coherent).
    private const string CheckGlyph = "M2,7 L6,11 L14,2";
    private const string CrossGlyph = "M2,2 L14,14 M14,2 L2,14";

    // Geometrie des glyphes de la barre de titre personnalisee (bouton agrandir/restaurer, echelle 10x10).
    private const string MaximizeGlyphData = "M0.5,0.5 H9.5 V9.5 H0.5 Z";
    private const string RestoreGlyphData = "M3,1 H9 V7 H7 M1,3 H7 V9 H1 Z";

    public MainWindow()
    {
        InitializeComponent();
        WindowChromeHelper.SyncTitleBarWithTheme(this);
        WindowChromeHelper.FixMaximizedBounds(this);

        PresetModeCombo.ItemsSource = GameVisualModeCatalog.All;
        DefaultExitModeCombo.ItemsSource = GameVisualModeCatalog.All;

        RefreshDwcStatus();
        CleanupLegacySteamLaunchOptions();

        LoadDefaultExitProfileUi();
        RefreshPresetsList();
        RefreshProfilesList();

        StartAtBootCheck.IsChecked = GetStartAtBoot();
        CloseToTrayCheck.IsChecked = _config.CloseToTray;
        ShowNotificationsCheck.IsChecked = _config.ShowProfileNotifications;

        _trayIcon = new TrayIconService(this);
        _trayIcon.ExitRequested += (_, _) => { _reallyClosing = true; Close(); };
        Closing += OnWindowClosing;

        StateChanged += (_, _) => UpdateMaximizeGlyph();
        UpdateMaximizeGlyph();

        _processWatcher = new ProcessWatcherService(() => _config);
        _processWatcher.ProfileTriggered += OnProfileTriggeredByWatcher;
        _processWatcher.Start();
    }

    /// <summary>
    /// Appelee par App.xaml.cs (via le Dispatcher) quand une deuxieme instance de l'app vient d'etre
    /// lancee et de se fermer aussitot -- voir le mutex nomme dans App.OnStartup. Ramene cette fenetre
    /// au premier plan au lieu de laisser une deuxieme instance s'ouvrir en parallele.
    /// </summary>
    public void ActivateFromAnotherInstance() => _trayIcon.Restore();

    // ---------- Custom title bar (voir MainWindow.xaml, WindowChrome) ----------

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseButtonClick(object sender, RoutedEventArgs e) => Close();

    private void UpdateMaximizeGlyph()
    {
        MaximizeGlyph.Data = Geometry.Parse(WindowState == WindowState.Maximized ? RestoreGlyphData : MaximizeGlyphData);
        var label = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
        MaximizeButton.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, label);
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_reallyClosing || CloseToTrayCheck.IsChecked != true)
        {
            _trayIcon.Dispose();
            return;
        }

        e.Cancel = true;
        Hide();
        _trayIcon.Show();
    }

    private void OnAppSettingChanged(object sender, RoutedEventArgs e)
    {
        _config.CloseToTray = CloseToTrayCheck.IsChecked == true;
        _config.ShowProfileNotifications = ShowNotificationsCheck.IsChecked == true;
        ConfigStore.Save(_config);

        SetStartAtBoot(StartAtBootCheck.IsChecked == true);
    }

    private static bool GetStartAtBoot()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    private static void SetStartAtBoot(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (enabled)
            {
                key.SetValue(RunValueName, $"\"{ResolveLauncherExePath()}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Meilleur effort : cle de registre inaccessible (politique de groupe, etc.) -- la case a
            // cocher peut alors ne plus refleter l'etat reel, mais l'app ne doit jamais planter pour ca.
        }
    }

    // ---------- Profiles list ----------

    private void RefreshProfilesList()
    {
        var previouslySelected = _selected?.Id;

        var rows = new List<GameListRow> { new() { Profile = DefaultProfileSentinel } };
        foreach (var profile in _config.Profiles)
        {
            var launchPreset = _config.Presets.FirstOrDefault(p => p.Id == profile.OnLaunchPresetId);
            var exitPreset = _config.Presets.FirstOrDefault(p => p.Id == profile.OnExitPresetId);
            var subtitle = launchPreset?.Name ?? "No preset";
            if (exitPreset != null) subtitle += $" · {exitPreset.Name} on exit";

            rows.Add(new GameListRow
            {
                Profile = profile,
                SubtitleText = subtitle
            });
        }

        ProfilesListBox.ItemsSource = null;
        ProfilesListBox.ItemsSource = rows;

        var match = previouslySelected != null ? rows.FirstOrDefault(r => r.Profile.Id == previouslySelected) : null;
        if (match != null) ProfilesListBox.SelectedItem = match;
    }

    private void OnProfileSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selection = (ProfilesListBox.SelectedItem as GameListRow)?.Profile;
        if (selection != null) PresetsListBox.SelectedItem = null;

        if (ReferenceEquals(selection, DefaultProfileSentinel))
        {
            _selected = null;
            RemoveButton.IsEnabled = false;
            EditorPanel.Visibility = Visibility.Collapsed;
            PresetEditorPanel.Visibility = Visibility.Collapsed;
            EmptyStateText.Visibility = Visibility.Collapsed;
            DefaultEditorPanel.Visibility = Visibility.Visible;
            LoadDefaultExitProfileUi();
            return;
        }

        _selected = selection;
        RemoveButton.IsEnabled = _selected != null;
        DefaultEditorPanel.Visibility = Visibility.Collapsed;
        PresetEditorPanel.Visibility = Visibility.Collapsed;

        if (_selected is null)
        {
            EditorPanel.Visibility = Visibility.Collapsed;
            EmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        EditorPanel.Visibility = Visibility.Visible;
        EmptyStateText.Visibility = Visibility.Collapsed;

        ProfileTitleText.Text = _selected.DisplayName;
        ProfileTitleIcon.Source = IconCache.GetIcon(_selected.ExePath);

        LaunchPresetCombo.SelectionChanged -= OnGamePresetChanged;
        LaunchPresetCombo.ItemsSource = new[] { NoLaunchPresetSentinel }.Concat(_config.Presets).ToList();
        LaunchPresetCombo.SelectedItem = _config.Presets.FirstOrDefault(p => p.Id == _selected.OnLaunchPresetId) ?? NoLaunchPresetSentinel;
        LaunchPresetCombo.SelectionChanged += OnGamePresetChanged;

        ExitPresetCombo.SelectionChanged -= OnGamePresetChanged;
        ExitPresetCombo.ItemsSource = new[] { DefaultExitPresetSentinel }.Concat(_config.Presets).ToList();
        ExitPresetCombo.SelectedItem = _config.Presets.FirstOrDefault(p => p.Id == _selected.OnExitPresetId) ?? DefaultExitPresetSentinel;
        ExitPresetCombo.SelectionChanged += OnGamePresetChanged;

        UpdateEditPresetButtonsEnabled();

        LaunchNowButton.Visibility = _selected.IsSteamGame ? Visibility.Collapsed : Visibility.Visible;

        UpdateWatchStatusText();
    }

    private void OnGamePresetChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateEditPresetButtonsEnabled();

    private void UpdateEditPresetButtonsEnabled()
    {
        EditLaunchPresetButton.IsEnabled = LaunchPresetCombo.SelectedItem is GameProfilePreset { Id.Length: > 0 };
        EditExitPresetButton.IsEnabled = ExitPresetCombo.SelectedItem is GameProfilePreset { Id.Length: > 0 };
    }

    private void OnEditLaunchPresetFromGameClick(object sender, RoutedEventArgs e) => EditPresetFromCombo(LaunchPresetCombo);
    private void OnEditExitPresetFromGameClick(object sender, RoutedEventArgs e) => EditPresetFromCombo(ExitPresetCombo);

    private void EditPresetFromCombo(ComboBox combo)
    {
        if (combo.SelectedItem is not GameProfilePreset { Id.Length: > 0 } preset) return;
        PresetsListBox.SelectedItem = _config.Presets.FirstOrDefault(p => p.Id == preset.Id);
    }

    // ---------- Presets list ----------

    private void RefreshPresetsList()
    {
        var previouslySelected = _selectedPreset?.Id;
        PresetsListBox.ItemsSource = null;
        PresetsListBox.ItemsSource = _config.Presets;

        if (previouslySelected != null)
        {
            var match = _config.Presets.FirstOrDefault(p => p.Id == previouslySelected);
            if (match != null) PresetsListBox.SelectedItem = match;
        }
    }

    private void OnPresetSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selectedPreset = PresetsListBox.SelectedItem as GameProfilePreset;
        RemovePresetButton.IsEnabled = _selectedPreset != null;

        if (_selectedPreset is null)
        {
            PresetEditorPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ProfilesListBox.SelectedItem = null;
        EditorPanel.Visibility = Visibility.Collapsed;
        DefaultEditorPanel.Visibility = Visibility.Collapsed;
        EmptyStateText.Visibility = Visibility.Collapsed;
        PresetEditorPanel.Visibility = Visibility.Visible;

        PresetNameBox.Text = _selectedPreset.Name;
        PresetModeCombo.SelectedItem = GameVisualModeCatalog.All.First(i => i.Value == _selectedPreset.Mode);
        PresetBoostCheck.IsChecked = _selectedPreset.FrameRateBoost;

        PresetPropertiesPanel.Children.Clear();
        foreach (var p in _selectedPreset.ExtraProperties)
            AddPropertyRow(PresetPropertiesPanel, p.Property, p.Value);

        var usedByLaunch = _config.Profiles.Count(p => p.OnLaunchPresetId == _selectedPreset.Id);
        var usedByExit = _config.Profiles.Count(p => p.OnExitPresetId == _selectedPreset.Id);
        PresetUsageText.Text = BuildPresetUsageText(usedByLaunch, usedByExit);
    }

    private static string BuildPresetUsageText(int usedByLaunch, int usedByExit)
    {
        if (usedByLaunch == 0 && usedByExit == 0) return "Not used by any game yet.";

        var parts = new List<string>();
        if (usedByLaunch > 0) parts.Add($"{usedByLaunch} on launch");
        if (usedByExit > 0) parts.Add($"{usedByExit} on exit");
        return $"Used by {string.Join(", ", parts)}.";
    }

    private void OnAddPresetClick(object sender, RoutedEventArgs e)
    {
        var preset = new GameProfilePreset { Name = "New preset" };
        _config.Presets.Add(preset);
        ConfigStore.Save(_config);
        RefreshPresetsList();
        PresetsListBox.SelectedItem = preset;
    }

    private void OnRemovePresetClick(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset is null) return;

        if (!ConfirmDialog.Show(this, $"Delete the preset \"{_selectedPreset.Name}\"? Games using it on launch or exit will fall back to no preset / the default exit profile.", "Delete"))
            return;

        foreach (var profile in _config.Profiles.Where(p => p.OnLaunchPresetId == _selectedPreset.Id))
            profile.OnLaunchPresetId = "";
        foreach (var profile in _config.Profiles.Where(p => p.OnExitPresetId == _selectedPreset.Id))
            profile.OnExitPresetId = "";

        _config.Presets.Remove(_selectedPreset);
        _selectedPreset = null;
        ConfigStore.Save(_config);
        RefreshPresetsList();
        RefreshProfilesList();
        PresetEditorPanel.Visibility = Visibility.Collapsed;
        RemovePresetButton.IsEnabled = false;
    }

    private void OnSavePresetClick(object sender, RoutedEventArgs e)
    {
        if (_selectedPreset is null) return;

        _selectedPreset.Name = string.IsNullOrWhiteSpace(PresetNameBox.Text) ? "Unnamed preset" : PresetNameBox.Text.Trim();
        _selectedPreset.Mode = ((GameVisualModeItem)PresetModeCombo.SelectedItem).Value;
        _selectedPreset.FrameRateBoost = PresetBoostCheck.IsChecked == true;
        _selectedPreset.ExtraProperties = ReadPropertyRows(PresetPropertiesPanel);

        ConfigStore.Save(_config);
        RefreshPresetsList();
        RefreshProfilesList();
    }

    /// <summary>Met a jour le texte explicatif "comment ce profil s'applique" pour le jeu selectionne -- live, avant meme d'enregistrer.</summary>
    private void UpdateWatchStatusText()
    {
        if (_selected is null) return;

        // Pas de preset "on launch" choisi : rien ne changera au lancement, et rien d'autre dans
        // l'UI ne le signale (releve par l'audit UX -- une sauvegarde silencieuse ici donnait
        // l'impression que le jeu etait configure alors qu'il ne se passe rien).
        if (LaunchPresetCombo.SelectedItem is GameProfilePreset { Id.Length: 0 })
        {
            WatchStatusText.Foreground = DangerBrush;
            WatchStatusText.Text = "No preset is assigned \"On launch\" -- nothing will change when this game starts. Pick a preset above if that's not intentional.";
            return;
        }

        WatchStatusText.Foreground = MutedBrush;
        var processName = Path.GetFileName(_selected.ExePath);
        WatchStatusText.Text = string.IsNullOrEmpty(processName)
            ? "Applied automatically while AsusGameProfiles is running and this game's process is detected. Set an executable path so it can be detected."
            : $"Applied automatically whenever \"{processName}\" is detected running -- keep AsusGameProfiles running (Start with Windows + Close to tray) for this to work.";
    }

    // ---------- Add / remove ----------

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var candidates = new List<SteamGameInfo>();
        if (_steamPath != null)
        {
            var installed = SteamLibraryScanner.ScanInstalledGames(_steamPath);
            var alreadyAdded = _config.Profiles.Select(p => p.Id).ToHashSet();
            candidates = installed.Where(g => !alreadyAdded.Contains(g.AppId)).ToList();
        }

        var dialog = new AddFromSteamWindow(candidates) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        if (dialog.SelectedGames.Count > 0)
        {
            var added = new List<GameProfile>();
            foreach (var game in dialog.SelectedGames)
            {
                // Meilleur effort : l'exe trouve ici ne sert qu'a afficher une icone pour un jeu Steam,
                // le lancement reel passe toujours par %command% resolu par Steam lui-meme.
                var exePath = GameExecutableFinder.TryFindMainExecutable(game.InstallPath, game.Name) ?? "";

                var profile = new GameProfile
                {
                    Id = game.AppId,
                    DisplayName = game.Name,
                    ExePath = exePath,
                    IsSteamGame = true
                };
                _config.Profiles.Add(profile);
                added.Add(profile);
            }

            ConfigStore.Save(_config);
            RefreshProfilesList();
        }

        if (dialog.ManualExePath != null)
        {
            AddManualProfile(dialog.ManualExePath);
        }
    }

    private void AddManualProfile(string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath);
        var id = "manual:" + Guid.NewGuid().ToString("N")[..8];

        _config.Profiles.Add(new GameProfile
        {
            Id = id,
            DisplayName = name,
            ExePath = exePath,
            IsSteamGame = false
        });

        ConfigStore.Save(_config);
        RefreshProfilesList();
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        if (!ConfirmDialog.Show(this, $"Remove the profile \"{_selected.DisplayName}\"?", "Remove"))
            return;

        // Meilleur effort : si ce jeu avait encore une ancienne option de lancement Steam (version
        // anterieure a la suppression de ce mecanisme), on la retire -- inoffensif si Steam tourne
        // (l'ecriture echoue silencieusement, l'entree orpheline restera juste en place).
        if (_selected.IsSteamGame && _steamPath != null)
            SteamLaunchOptionsWriter.ClearLaunchOptions(_steamPath, _selected.Id);

        _config.Profiles.Remove(_selected);
        _selected = null;
        ConfigStore.Save(_config);
        RefreshProfilesList();
        EditorPanel.Visibility = Visibility.Collapsed;
        EmptyStateText.Visibility = Visibility.Visible;
        RemoveButton.IsEnabled = false;
    }

    // ---------- Edit / save ----------

    private void OnSaveProfileClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        _selected.OnLaunchPresetId = (LaunchPresetCombo.SelectedItem as GameProfilePreset)?.Id ?? "";
        _selected.OnExitPresetId = (ExitPresetCombo.SelectedItem as GameProfilePreset)?.Id ?? "";

        ConfigStore.Save(_config);
        RefreshProfilesList();

        // Meilleur effort : si ce jeu avait encore une ancienne option de lancement Steam (version
        // anterieure a la suppression de ce mecanisme), on la retire.
        if (_selected.IsSteamGame && _steamPath != null)
            SteamLaunchOptionsWriter.ClearLaunchOptions(_steamPath, _selected.Id);

        UpdateWatchStatusText();
    }

    private static Brush MutedBrush => (Brush)Application.Current.Resources["TextSecondaryBrush"];
    private static Brush SuccessBrush => (Brush)Application.Current.Resources["SuccessBrush"];
    private static Brush DangerBrush => (Brush)Application.Current.Resources["DangerBrush"];

    // ---------- Default exit profile ----------

    private void LoadDefaultExitProfileUi()
    {
        DefaultExitModeCombo.SelectedItem = GameVisualModeCatalog.All.First(i => i.Value == _config.DefaultExitMode);
        DefaultExitBoostCheck.IsChecked = _config.DefaultExitFrameRateBoost;

        DefaultExitPropertiesPanel.Children.Clear();
        foreach (var p in _config.DefaultExitExtraProperties)
            AddPropertyRow(DefaultExitPropertiesPanel, p.Property, p.Value);
    }

    private void OnSaveDefaultExitClick(object sender, RoutedEventArgs e)
    {
        _config.DefaultExitMode = ((GameVisualModeItem)DefaultExitModeCombo.SelectedItem).Value;
        _config.DefaultExitFrameRateBoost = DefaultExitBoostCheck.IsChecked == true;
        _config.DefaultExitExtraProperties = ReadPropertyRows(DefaultExitPropertiesPanel);
        ConfigStore.Save(_config);
    }

    private void OnAddDefaultExitPropertyClick(object sender, RoutedEventArgs e) => AddPropertyRow(DefaultExitPropertiesPanel);
    private void OnAddPresetPropertyClick(object sender, RoutedEventArgs e) => AddPropertyRow(PresetPropertiesPanel);

    /// <summary>
    /// Ajoute une ligne editable (property ComboBox + editeur de valeur adapte + bouton retirer) dans
    /// le conteneur donne. L'editeur de valeur change automatiquement de forme (case a cocher / slider
    /// 0-100 / champ texte) selon le type de la propriete choisie, d'apres <see cref="DwcPropertyCatalog"/>.
    /// </summary>
    private void AddPropertyRow(StackPanel container, string property = "", string value = "")
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var propertyCombo = new ComboBox
        {
            IsEditable = true,
            Text = property,
            FontSize = 12,
            ToolTip = DwcPropertyCatalog.Find(property)?.Description
        };
        Grid.SetColumn(propertyCombo, 0);

        var valueHost = new Grid();
        Grid.SetColumn(valueHost, 2);
        valueHost.Children.Add(BuildValueEditor(DwcPropertyCatalog.Find(property), value));

        var removeButton = new Button { Content = "×", Style = (Style)FindResource("RowRemoveButton") };
        Grid.SetColumn(removeButton, 4);
        removeButton.Click += (_, _) =>
        {
            container.Children.Remove(row);
            RefreshPropertyPickerLists(container);
        };

        var lastKnownProperty = property;
        void OnPropertyChanged()
        {
            var current = propertyCombo.Text?.Trim() ?? "";
            if (string.Equals(current, lastKnownProperty, StringComparison.OrdinalIgnoreCase))
            {
                RefreshPropertyPickerLists(container);
                return;
            }
            lastKnownProperty = current;

            var info = DwcPropertyCatalog.Find(current);
            propertyCombo.ToolTip = info?.Description;
            valueHost.Children.Clear();
            valueHost.Children.Add(BuildValueEditor(info, ""));
            RefreshPropertyPickerLists(container);
        }

        // TextChanged (pas seulement SelectionChanged/LostFocus) pour que l'editeur de valeur se mette
        // a jour immediatement des que le nom tape correspond a une propriete connue, sans avoir a
        // quitter le champ -- l'evenement du TextBox interne remonte (bubbling) jusqu'au ComboBox.
        propertyCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => OnPropertyChanged()));
        propertyCombo.SelectionChanged += (_, _) => OnPropertyChanged();

        row.Children.Add(propertyCombo);
        row.Children.Add(valueHost);
        row.Children.Add(removeButton);
        container.Children.Add(row);
        RefreshPropertyPickerLists(container);
    }

    /// <summary>
    /// Exclut de la liste deroulante de chaque ligne les proprietes deja choisies par une AUTRE ligne
    /// du meme conteneur (Launch / Exit / Default exit) : impossible d'ajouter deux fois la meme
    /// propriete plutot que de le signaler apres coup.
    /// </summary>
    private static void RefreshPropertyPickerLists(StackPanel container)
    {
        var combos = container.Children.OfType<Grid>()
            .Select(r => (ComboBox)r.Children[0])
            .ToList();

        // Trie alphabetique pour la liste affichee a l'utilisateur -- KnownProperties lui-meme reste
        // groupe par categorie (Image & Color, Display Modes...) pour rester lisible dans le code.
        var allNames = DwcPropertyCatalog.KnownProperties.Select(p => p.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var combo in combos)
        {
            var usedByOthers = combos
                .Where(c => !ReferenceEquals(c, combo))
                .Select(c => c.Text?.Trim() ?? "")
                .Where(t => t.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            combo.ItemsSource = allNames.Where(n => !usedByOthers.Contains(n)).ToArray();
        }
    }

    private static FrameworkElement BuildValueEditor(DwcPropertyInfo? info, string value)
    {
        switch (info?.Kind ?? DwcPropertyInputKind.FreeText)
        {
            case DwcPropertyInputKind.Boolean:
                return new CheckBox
                {
                    Content = "ON",
                    IsChecked = value == "1",
                    VerticalAlignment = VerticalAlignment.Center
                };

            case DwcPropertyInputKind.Enum:
            {
                var options = info!.Options ?? Array.Empty<DwcEnumOption>();
                var combo = new ComboBox { FontSize = 12, ItemsSource = options, HorizontalAlignment = HorizontalAlignment.Stretch };
                combo.SelectedItem = options.FirstOrDefault(o => o.Value == value) ?? options.FirstOrDefault();
                return combo;
            }

            case DwcPropertyInputKind.Range0To100:
            {
                var initial = double.TryParse(value, out var v) ? Math.Clamp(v, 0, 100) : 50;
                var readout = new TextBlock
                {
                    Text = ((int)initial).ToString(),
                    Width = 26,
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12
                };
                DockPanel.SetDock(readout, Dock.Right);

                var slider = new Slider
                {
                    Minimum = 0,
                    Maximum = 100,
                    TickFrequency = 1,
                    IsSnapToTickEnabled = true,
                    Value = initial,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                slider.ValueChanged += (_, e) => readout.Text = ((int)e.NewValue).ToString();

                var panel = new DockPanel();
                panel.Children.Add(readout);
                panel.Children.Add(slider);
                return panel;
            }

            default:
                return new TextBox { Text = value, FontSize = 12 };
        }
    }

    /// <summary>
    /// Relit les lignes property/value construites par <see cref="AddPropertyRow"/>, en ignorant les
    /// lignes sans nom de propriété et en ne gardant que la première occurrence d'une propriété en
    /// doublon (voir <see cref="RevalidatePropertyRows"/>, qui signale ces doublons visuellement).
    /// </summary>
    private static List<DwcPropertyValue> ReadPropertyRows(StackPanel container)
    {
        var result = new List<DwcPropertyValue>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in container.Children)
        {
            if (child is not Grid row) continue;
            var propertyCombo = (ComboBox)row.Children[0];
            var valueHost = (Grid)row.Children[1];

            var property = propertyCombo.Text?.Trim();
            if (string.IsNullOrEmpty(property)) continue;
            if (!seen.Add(property)) continue;

            var value = ReadValueFromHost(valueHost);
            if (!string.IsNullOrEmpty(value))
                result.Add(new DwcPropertyValue(property, value));
        }
        return result;
    }

    private static string? ReadValueFromHost(Grid valueHost)
    {
        if (valueHost.Children.Count == 0) return null;
        return valueHost.Children[0] switch
        {
            CheckBox cb => cb.IsChecked == true ? "1" : "0",
            DockPanel dp when dp.Children[1] is Slider slider => ((int)slider.Value).ToString(),
            ComboBox combo => (combo.SelectedItem as DwcEnumOption)?.Value,
            TextBox tb => tb.Text?.Trim(),
            _ => null
        };
    }

    // ---------- Direct launch (non-Steam games) ----------

    private async void OnLaunchNowClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.IsSteamGame) return;

        LaunchNowButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        SetWatchStatus(true, "Game running -- display settings will be restored when it closes...");

        var profile = _selected;

        int exitCode = await System.Threading.Tasks.Task.Run(() =>
        {
            var args = new[] { "--launch", profile.Id, profile.ExePath };
            return GameLauncher.RunLaunchMode(args);
        });

        SetWatchStatus(true, $"Done (exit code {exitCode}). Display settings restored.");
        LaunchNowButton.IsEnabled = true;
        SaveButton.IsEnabled = true;
    }

    private void SetWatchStatus(bool success, string message)
    {
        WatchStatusText.Foreground = success ? SuccessBrush : DangerBrush;
        WatchStatusText.Text = message;
    }

    // ---------- dwc.exe / Steam / Display status ----------

    private void RefreshDwcStatus()
    {
        bool detected = DwcService.TryAutoDetect(_config.DwcExePath, out var resolvedPath);

        if (detected && !string.Equals(resolvedPath, _config.DwcExePath, StringComparison.Ordinal))
        {
            _config.DwcExePath = resolvedPath;
            ConfigStore.Save(_config);
        }

        if (detected)
        {
            DwcStatusIcon.Background = SuccessBrush;
            DwcStatusGlyph.Data = Geometry.Parse(CheckGlyph);
            DwcStatusText.Foreground = SuccessBrush;
            DwcStatusText.Text = "dwc.exe detected";
            DwcInstallButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            DwcStatusIcon.Background = DangerBrush;
            DwcStatusGlyph.Data = Geometry.Parse(CrossGlyph);
            DwcStatusText.Foreground = DangerBrush;
            DwcStatusText.Text = "dwc.exe not found.";
            DwcInstallButton.Visibility = Visibility.Visible;
        }

        RefreshDisplayInfo();
    }

    /// <summary>Telecharge dwc.exe depuis le depot GitHub officiel ASUS-Display (voir <see cref="DwcInstaller"/>) et l'installe dans le dossier de donnees de l'app.</summary>
    private async void OnInstallDwcClick(object sender, RoutedEventArgs e)
    {
        DwcInstallButton.IsEnabled = false;
        DwcBrowseButton.IsEnabled = false;
        DwcStatusText.Text = "Downloading dwc.exe from ASUS's official repository…";

        var destDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AsusGameProfiles", "dwc");
        var result = await DwcInstaller.InstallAsync(destDir);

        if (result.Success && result.ResolvedPath != null)
        {
            _config.DwcExePath = result.ResolvedPath;
            ConfigStore.Save(_config);
            RefreshDwcStatus();
        }
        else
        {
            // On laisse le message d'erreur affiche : RefreshDwcStatus() l'ecraserait avec le texte
            // generique "dwc.exe not found" puisque la detection echoue toujours a ce stade.
            DwcStatusText.Text = result.Message;
            DwcInstallButton.IsEnabled = true;
        }

        DwcBrowseButton.IsEnabled = true;
    }

    /// <summary>
    /// Seul retour visible pour l'utilisateur quand le watcher bascule un profil en arriere-plan --
    /// sans ca, tout le mecanisme central de l'app est silencieux (releve par l'audit UX). Resout le
    /// nom du preset applique pour un message concret plutot qu'un simple "quelque chose a change".
    /// </summary>
    private void OnProfileTriggeredByWatcher(GameProfile profile, string action)
    {
        RefreshDisplayInfo();

        if (!_config.ShowProfileNotifications) return;

        var presetId = action == "launch" ? profile.OnLaunchPresetId : profile.OnExitPresetId;
        var preset = _config.Presets.FirstOrDefault(p => p.Id == presetId);
        var text = action == "launch"
            ? (preset != null ? $"Applied \"{preset.Name}\" for {profile.DisplayName}." : $"{profile.DisplayName} launched, but no preset is assigned.")
            : $"{profile.DisplayName} closed -- restored \"{preset?.Name ?? "the default exit profile"}\".";

        _trayIcon.ShowNotification("AsusGameProfiles", text);
    }

    private void RefreshDisplayInfo()
    {
        var monitors = DwcService.GetDetectedMonitors(_config.DwcExePath);
        DisplayCard.Visibility = Visibility.Visible;

        if (monitors.Count == 0)
        {
            // Auparavant la carte disparaissait silencieusement (releve par l'audit UX) -- un
            // cable debranche ou un moniteur non supporte donnait l'impression que rien ne
            // s'affichait plutot que d'expliquer le probleme.
            DisplayModelText.Text = "No monitor detected";
            DisplayTechInfoText.Text = "Check that dwc.exe can reach your display, and that it's a monitor dwc.exe supports.";
            return;
        }
        var primary = monitors[0];
        DisplayModelText.Text = monitors.Count == 1
            ? primary.Model
            : string.Join(" + ", monitors.Select(m => m.Model));

        var mode = DisplayInfoService.GetCurrentMode(primary.DeviceId);
        var currentGameVisual = GameVisualModeExtensions.FromDwcValue(DwcService.GetPropertyValue(_config.DwcExePath, "GameVisual"));
        var brightness = DwcService.GetPropertyValue(_config.DwcExePath, "Brightness");
        var inputSource = DwcService.GetPropertyValue(_config.DwcExePath, "InputSource");
        var colorTemp = DwcService.GetPropertyValue(_config.DwcExePath, "ColorTemp");
        var usageHours = DwcService.GetPropertyValue(_config.DwcExePath, "UsageTime");

        var infoParts = new List<string>();
        if (mode != null) infoParts.Add($"{mode.Width} × {mode.Height} @ {mode.RefreshHz} Hz");
        if (currentGameVisual != null) infoParts.Add($"GameVisual: {currentGameVisual.Value.ToDisplayName()}");
        if (brightness != null) infoParts.Add($"Brightness: {brightness}");
        if (inputSource != null) infoParts.Add($"Input: {ResolveEnumLabel("InputSource", inputSource)}");
        if (colorTemp != null) infoParts.Add($"Color temp: {ResolveEnumLabel("ColorTemp", colorTemp)}");
        if (usageHours != null) infoParts.Add($"{usageHours}h on");
        DisplayTechInfoText.Text = infoParts.Count > 0
            ? string.Join("\n", infoParts)
            : "Technical details unavailable.";
    }

    /// <summary>Traduit la valeur brute renvoyee par dwc.exe get (ex: "15") en son libelle convivial (ex: "DP-1") via le catalogue -- garde la valeur brute si elle n'y figure pas (propriete non couverte ou moniteur different).</summary>
    private static string ResolveEnumLabel(string propertyName, string rawValue)
    {
        var match = DwcPropertyCatalog.Find(propertyName)?.Options?.FirstOrDefault(o => o.Value == rawValue);
        return match?.Label ?? rawValue;
    }

    /// <summary>
    /// Nettoyage ponctuel (2026-08-27, suppression du mode "Steam launch options") : les jeux Steam
    /// configures avant ce changement peuvent encore avoir une option de lancement Steam ecrite par une
    /// version anterieure de l'app (wrapper --launch). Best-effort silencieux, appele une fois au
    /// demarrage -- <see cref="SteamLaunchOptionsWriter.ClearLaunchOptions"/> est un no-op rapide (pas
    /// d'ecriture ni de sauvegarde) quand il n'y a deja rien a effacer, donc l'appeler a chaque
    /// demarrage est peu couteux et ne necessite pas de mecanisme de retente dedie : si Steam tournait
    /// au moment de cet appel, ca sera simplement retente au prochain demarrage de l'app.
    /// </summary>
    private void CleanupLegacySteamLaunchOptions()
    {
        if (_steamPath is null) return;

        foreach (var profile in _config.Profiles.Where(p => p.IsSteamGame))
            SteamLaunchOptionsWriter.ClearLaunchOptions(_steamPath, profile.Id);
    }

    private void OnBrowseDwcClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Locate dwc.exe",
            Filter = "dwc.exe|dwc.exe|Executables (*.exe)|*.exe"
        };

        // Si dwc.exe est deja detecte via un chemin complet (pas juste "dwc.exe" trouve sur le PATH,
        // qui n'a pas de dossier a proposer), on ouvre la boite de dialogue directement a cet endroit
        // plutot que sur l'emplacement par defaut de Windows.
        var currentDir = Path.GetDirectoryName(_config.DwcExePath);
        if (!string.IsNullOrEmpty(currentDir) && Directory.Exists(currentDir))
            dialog.InitialDirectory = currentDir;

        if (dialog.ShowDialog(this) != true) return;

        _config.DwcExePath = dialog.FileName;
        ConfigStore.Save(_config);
        RefreshDwcStatus();
    }
}
