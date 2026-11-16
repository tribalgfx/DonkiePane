# DonkiePane installeren, bijwerken en verwijderen

## Voor wie DonkiePane gebruikt

1. Dubbelklik op `DonkiePane-Setup-<versie>.exe`.
   - Er is geen beheerder en geen .NET nodig. DonkiePane komt in `%LOCALAPPDATA%\Programs\DonkiePane`.
   - Windows kan een SmartScreen-waarschuwing tonen, omdat de installer niet digitaal is ondertekend. Kies **Meer info** en dan **Toch uitvoeren**.
2. Laat DonkiePane aan het eind starten. Bij de allereerste start komt een welkomstscherm met twee keuzes:
   - **Windows-bureaubladiconen verbergen.** DonkiePane zet je iconen in een pane met de naam Inbox. Je bestanden blijven precies waar ze staan; er wordt niets verplaatst of verwijderd. Zet je dit uit, dan zie je de iconen dubbel.
   - **Starten met Windows.**
3. Takenbronnen (Google Taken, Home Assistant, een eigen dienst) voeg je zelf toe in Instellingen, onder Takenbronnen. Zie [Bronnen.md](Bronnen.md).

### Bijwerken

Draai de nieuwe `DonkiePane-Setup-<versie>.exe`. Het setup-programma vraagt een draaiende DonkiePane netjes af te sluiten (dan komen de Windows-bureaubladiconen even terug), vervangt de bestanden en laat je panes, taken en instellingen staan.

### Verwijderen

Via **Instellingen → Apps → Geïnstalleerde apps → DonkiePane → Verwijderen**.

- DonkiePane sluit eerst netjes af, zodat de Windows-bureaubladiconen terugkomen.
- "Starten met Windows" wordt weggehaald.
- Het programma vraagt of je panes, taken en instellingen (in `%APPDATA%\DonkiePane`) ook weg moeten. Standaard blijven ze staan, ook bij een stille verwijdering. De opgeslagen tokens van takenbronnen horen daar ook bij.
- Je bureaubladbestanden blijven altijd staan.

### Iconen blijven verborgen

Is DonkiePane ooit hard gestopt (bijvoorbeeld via Taakbeheer) en blijven je Windows-iconen weg, dan haal je ze terug met rechtsklikken op het bureaublad → **Beeld** → **Bureaubladpictogrammen weergeven**.

## Voor wie de installer bouwt

Nodig: de .NET SDK (zie `global.json`) en Inno Setup 6.

```powershell
winget install JRSoftware.InnoSetup
.\scripts\build-installer.ps1
```

Het script:

1. leest de versie uit `Directory.Build.props`;
2. publiceert het programma zelfstandig (`win-x64`, .NET erin) en de plug-ins in `plugins\<naam>` naar `artifacts\publish`;
3. laat Inno Setup (`installer\DonkiePane.iss`) er `artifacts\DonkiePane-Setup-<versie>.exe` van maken (ongeveer 50 MB).

`artifacts\` staat in `.gitignore`. Het pakket is niet ondertekend; met een code-signing-certificaat kan dat met `SignTool` in het Inno Setup-script.

Het icoon komt uit `scripts\make-icon.ps1` (schrijft `src\DonkiePane.App\Assets\DonkiePane.ico`).

### Voor de ontwikkelaar zelf

`scripts\install.ps1` bouwt en installeert naar dezelfde map zonder installer (handig tijdens het werken); `scripts\uninstall.ps1` haalt dat weer weg. Een draaiende versie van vóór `--quit` laat zich niet netjes afsluiten door het setup-programma: stop die eerst via het tray-menu (Afsluiten).

### Netjes afsluiten: `--quit`

`DonkiePane.exe --quit` vraagt een draaiende DonkiePane af te sluiten, op dezelfde manier als Afsluiten in het tray-menu (de Windows-bureaubladiconen worden dan teruggezet). Draait er niets, dan gebeurt er niets. Het setup- en verwijderprogramma gebruiken dit.
