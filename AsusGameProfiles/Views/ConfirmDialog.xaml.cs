using System.Windows;
using AsusGameProfiles.Services;

namespace AsusGameProfiles.Views;

/// <summary>
/// Boite de confirmation Oui/Non themee (contrairement a <see cref="MessageBox"/>, qui reste toujours
/// en chrome Windows clair natif meme quand le reste de l'app est en theme sombre).
/// </summary>
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string message, string confirmText, string cancelText, string title)
    {
        InitializeComponent();
        WindowChromeHelper.SyncTitleBarWithTheme(this);

        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>Affiche la boite et bloque jusqu'a la reponse. Renvoie vrai si l'utilisateur a confirme.</summary>
    public static bool Show(Window owner, string message, string confirmText = "Yes", string cancelText = "Cancel", string title = "Confirm")
    {
        var dialog = new ConfirmDialog(message, confirmText, cancelText, title) { Owner = owner };
        return dialog.ShowDialog() == true;
    }
}
