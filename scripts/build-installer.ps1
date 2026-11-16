<#
.SYNOPSIS
  Bouwt DonkiePane-Setup-<versie>.exe: een installer die zonder .NET of beheerdersrechten werkt.

.DESCRIPTION
  1. Publiceert het programma zelfstandig (met .NET erin) naar artifacts\publish, plus de plug-ins in plugins\<naam>.
  2. Laat Inno Setup (installer\DonkiePane.iss) er een Setup.exe van maken in artifacts\.
  Inno Setup installeren: winget install JRSoftware.InnoSetup

  De installer is niet digitaal ondertekend; Windows kan daarom een SmartScreen-waarschuwing tonen
  ("Meer info" -> "Toch uitvoeren"). Ondertekenen kan met een code-signing-certificaat.

.EXAMPLE
  .\scripts\build-installer.ps1
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $repo 'artifacts\publish'

# Ontbreekt de .NET SDK of Inno Setup, dan worden ze met winget geinstalleerd (zie prerequisites.ps1).
. (Join-Path $PSScriptRoot 'prerequisites.ps1')
$dotnet = Ensure-DotnetSdk $repo
$iscc = Ensure-InnoSetup

# De versie komt uit Directory.Build.props.
[xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props') -Raw
$version = ($props.Project.PropertyGroup.Version | Select-Object -First 1).Trim()
Write-Host "Versie $version"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host 'Programma publiceren (zelfstandig, win-x64) ...'
& $dotnet publish (Join-Path $repo 'src\DonkiePane.App\DonkiePane.App.csproj') `
    -c Release -r win-x64 --self-contained true -p:SatelliteResourceLanguages=en -o $publish --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Publiceren is mislukt.' }

$plugins = [ordered]@{ google = 'DonkiePane.Plugins.Google'; rest = 'DonkiePane.Plugins.RestTasks'; homeassistant = 'DonkiePane.Plugins.HomeAssistant' }
foreach ($name in $plugins.Keys) {
    $project = $plugins[$name]
    $dest = Join-Path $publish "plugins\$name"
    Write-Host "Plug-in $name publiceren ..."
    & $dotnet publish (Join-Path $repo "src\$project\$project.csproj") -c Release -o $dest --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Publiceren van plug-in $name is mislukt." }
    # De gedeelde onderdelen komen van het programma zelf; een eigen kopie zou het contract breken.
    Remove-Item (Join-Path $dest 'DonkiePane.Core.*'), (Join-Path $dest 'DonkiePane.Tasks.*') -ErrorAction SilentlyContinue
}

Write-Host 'Installer bouwen ...'
& $iscc "/DAppVersion=$version" "/DSourceDir=$publish" (Join-Path $repo 'installer\DonkiePane.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup meldde een fout.' }

$setup = Join-Path $repo "artifacts\DonkiePane-Setup-$version.exe"
$size = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "Klaar: $setup ($size MB)"
