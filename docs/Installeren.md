# DonkiePane installeren, bijwerken en verwijderen

[English](Installing.md) · **Nederlands**

## Voor wie DonkiePane gebruikt

1. Dubbelklik op `DonkiePane-Setup-<versie>.exe`.
   - Er is geen beheerder en geen .NET nodig. DonkiePane komt in `%LOCALAPPDATA%\Programs\DonkiePane`.
   - Windows kan een SmartScreen-waarschuwing tonen, omdat de installer niet digitaal is ondertekend. Kies **Meer info** en dan **Toch uitvoeren**.
   - De installer is Nederlands of Engels, net als de taal van Windows.
2. Laat DonkiePane aan het eind starten. Bij de allereerste start komt een welkomstscherm met twee keuzes:
   - **Windows-bureaubladiconen verbergen.** DonkiePane zet je iconen in een pane met de naam Inbox. Je bestanden blijven precies waar ze staan; er wordt niets verplaatst of verwijderd. Zet je dit uit, dan zie je de iconen dubbel.
   - **Starten met Windows.**
3. Takenbronnen (Google Taken, Home Assistant, een eigen dienst) voeg je zelf toe in Instellingen, onder Takenbronnen. Zie [Bronnen.md](Bronnen.md).

DonkiePane volgt de taal van Windows (Nederlands bij een Nederlandse Windows, anders Engels). Een andere taal kies je in **Instellingen → Taal**; die geldt na opnieuw starten, en daar staat een knop **Nu opnieuw starten** voor.

### Bijwerken

DonkiePane kijkt zelf of er een nieuwe versie is: een minuut na het starten en daarna eens per dag, bij de releases op GitHub. Is er een, dan krijg je één melding bij het icoon bij de klok, en bovenaan het tray-menu staat **Bijwerken naar versie …**. Daar zie je wat er nieuw is en kies je **Nu bijwerken**:

1. DonkiePane downloadt de installer en controleert hem met het SHA-256-controlegetal dat GitHub bij de release vermeldt. Klopt dat niet, dan wordt het bestand weggegooid.
2. De installer start zonder vragen (je ziet alleen de voortgang), sluit DonkiePane netjes af, vervangt de bestanden en start DonkiePane daarna opnieuw.

Zonder klik op Nu bijwerken wordt er niets gedownload of geïnstalleerd. Het automatisch kijken zet je uit in **Instellingen → Updates**; daar kun je ook meteen controleren. Er gaat bij het kijken niets over jou of je pc mee, alleen een verzoek om de nieuwste release.

Zelf bijwerken kan ook: draai de nieuwe `DonkiePane-Setup-<versie>.exe`. Het setup-programma vraagt een draaiende DonkiePane netjes af te sluiten (dan komen de Windows-bureaubladiconen even terug), vervangt de bestanden en laat je panes, taken en instellingen staan.

### Verwijderen

Via **Instellingen → Apps → Geïnstalleerde apps → DonkiePane → Verwijderen**.

- DonkiePane sluit eerst netjes af, zodat de Windows-bureaubladiconen terugkomen.
- "Starten met Windows" wordt weggehaald.
- Het programma vraagt of je panes, taken en instellingen (in `%APPDATA%\DonkiePane`) ook weg moeten. Standaard blijven ze staan, ook bij een stille verwijdering. De opgeslagen tokens van takenbronnen horen daar ook bij.
- Je bureaubladbestanden blijven altijd staan.

### Iconen blijven verborgen

Is DonkiePane ooit hard gestopt (bijvoorbeeld via Taakbeheer) en blijven je Windows-iconen weg, dan haal je ze terug met rechtsklikken op het bureaublad → **Beeld** → **Bureaubladpictogrammen weergeven**.

## Voor wie de installer bouwt

Nodig: de .NET SDK (zie `global.json`) en Inno Setup 6. Het script controleert beide en installeert wat ontbreekt met winget (`scripts\prerequisites.ps1`); Windows kan daarbij om toestemming vragen. Heeft de pc geen winget, dan meldt het script wat je zelf moet installeren.

```powershell
.\scripts\build-installer.ps1
```

Het script:

1. controleert de .NET SDK en Inno Setup en installeert die zo nodig;
2. leest de versie uit `Directory.Build.props`;
3. publiceert het programma zelfstandig (`win-x64`, .NET erin) en de plug-ins in `plugins\<naam>` naar `artifacts\publish`;
4. laat Inno Setup (`installer\DonkiePane.iss`) er `artifacts\DonkiePane-Setup-<versie>.exe` van maken (ongeveer 50 MB).

`artifacts\` staat in `.gitignore`. Het pakket is niet ondertekend; met een code-signing-certificaat kan dat met `SignTool` in het Inno Setup-script.

Het icoon komt uit `scripts\make-icon.ps1` (schrijft `src\DonkiePane.App\Assets\DonkiePane.ico`).

### Voor de ontwikkelaar zelf

`scripts\install.ps1` bouwt en installeert naar dezelfde map zonder installer (handig tijdens het werken); `scripts\uninstall.ps1` haalt dat weer weg. Een draaiende versie van vóór `--quit` laat zich niet netjes afsluiten door het setup-programma: stop die eerst via het tray-menu (Afsluiten).

### Netjes afsluiten: `--quit`

`DonkiePane.exe --quit` vraagt een draaiende DonkiePane af te sluiten, op dezelfde manier als Afsluiten in het tray-menu (de Windows-bureaubladiconen worden dan teruggezet). Draait er niets, dan gebeurt er niets. Het setup- en verwijderprogramma gebruiken dit.

`DonkiePane.exe --restart-after <proces-id>` wacht tot dat proces weg is en start dan; DonkiePane gebruikt dit om zichzelf opnieuw te starten na het wisselen van taal.
