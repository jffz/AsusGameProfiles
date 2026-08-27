<#
.SYNOPSIS
    Publie AsusGameProfiles en deploiement framework-dependent (necessite le runtime .NET 8 Desktop
    sur la machine cible, pas le SDK) dans AsusGameProfiles\bin\Release\net8.0-windows\win-x64\publish.
.EXAMPLE
    .\publish.ps1
#>
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet publish AsusGameProfiles\AsusGameProfiles.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Publie dans AsusGameProfiles\bin\Release\net8.0-windows\win-x64\publish\"
