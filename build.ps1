<#
.SYNOPSIS
    Compile AsusGameProfiles (et le projet de tests).
.PARAMETER Configuration
    Debug (defaut) ou Release.
.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Release
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet build AsusGameProfiles.sln -c $Configuration
exit $LASTEXITCODE
