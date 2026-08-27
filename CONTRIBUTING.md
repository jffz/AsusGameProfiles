# Contributing

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows (WPF only
runs on Windows, so this can't be built or tested cross-platform).

```powershell
.\build.ps1     # compile
.\test.ps1      # run the unit tests (-Filter <name> to run a subset)
.\run.ps1       # compile + launch the app
.\publish.ps1   # framework-dependent publish to AsusGameProfiles\bin\Release\net8.0-windows\win-x64\publish\
.\package.ps1   # publish.ps1 + build the .msi installer into dist\
```

Or open `AsusGameProfiles.sln` in Visual Studio and build normally.

`package.ps1` needs the [WiX Toolset v5](https://wixtoolset.org/) CLI as a local `dotnet` tool
(already declared in `.config/dotnet-tools.json` — run `dotnet tool restore` once if it's not
picked up automatically).

## Project structure

```
AsusGameProfiles/
  App.xaml(.cs)              entry point: headless launcher mode vs. the management UI
  MainWindow.xaml(.cs)       the management UI
  Views/                     secondary windows (Add from Steam, confirm dialog)
  Models/                    GameProfile, GameProfilePreset, AppConfig, GameVisualMode
  Services/
    ProcessWatcherService.cs   polls running processes, triggers presets on launch/exit
    DwcService.cs               wraps dwc.exe (set/get, output + exit code capture)
    DwcInstaller.cs             downloads dwc.exe from ASUS's repo, checksum-verified
    SteamLibraryScanner.cs      reads libraryfolders.vdf + appmanifest_*.acf (read-only)
    SteamLaunchOptionsWriter.cs targeted localconfig.vdf editing (legacy cleanup path only)
    ConfigStore.cs              reads/writes %AppData%\AsusGameProfiles\config.json
AsusGameProfiles.Tests/     xunit tests (SteamLaunchOptionsWriter is the most safety-critical one)
AsusGameProfiles.Setup/     WiX v5 installer project
```

Config and logs live in `%AppData%\AsusGameProfiles\` (`config.json`, plus a `logs\` folder with
one file per game launch — useful if a profile doesn't apply as expected).

## Before opening a PR

- `.\build.ps1` and `.\test.ps1` should both pass. CI runs the same checks on every PR.
- If you're touching `Services/SteamLaunchOptionsWriter.cs`, be extra careful — it edits Steam's
  real config file, and its test suite exists specifically to catch regressions there.
- Keep PRs focused. Larger changes (new trigger mechanisms, architecture changes) are easier to
  discuss in an issue first.

## Reporting bugs / requesting features

Use [Issues](https://github.com/jffz/AsusGameProfiles/issues). For anything security-related, see
[SECURITY.md](SECURITY.md) instead.
