<#
.SYNOPSIS
    Publie AsusGameProfiles puis construit l'installeur Windows (.msi) dans dist\.
.EXAMPLE
    .\package.ps1
#>
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "== dotnet publish ==" -ForegroundColor Cyan
dotnet publish AsusGameProfiles\AsusGameProfiles.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$publishDir = Resolve-Path "AsusGameProfiles\bin\Release\net8.0-windows\win-x64\publish"

Write-Host "== dotnet build (installeur WiX) ==" -ForegroundColor Cyan
dotnet build AsusGameProfiles.Setup\AsusGameProfiles.Setup.wixproj -c Release -p:AppPublishDir="$publishDir"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$msi = "AsusGameProfiles.Setup\bin\x64\Release\AsusGameProfiles-Setup.msi"
$distDir = "dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Copy-Item $msi -Destination "$distDir\AsusGameProfiles-Setup.msi" -Force

Write-Host ""
Write-Host "Installeur pret : $distDir\AsusGameProfiles-Setup.msi" -ForegroundColor Green
