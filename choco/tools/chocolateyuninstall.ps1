$ErrorActionPreference = "Stop"

$packageName = "asusgameprofiles"
$softwareName = "AsusGameProfiles"

$key = Get-UninstallRegistryKey -SoftwareName $softwareName

if ($key.Count -eq 1) {
    $key | ForEach-Object {
        $uninstallArgs = "/x `"$($_.PSChildName)`" /quiet /norestart"
        Uninstall-ChocolateyPackage -PackageName $packageName -FileType "msi" -SilentArgs $uninstallArgs -ValidExitCodes @(0, 3010)
    }
} elseif ($key.Count -eq 0) {
    Write-Warning "$packageName has already been uninstalled by other means."
} elseif ($key.Count -gt 1) {
    Write-Warning "$($key.Count) matches found for '$softwareName' -- please uninstall manually."
    $key | ForEach-Object { Write-Warning $_.DisplayName }
}
