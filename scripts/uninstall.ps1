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
Add-Type -Name Desktop -Namespace DonkiePaneUninstall -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string n);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
'@ -ErrorAction SilentlyContinue
$progman = [DonkiePaneUninstall.Desktop]::FindWindow('Progman', $null)
$defView = [DonkiePaneUninstall.Desktop]::FindWindowEx($progman, [IntPtr]::Zero, 'SHELLDLL_DefView', $null)
if ($defView -ne [IntPtr]::Zero) {
    $list = [DonkiePaneUninstall.Desktop]::FindWindowEx($defView, [IntPtr]::Zero, 'SysListView32', $null)
    if ($list -ne [IntPtr]::Zero -and -not [DonkiePaneUninstall.Desktop]::IsWindowVisible($list)) {
        [void][DonkiePaneUninstall.Desktop]::SendMessage($defView, 0x0111, [IntPtr]0x7402, [IntPtr]::Zero)
        Write-Host 'Windows-bureaubladiconen teruggezet.'
    }
}

# 3. Starten met Windows weghalen.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -Path $runKey -Name 'DonkiePane' -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $runKey -Name 'DonkiePane'
    Write-Host 'Starten met Windows: uit'
}

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
