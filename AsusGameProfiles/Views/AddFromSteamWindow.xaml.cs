using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AsusGameProfiles.Models;
using AsusGameProfiles.Services;

namespace AsusGameProfiles.Views;

public partial class AddFromSteamWindow : Window
{
    private readonly ObservableCollection<SelectableSteamGame> _items;

    /// <summary>Rempli avec les jeux cochés une fois la fenêtre fermée avec succès.</summary>
    public List<SteamGameInfo> SelectedGames { get; private set; } = new();

    /// <summary>Non-null si l'utilisateur a choisi "+ Add manually" et sélectionné un exécutable.</summary>
    public string? ManualExePath { get; private set; }

    public AddFromSteamWindow(IEnumerable<SteamGameInfo> availableGames)
    {
        InitializeComponent();
        WindowChromeHelper.SyncTitleBarWithTheme(this);

        _items = new ObservableCollection<SelectableSteamGame>(
            availableGames.Select(g => new SelectableSteamGame { Game = g }));
        GamesListBox.ItemsSource = _items;
        EmptyStateText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Un clic n'importe ou sur la ligne (pas seulement sur la case) coche/decoche le jeu.</summary>
    private void OnGameRowClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: SelectableSteamGame item })
            item.IsSelected = !item.IsSelected;
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        SelectedGames = _items.Where(i => i.IsSelected).Select(i => i.Game).ToList();
        DialogResult = true;
        Close();
    }

    private void OnAddManuallyClick(object sender, RoutedEventArgs e)
    {
        var openDialog = new OpenFileDialog
        {
            Title = "Select the game's executable",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*"
        };
        if (openDialog.ShowDialog(this) != true) return;

        ManualExePath = openDialog.FileName;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
