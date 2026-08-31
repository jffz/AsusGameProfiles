using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AsusGameProfiles.Services;
using AsusGameProfiles.Views;

namespace AsusGameProfiles;

public partial class App : Application
{
    // Nommes (pas juste un objet en memoire) : c'est ce qui permet a un DEUXIEME process de detecter
    // le premier, un Mutex/EventWaitHandle in-process ne serait visible que dans son propre process.
    private const string SingleInstanceMutexName = "AsusGameProfiles_SingleInstance_Mutex";
    private const string ActivateEventName = "AsusGameProfiles_SingleInstance_Activate";

    // Racine volontairement gardee en vie pour toute la duree du process (jamais dispose explicitement) :
    // le mutex ne doit se relacher qu'a la fermeture du process, exactement ce que fait le GC/l'OS ici.
    private Mutex? _singleInstanceMutex;

    /// <summary>
    /// Ouvre le menu deroulant d'un ComboBox editable des qu'on clique n'importe ou dans son champ
    /// texte, pas seulement sur la fleche -- comme le comportement natif d'un ComboBox non-editable
    /// (ex: le selecteur GameVisual). Ne referme jamais le menu (seul un clic hors du ComboBox le fait).
    /// </summary>
    private void OnEditableComboTextBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject start) return;

        DependencyObject? current = start;
        while (current != null && current is not ComboBox)
            current = VisualTreeHelper.GetParent(current);

        // GotFocus se declenche une fois le focus reellement etabli (apres toute logique interne du
        // ComboBox liee au changement de focus) : contrairement a un handler sur MouseDown, l'ouverture
        // ici n'est plus aussitot annulee par cette meme logique.
        if (current is ComboBox comboBox && !comboBox.IsDropDownOpen)
            comboBox.IsDropDownOpen = true;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ThemeService.ApplyOsTheme();

        var args = Environment.GetCommandLineArgs();

        // Legacy/defensif (2026-08-27 : le mecanisme "options de lancement Steam" a ete retire, plus
        // rien n'ecrit ce genre d'option -- ce point d'entree reste gere au cas ou une installation
        // Steam garde encore une ancienne option pas encore nettoyee par MainWindow) :
        //   "AsusGameProfiles.exe" --launch <appid> <exe_du_jeu> [args...]
        // -> pas d'interface, on applique le profil, on lance le jeu, on attend, on restaure, on quitte.
        if (args.Length > 1 && args[1].Equals("--launch", StringComparison.OrdinalIgnoreCase))
        {
            // GetCommandLineArgs()[0] est le chemin de cet exécutable lui-même : GameLauncher attend
            // un tableau qui commence directement par "--launch", donc on l'enlève avant de déléguer.
            int code = GameLauncher.RunLaunchMode(args.Skip(1).ToArray());
            Shutdown(code);
            return;
        }

        // Sinon : lancement normal (double-clic) -> interface de gestion des profils. Un seul exemplaire
        // de l'UI a la fois (2026-08-31, vrai bug rapporte : rien n'empechait de lancer l'app plusieurs
        // fois, ce qui demarrait autant de ProcessWatcherService/icones de zone de notification en
        // parallele -- polling redondant, ecritures concurrentes de config.json, plusieurs icones tray).
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);

        // Cree systematiquement (pas juste "ouvert" par la deuxieme instance) : evite une course ou la
        // deuxieme instance appellerait EventWaitHandle.OpenExisting avant que la premiere ait fini de
        // creer l'event -- ce constructeur ouvre l'event existant s'il y en a deja un, sinon le cree.
        var activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

        if (!createdNew)
        {
            // Une instance de l'UI tourne deja : on lui demande de se montrer, et celle-ci se ferme
            // sans jamais ouvrir de fenetre.
            activateEvent.Set();
            Shutdown(0);
            return;
        }

        var window = new MainWindow();
        window.Show();

        // Ecoute en tache de fond, pour toute la duree de vie de l'app : une deuxieme instance signale
        // cet event au lieu d'ouvrir sa propre fenetre (voir ci-dessus).
        var listenerThread = new Thread(() =>
        {
            while (true)
            {
                activateEvent.WaitOne();
                Dispatcher.Invoke(window.ActivateFromAnotherInstance);
            }
        })
        { IsBackground = true };
        listenerThread.Start();
    }
}
