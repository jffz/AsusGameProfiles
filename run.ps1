<#
.SYNOPSIS
    Compile puis lance AsusGameProfiles (mode interface, double-clic normal).
.PARAMETER Configuration
    Debug (defaut) ou Release.
.EXAMPLE
    .\run.ps1
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet build AsusGameProfiles\AsusGameProfiles.csproj -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = "AsusGameProfiles\bin\$Configuration\net8.0-windows\win-x64\AsusGameProfiles.exe"
Start-Process -FilePath $exe
