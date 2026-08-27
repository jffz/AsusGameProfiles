<#
Placeholders (__VERSION__, __URL__, __CHECKSUM__) are substituted by .github/workflows/release.yml
before `choco pack` -- this file stays templated in the repo, don't hand-fill it per release.
#>
$ErrorActionPreference = "Stop"

$packageArgs = @{
    packageName    = "asusgameprofiles"
    fileType       = "msi"
    url64bit       = "__URL__"
    checksum64     = "__CHECKSUM__"
    checksumType64 = "sha256"
    silentArgs     = "/quiet /norestart"
    validExitCodes = @(0, 3010)
}

Install-ChocolateyPackage @packageArgs
