param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    [xml]$props = Get-Content "Directory.Build.props"
    $version = [string]$props.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Project version could not be resolved."
    }

    $bundleVersion = "$version.0"
    $publishDir = Join-Path $repoRoot "artifacts\publish"
    $updaterDir = Join-Path $repoRoot "artifacts\updater"
    $uiDir = Join-Path $repoRoot "artifacts\wix\ui"
    $msiOut = Join-Path $repoRoot "artifacts\wix\msi"
    $bundleOut = Join-Path $repoRoot "artifacts\wix\installer"
    $finalOut = Join-Path $repoRoot "artifacts\nmc-installer"

    foreach ($dir in @($publishDir, $updaterDir, $uiDir, $msiOut, $bundleOut, $finalOut)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    & dotnet publish "src/NmcScsLauncher.App/NmcScsLauncher.App.csproj" --configuration $Configuration --output $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed." }

    & dotnet publish "src/NmcScsLauncher.Updater/NmcScsLauncher.Updater.csproj" --configuration $Configuration --runtime win-x64 --self-contained true "-p:PublishSingleFile=true" --output $updaterDir
    if ($LASTEXITCODE -ne 0) { throw "Updater publish failed." }

    Copy-Item (Join-Path $updaterDir "NmcScsLauncher.Updater.exe") (Join-Path $publishDir "NmcScsLauncher.Updater.exe") -Force

    & dotnet publish "installer-wix/NmcScsLauncher.Installer.UI/NmcScsLauncher.Installer.UI.csproj" --configuration $Configuration --runtime win-x64 --self-contained true --output $uiDir
    if ($LASTEXITCODE -ne 0) { throw "NMC Installer UI publish failed." }

    & dotnet build "installer-wix/NmcScsLauncher.Installer.Msi/NmcScsLauncher.Installer.Msi.wixproj" --configuration $Configuration "-p:ProductVersion=$version" "-p:PublishDir=$publishDir"
    if ($LASTEXITCODE -ne 0) { throw "WiX MSI build failed." }

    $msi = Get-ChildItem $msiOut -Filter "NMC-SCS-LAUNCHER-$version.msi" -Recurse | Select-Object -First 1
    if (-not $msi) { throw "WiX MSI was not created." }

    $bootstrapper = Join-Path $uiDir "NmcScsLauncher.Installer.UI.exe"
    if (-not (Test-Path $bootstrapper)) { throw "NMC Installer UI executable was not created." }

    & dotnet build "installer-wix/NmcScsLauncher.Installer.Bundle/NmcScsLauncher.Installer.Bundle.wixproj" --configuration $Configuration "-p:ProductVersion=$version" "-p:BundleVersion=$bundleVersion" "-p:MsiPath=$($msi.FullName)" "-p:BootstrapperExe=$bootstrapper"
    if ($LASTEXITCODE -ne 0) { throw "WiX Burn bundle build failed." }

    $bundle = Get-ChildItem $bundleOut -Filter "NMC-SCS-LAUNCHER-$version-Setup.exe" -Recurse | Select-Object -First 1
    if (-not $bundle) { throw "NMC custom installer was not created." }

    $finalInstaller = Join-Path $finalOut $bundle.Name
    Copy-Item $bundle.FullName $finalInstaller -Force

    $sha256 = (Get-FileHash -Path $finalInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$finalInstaller.sha256" -Value "$sha256  $($bundle.Name)" -Encoding ascii -NoNewline

    Write-Host "NMC custom installer: $finalInstaller"
    Write-Host "SHA-256: $sha256"
}
finally {
    Pop-Location
}
