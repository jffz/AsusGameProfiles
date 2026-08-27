<#
.SYNOPSIS
    Lance la suite de tests unitaires (AsusGameProfiles.Tests).
.PARAMETER Filter
    Filtre optionnel passe a --filter (ex: "FullyQualifiedName~SteamLaunchOptionsWriter").
.EXAMPLE
    .\test.ps1
    .\test.ps1 -Filter "ClearLaunchOptions"
#>
param(
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if ($Filter) {
    dotnet test AsusGameProfiles.Tests\AsusGameProfiles.Tests.csproj --filter $Filter
} else {
    dotnet test AsusGameProfiles.sln
}
exit $LASTEXITCODE
