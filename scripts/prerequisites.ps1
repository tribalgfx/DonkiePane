<#
.SYNOPSIS
  Controleert (en installeert zo nodig) wat nodig is om DonkiePane te bouwen. Wordt door install.ps1 en
  build-installer.ps1 aangeroepen; niet bedoeld om los te draaien.

.DESCRIPTION
  - Ensure-DotnetSdk: de .NET SDK die global.json vraagt. Ontbreekt die, dan wordt hij met winget geinstalleerd
    (Windows kan daarbij om toestemming vragen). Geeft het pad naar dotnet.exe terug.
  - Ensure-InnoSetup: Inno Setup 6, alleen nodig voor de installer. Ook via winget, per gebruiker.

  Het eindresultaat, DonkiePane-Setup-<versie>.exe, heeft zelf niets nodig: geen .NET en geen beheerder.

  Dit bestand is bewust zuiver ASCII, zodat Windows PowerShell 5.1 het niet beschadigt.
#>

function Find-Dotnet {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $default = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path $default) { return $default }
    return $null
}

# Voldoet de aanwezige SDK aan global.json in deze map? "dotnet --version" faalt als er geen passende SDK is.
function Test-DotnetSdk([string]$Dotnet, [string]$RepoRoot) {
    if (-not $Dotnet) { return $false }
    Push-Location $RepoRoot
    try {
        cmd /c "`"$Dotnet`" --version >nul 2>&1"
        return ($LASTEXITCODE -eq 0)
    }
    finally { Pop-Location }
}

function Install-WithWinget([string]$Id, [string[]]$ExtraArguments) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "winget ontbreekt, dus $Id kan niet automatisch worden geinstalleerd. Installeer het zelf (zie https://dotnet.microsoft.com/download of https://jrsoftware.org/isdl.php) en start dit script opnieuw."
    }
    & winget install --id $Id -e --accept-package-agreements --accept-source-agreements @ExtraArguments
    if ($LASTEXITCODE -ne 0) { throw "Installeren van $Id met winget is mislukt (code $LASTEXITCODE)." }
}

function Ensure-DotnetSdk([string]$RepoRoot) {
    $dotnet = Find-Dotnet
    if (Test-DotnetSdk $dotnet $RepoRoot) { return $dotnet }

    Write-Host '.NET SDK ontbreekt of is te oud voor dit project. Installeren met winget (Windows kan om toestemming vragen) ...'
    Install-WithWinget 'Microsoft.DotNet.SDK.10' @()

    # De PATH van deze sessie kent de nieuwe installatie nog niet; zoek op de standaardplek.
    $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-DotnetSdk $dotnet $RepoRoot)) {
        throw 'De .NET SDK is geinstalleerd, maar voldoet nog niet aan global.json. Open een nieuw PowerShell-venster en probeer opnieuw.'
    }
    return $dotnet
}

function Find-InnoSetup {
    @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

function Ensure-InnoSetup {
    $iscc = Find-InnoSetup
    if ($iscc) { return $iscc }

    Write-Host 'Inno Setup ontbreekt. Installeren met winget (per gebruiker) ...'
    Install-WithWinget 'JRSoftware.InnoSetup' @('--scope', 'user')

    $iscc = Find-InnoSetup
    if (-not $iscc) { throw 'Inno Setup is geinstalleerd, maar ISCC.exe is niet gevonden.' }
    return $iscc
}
