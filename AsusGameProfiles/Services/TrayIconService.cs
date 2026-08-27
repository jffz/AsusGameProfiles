using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace AsusGameProfiles.Services;

/// <summary>
/// Icône dans la zone de notification, utilisée uniquement quand l'utilisateur active "Close to tray".
/// Ce n'est pas un service en tâche de fond : c'est juste une icône + un menu, gérés par ce process UI
/// et actifs seulement à la demande explicite de l'utilisateur (pas de polling, pas de logique propre).
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Window _window;

    public event EventHandler? ExitRequested;

    public TrayIconService(Window window)
    {
        _window = window;

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "AsusGameProfiles",
            Visible = false
        };
        _notifyIcon.DoubleClick += (_, _) => Restore();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Restore());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _notifyIcon.ContextMenuStrip = menu;
    }

    public void Show() => _notifyIcon.Visible = true;
    public void Hide() => _notifyIcon.Visible = false;

    /// <summary>
    /// Notification silencieuse quand un profil est applique en arriere-plan (voir
    /// MainWindow, abonnement a ProcessWatcherService.ProfileTriggered) -- sans ca, tout le
    /// mecanisme central de l'app (bascule automatique) est invisible pour l'utilisateur.
    /// No-op si l'icone n'est pas visible (fenetre ouverte, pas "close to tray") : une bulle
    /// sans icone dans la zone de notification pour s'ancrer n'aurait rien a montrer.
    /// </summary>
    public void ShowNotification(string title, string text)
    {
        if (!_notifyIcon.Visible) return;
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(3000);
    }

    private void Restore()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
        Hide();
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            var streamInfo = Application.GetResourceStream(new Uri("/Assets/AppIcon.ico", UriKind.Relative));
            return streamInfo != null ? new Icon(streamInfo.Stream) : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
