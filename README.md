# AsusGameProfiles

Petite app Windows (WPF, .NET 8, x64) qui généralise ton `cs2_gamevisual.bat` à tous tes jeux
Steam : une interface pour choisir, par jeu, le profil GameVisual + Frame Rate Boost à appliquer
au lancement et à la fermeture, au lieu de maintenir un `.bat` par jeu à la main.

## Compiler

Nécessite le SDK .NET 8 (https://dotnet.microsoft.com/download/dotnet/8.0) sur ta machine Windows --
ça n'a pas pu être compilé/testé côté serveur (WPF ne peut de toute façon s'exécuter que sous Windows).

```powershell
cd AsusGameProfiles
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false
```

L'exécutable sort dans `AsusGameProfiles\bin\Release\net8.0-windows\win-x64\publish\AsusGameProfiles.exe`.
Copie tout le contenu de ce dossier `publish\` où tu veux (ex: `C:\Tools\AsusGameProfiles\`) --
c'est un déploiement "framework-dependent", donc il faut le runtime .NET 8 Desktop installé sur la machine
qui l'exécute (ce qui est ton cas puisque tu as le SDK).

Alternative simple : ouvrir `AsusGameProfiles.sln` dans Visual Studio et faire Build.

Scripts PowerShell disponibles à la racine : `.\build.ps1` (compile), `.\test.ps1` (tests unitaires,
`-Filter` optionnel), `.\run.ps1` (compile + lance l'app), `.\publish.ps1` (publie dans `publish\`).

## Installeur (.msi)

```powershell
.\package.ps1
```

Publie l'app puis construit un installeur Windows classique dans `dist\AsusGameProfiles-Setup.msi`
(install dans `Program Files`, raccourci menu Démarrer, désinstallation depuis "Applications").
Le projet installeur (`AsusGameProfiles.Setup\`, WiX Toolset v5) nécessite le runtime .NET 8 Desktop
sur la machine cible -- pas embarqué dans le .msi, `AsusGameProfiles.exe` invite à l'installer
automatiquement si absent au premier lancement.

## Utilisation

1. Lance `AsusGameProfiles.exe` normalement (double-clic) → ça ouvre l'interface de gestion.
2. Renseigne le chemin de `dwc.exe` en bas de fenêtre (le CLI officiel ASUS Display Control),
   et clique "Tester" pour vérifier qu'il répond.
3. "+ Ajouter depuis Steam" → coche les jeux à gérer. Pour chaque jeu sélectionné, une fenêtre
   te demande de pointer le bon `.exe` (pré-ouverte dans le bon dossier d'installation) -- nécessaire
   car Steam ne stocke pas toujours un chemin d'exécutable unique et fiable dans ses fichiers internes.
4. Sélectionne le jeu dans la liste, choisis le mode GameVisual + Frame Rate Boost pour
   "Au lancement" et "À la fermeture", clique "Enregistrer ce profil".
5. Clique "Enregistrer ce profil" -- **l'app écrit elle-même l'option de lancement dans le fichier
   de config Steam** (`localconfig.vdf`), aucun copier-coller à faire dans Steam.

   ⚠️ Steam doit être complètement fermé (icône dans la zone de notification comprise) au moment
   où tu cliques "Enregistrer" pour un jeu Steam -- sinon l'écriture est refusée avec un message clair
   (Steam peut sinon écraser le changement ou corrompre le fichier en cas d'accès concurrent). Une
   sauvegarde horodatée (`localconfig.vdf.bak-AAAAMMJJ-HHMMSS`) est créée à chaque écriture, à côté
   du fichier original, au cas où tu voudrais revenir en arrière manuellement.

Pour un jeu ajouté manuellement (pas suivi par Steam), il n'y a pas d'"options de lancement" à écrire
nulle part -- utilise directement le bouton **"Lancer maintenant"** qui apparaît dans l'app pour ce
profil : il applique le réglage, lance le jeu, attend sa fermeture, puis restaure l'écran, sans quitter
l'interface.

C'est tout -- plus besoin de créer un `.bat` par jeu, ni de toucher aux propriétés Steam.

## Pourquoi pas de détection en tâche de fond (WMI ou autre) ?

Ce n'est pas nécessaire : comme avec ton script actuel, c'est Steam qui appelle directement
`AsusGameProfiles.exe --launch <appid> %command%` via les options de lancement. L'appli lance
alors elle-même le jeu (`Process.Start` + `Process.WaitForExit()`, l'équivalent fiable de
`start /wait` en .NET) et applique les réglages avant/après. Il n'y a donc jamais de surveillance
de processus en arrière-plan, pas besoin de droits administrateur, et pas de service qui tourne
en permanence.

**Sur la question anti-cheat (VAC / FACEIT) :** cette architecture n'a rigoureusement rien à voir
avec ce que détectent les anti-cheats. Ce que VAC et l'anticheat FACEIT surveillent, c'est l'injection
de code dans le processus du jeu, la lecture/écriture de sa mémoire, le hooking du rendu (DirectX/OpenGL),
ou des pilotes noyau suspects -- c'est-à-dire tout ce qui touche directement au processus protégé.
`AsusGameProfiles.exe` ne fait ni l'un ni l'autre : c'est un processus totalement externe qui (1) lance
`cs2.exe` comme le ferait Steam lui-même, (2) parle en DDC/CI à l'écran via `dwc.exe` (aucune interaction
avec le jeu), (3) attend que le processus se termine. Cette architecture est d'ailleurs exactement le
mécanisme qu'utilise DisplayWidget Center depuis toujours pour son "App Tweaker" -- un logiciel préinstallé
sur des millions de PC gamers, y compris ceux qui jouent en compétitif tous les jours.
Si une détection de processus externe suffisait à déclencher un ban, la moitié des overlays
(Discord, MSI Afterburner/RTSS, GeForce Experience, DisplayWidget Center lui-même) poserait
déjà problème depuis des années.

## Écriture directe des options de lancement dans Steam

L'app édite directement `userdata\<compte>\config\localconfig.vdf` (le fichier où Steam stocke,
entre autres, les options de lancement par jeu). C'est fait avec un vrai analyseur de blocs
(`Services/SteamLaunchOptionsWriter.cs`) qui repère précisément le bloc `apps/<appid>/LaunchOptions`
par comptage d'accolades -- pas un remplacement de texte à l'aveugle qui risquerait de toucher au
mauvais jeu ou de casser la structure du fichier. Tout le reste du fichier (autres jeux, réglages
cloud, etc.) est recopié à l'identique.

Trois garde-fous, parce que ce fichier est sensible :
- **Refus d'écrire si Steam tourne encore** (détecté via le processus `steam.exe`) -- message clair
  demandant de fermer Steam plutôt que d'écrire quand même et risquer un conflit.
- **Sauvegarde horodatée automatique** avant chaque écriture, à côté du fichier original.
- Si la structure attendue (`Software/Valve/Steam/apps`) n'est pas trouvée dans le fichier, l'app
  abandonne l'écriture proprement plutôt que de deviner et risquer de produire un fichier invalide.

Si plusieurs comptes Windows ont utilisé Steam sur cette machine, l'app choisit le `localconfig.vdf`
modifié le plus récemment (en pratique, le compte actif).

## Structure du projet

```
AsusGameProfiles/
  App.xaml(.cs)              point d'entrée : bascule mode lanceur silencieux vs interface
  MainWindow.xaml(.cs)       interface de gestion des profils
  Views/AddFromSteamWindow   fenêtre de sélection des jeux détectés
  Models/                    GameProfile, AppConfig, GameVisualMode (+ catalogue d'affichage)
  Services/
    SteamLibraryScanner.cs      lecture de libraryfolders.vdf + appmanifest_*.acf (aucune écriture)
    SteamLaunchOptionsWriter.cs écriture ciblée de LaunchOptions dans localconfig.vdf (avec backup)
    DwcService.cs               wrapper autour de dwc.exe (set/get, capture de sortie et code retour)
    GameLauncher.cs             le "mode lanceur" appelé par Steam (--launch <appid> <exe> [args])
    ConfigStore.cs              lecture/écriture de %AppData%\AsusGameProfiles\config.json
```

La config et les logs sont stockés dans `%AppData%\AsusGameProfiles\` (config.json + dossier logs/,
un fichier par lancement de jeu -- utile pour déboguer si un profil ne s'applique pas comme prévu).
