using System.ComponentModel;
using System.Runtime.CompilerServices;
using AsusGameProfiles.Services;

namespace AsusGameProfiles.Models;

/// <summary>Wrapper utilisé uniquement par la fenêtre "Ajouter depuis Steam" pour la sélection multiple.</summary>
public class SelectableSteamGame : INotifyPropertyChanged
{
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public SteamGameInfo Game { get; set; } = null!;

    public event PropertyChangedEventHandler? PropertyChanged;
}
