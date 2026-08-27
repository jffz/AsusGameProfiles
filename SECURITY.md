# Security Policy

## Reporting a vulnerability

If you find a security issue in AsusGameProfiles — especially anything related to how it reads or
writes files outside its own config folder (Steam's `localconfig.vdf`, the Windows registry, or the
`dwc.exe` download/verification path in `Services/DwcInstaller.cs`) — please report it privately
rather than opening a public issue.

Use GitHub's private vulnerability reporting for this repository: open the **Security** tab on
[github.com/jffz/AsusGameProfiles](https://github.com/jffz/AsusGameProfiles) and click
**"Report a vulnerability"**. This opens a private advisory visible only to the maintainer until a
fix is ready.

For anything else (bugs, feature requests, questions), use the regular
[Issues](https://github.com/jffz/AsusGameProfiles/issues) tab.

## Scope

This is a hobby project maintained by one person, with no formal SLA on response time. Reports are
still genuinely welcome and will be looked at as soon as possible — this app runs with the same
privileges as any other desktop app you install, touches Steam's launch configuration in specific
cases, and downloads `dwc.exe` from ASUS's GitHub repository on first use, so reports around those
paths are taken seriously.
