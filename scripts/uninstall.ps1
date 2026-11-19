<#
.SYNOPSIS
  Verwijdert DonkiePane van deze pc en zet het bureaublad terug zoals Windows het kent.

.DESCRIPTION
  - Stopt DonkiePane.
  - Zet de Windows-bureaubladiconen terug als DonkiePane ze verborgen had.
  - Haalt "Starten met Windows" weg.
  - Verwijdert het programma uit %LOCALAPPDATA%\Programs\DonkiePane.
  - Je panes, taken en instellingen in %APPDATA%\DonkiePane blijven staan,
    tenzij je -RemoveData meegeeft.

.EXAMPLE
  .\scripts\uninstall.ps1
  .\scripts\uninstall.ps1 -RemoveData
#>
param(
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$target = Join-Path $env:LOCALAPPDATA 'Programs\DonkiePane'
$data = Join-Path $env:APPDATA 'DonkiePane'

# 1. Stoppen: eerst netjes met --quit (dan zet DonkiePane de Windows-iconen zelf terug), alleen als dat niet lukt
#    een harde stop. Stap 2 zet de iconen in elk geval terug.
$exe = Join-Path $target 'DonkiePane.exe'
if (Get-Process DonkiePane -ErrorAction SilentlyContinue) {
    Write-Host 'DonkiePane stoppen...'
    if (Test-Path $exe) {
        Start-Process -FilePath $exe -ArgumentList '--quit' -Wait
        for ($i = 0; $i -lt 40 -and (Get-Process DonkiePane -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 250 }
    }
    $left = Get-Process DonkiePane -ErrorAction SilentlyContinue
    if ($left) {
        $left | Stop-Process -Force
        Start-Sleep -Seconds 1
    }
}

# 2. Windows-iconen terugzetten (dezelfde opdracht als rechtsklik -> Beeld -> Bureaubladpictogrammen weergeven).
# De vensternaam is een IntPtr: PowerShell maakt van $null bij een string-parameter een lege tekst, en dan vindt
# FindWindow niets ("Program Manager" en "FolderView" hebben een naam).
Add-Type -Name Desktop2 -Namespace DonkiePaneUninstall -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, IntPtr n);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
'@ -ErrorAction SilentlyContinue
$none = [IntPtr]::Zero
$desktop = [DonkiePaneUninstall.Desktop2]
# Meestal onder Progman, soms onder een WorkerW.
$defView = $desktop::FindWindowEx($none, $none, 'Progman', $none) | ForEach-Object { $desktop::FindWindowEx($_, $none, 'SHELLDLL_DefView', $none) }
$worker = $none
while ($defView -eq $none) {
    $worker = $desktop::FindWindowEx($none, $worker, 'WorkerW', $none)
    if ($worker -eq $none) { break }
    $defView = $desktop::FindWindowEx($worker, $none, 'SHELLDLL_DefView', $none)
}
if ($defView -ne $none) {
    $list = $desktop::FindWindowEx($defView, $none, 'SysListView32', $none)
    if ($list -ne $none -and -not $desktop::IsWindowVisible($list)) {
        [void]$desktop::SendMessage($defView, 0x0111, [IntPtr]0x7402, $none)
        Write-Host 'Windows-bureaubladiconen teruggezet.'
    }
}

# 3. Starten met Windows weghalen.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -Path $runKey -Name 'DonkiePane' -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $runKey -Name 'DonkiePane'
    Write-Host 'Starten met Windows: uit'
}
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run' -Name 'DonkiePane' -ErrorAction SilentlyContinue

# 4. Programma verwijderen.
if (Test-Path $target) {
    Remove-Item -Path $target -Recurse -Force
    Write-Host "Verwijderd: $target"
}
# Was DonkiePane met de installer geinstalleerd, dan ook de vermelding in Apps en functies weghalen
# (anders blijft er een vermelding staan die naar een verwijderde map wijst).
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{CC5D6995-47D1-4015-B1E6-D25463DEB7F2}_is1' -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $env:TEMP 'DonkiePane-update') -Recurse -Force -ErrorAction SilentlyContinue

# 5. Gegevens alleen op verzoek.
if ($RemoveData -and (Test-Path $data)) {
    Remove-Item -Path $data -Recurse -Force
    Write-Host "Gegevens verwijderd: $data"
} elseif (Test-Path $data) {
    Write-Host "Je panes en taken staan nog in $data (verwijder die map zelf, of gebruik -RemoveData)."
}

Write-Host 'Klaar.'
