<#
.SYNOPSIS
  Bouwt DonkiePane en zet het in een vaste map, zodat het veilig met Windows kan starten.

.DESCRIPTION
  - Publiceert naar %LOCALAPPDATA%\Programs\DonkiePane (geen beheerdersrechten nodig).
  - Stopt een draaiende DonkiePane eerst, zodat de bestanden vervangen kunnen worden.
  - Met -Autostart start DonkiePane voortaan mee met Windows (HKCU\...\Run).
  - Je instellingen, panes en taken in %APPDATA%\DonkiePane blijven onaangeroerd.

.EXAMPLE
  .\scripts\install.ps1 -Autostart
#>
param(
    [switch]$Autostart,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$target = Join-Path $env:LOCALAPPDATA 'Programs\DonkiePane'
$exe = Join-Path $target 'DonkiePane.exe'

# Ontbreekt de .NET SDK, dan wordt hij met winget geinstalleerd (zie prerequisites.ps1).
. (Join-Path $PSScriptRoot 'prerequisites.ps1')
$dotnet = Ensure-DotnetSdk $repo

# Een draaiende versie stoppen (de bestanden zijn anders in gebruik). DonkiePane heeft geen
# hoofdvenster, dus dit is een harde stop. De nieuwe versie neemt de bureaubladweergave
# gewoon over zoals die nu is: stonden de Windows-iconen verborgen, dan blijven ze dat tot
# je DonkiePane via het tray-menu afsluit.
$running = Get-Process DonkiePane -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'DonkiePane stoppen...'
    $running | Stop-Process -Force
    Start-Sleep -Seconds 1
}

Write-Host "Publiceren naar $target ..."
# Zelfstandig (met .NET erin), net als de installer: die gebruikt dezelfde map. Een versie die .NET van Windows
# verwacht, start niet tussen de .NET-bestanden die de installer daar heeft neergezet (hostfxr.dll naast het
# programma laat het daar naar .NET zoeken).
& $dotnet publish (Join-Path $repo 'src\DonkiePane.App\DonkiePane.App.csproj') `
    -c Release -r win-x64 --self-contained true -p:SatelliteResourceLanguages=en -o $target --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Publiceren is mislukt.' }

# Bronnen zijn plug-ins in plugins\<naam>. Het programma verwijst er niet naar; hij laadt ze uit deze map.
$plugins = [ordered]@{ google = 'DonkiePane.Plugins.Google'; rest = 'DonkiePane.Plugins.RestTasks'; homeassistant = 'DonkiePane.Plugins.HomeAssistant' }
foreach ($name in $plugins.Keys) {
    $project = $plugins[$name]
    $dest = Join-Path $target "plugins\$name"
    Write-Host "Plug-in $name publiceren ..."
    & $dotnet publish (Join-Path $repo "src\$project\$project.csproj") -c Release -o $dest --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Publiceren van plug-in $name is mislukt." }
    # De gedeelde onderdelen komen van het programma zelf; een eigen kopie zou het contract breken.
    Remove-Item (Join-Path $dest 'DonkiePane.Core.*'), (Join-Path $dest 'DonkiePane.Tasks.*') -ErrorAction SilentlyContinue
}

if ($Autostart) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Set-ItemProperty -Path $runKey -Name 'DonkiePane' -Value "`"$exe`""
    # Zonder "aan" in StartupApproved slaat Windows 11 de Run-waarde bij het inloggen over.
    $approvedKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
    if (-not (Test-Path $approvedKey)) { New-Item -Path $approvedKey -Force | Out-Null }
    Set-ItemProperty -Path $approvedKey -Name 'DonkiePane' -Type Binary -Value ([byte[]](2,0,0,0,0,0,0,0,0,0,0,0))
    Write-Host 'Starten met Windows: aan'
}

if (-not $NoStart) {
    Start-Process $exe
    Write-Host 'DonkiePane gestart.'
}
