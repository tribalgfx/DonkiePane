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

# 1. Stoppen. Een harde stop laat de Windows-iconen verborgen, dus daarna zelf herstellen.
$running = Get-Process DonkiePane -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'DonkiePane stoppen...'
    $running | Stop-Process -Force
    Start-Sleep -Seconds 1
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

# 5. Gegevens alleen op verzoek.
if ($RemoveData -and (Test-Path $data)) {
    Remove-Item -Path $data -Recurse -Force
    Write-Host "Gegevens verwijderd: $data"
} elseif (Test-Path $data) {
    Write-Host "Je panes en taken staan nog in $data (verwijder die map zelf, of gebruik -RemoveData)."
}

Write-Host 'Klaar.'
