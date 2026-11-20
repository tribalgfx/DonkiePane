<p align="center">
  <img src="src/DonkiePane.App/Assets/DonkiePane-256.png" alt="DonkiePane" width="128" />
</p>

# DonkiePane

Vakken (panes) op je Windows-bureaublad voor je snelkoppelingen en je taken. Met een rustig uiterlijk en een knipoog naar de ruitjes van een theedoek.

## Wat het doet

- **Panes op het bureaublad.** Ze staan achter je vensters en blijven staan bij Win+D.
- **Je bureaubladiconen in panes.** Wat je op het bureaublad met een bestand of snelkoppeling doet, kan in een pane ook: openen, selecteren (Ctrl, Shift, een kader trekken), slepen naar Verkenner, een app, een map of de Prullenbak, bestanden in een pane slepen, Delete, F2, Ctrl+C/X/V, Ctrl+Shift+N, Alt+Enter en het menu van Verkenner (rechts klikken op een lege plek geeft Plakken en Nieuw). Laat je iconen los op een lege plek van het bureaublad, dan komt daar een nieuw pane mee. Windows voert het uit, dus meldingen en Prullenbak zijn zoals altijd; uit zichzelf verplaatst of verwijdert DonkiePane niets. Nieuwe bestanden komen in de Inbox-pane, of in het pane waarin je ze plakte of neerzette.
- **Takenpanes.** Taken toevoegen, afvinken, ordenen en met een ster markeren. Lokaal, of gesynchroniseerd met een bron die je zelf toevoegt:
  - Google Taken
  - Home Assistant (to-do lijsten)
  - een eigen dienst via het protocol Tasks REST v1

  Wat je offline doet wordt bewaard en later verstuurd. Zie [docs/Bronnen.md](docs/Bronnen.md).
- **Rustig en aanpasbaar.** Effen thema's, een eigen kleur en transparantie per pane, een ruitraster om panes op vast te klikken en optioneel het theedoek-ruitpatroon.
- **Dubbelklik op een leeg stuk bureaublad** verbergt alle panes; nog een keer haalt ze terug.
- **Meerdere schermen.** Valt een scherm weg, dan komen de panes naar een ander scherm en gaan ze terug zodra het scherm terugkomt.

## Installeren

Download `DonkiePane-Setup-<versie>.exe` en start hem. Er is geen .NET en geen beheerder nodig; het programma komt in `%LOCALAPPDATA%\Programs\DonkiePane`. Windows kan een SmartScreen-waarschuwing tonen omdat de installer niet is ondertekend: kies **Meer info** en dan **Toch uitvoeren**.

Bij de eerste start legt een welkomstscherm uit wat er met je bureaublad gebeurt en kun je kiezen of de Windows-iconen verborgen worden. DonkiePane meldt zelf als er een nieuwe versie is en werkt op jouw klik bij (of draai een nieuwe installer eroverheen); verwijderen gaat via Apps en functies. Zie [docs/Installeren.md](docs/Installeren.md).

## Zelf bouwen

Nodig: Windows 10 of 11. De scripts controleren zelf of de .NET SDK (zie `global.json`) en, voor de installer, Inno Setup aanwezig zijn en installeren wat ontbreekt met winget. Windows kan daarbij om toestemming vragen.

```powershell
.\scripts\install.ps1            # bouwt en installeert naar %LOCALAPPDATA%\Programs\DonkiePane
.\scripts\build-installer.ps1    # maakt artifacts\DonkiePane-Setup-<versie>.exe
dotnet test                      # de tests (de .NET SDK moet dan al aanwezig zijn)
```

Wie alleen DonkiePane gebruikt, heeft dit niet nodig: de installer bevat alles, ook .NET.

## Opbouw

| Map | Inhoud |
|---|---|
| `src/DonkiePane.App` | WPF: panes, tray-icoon, instellingen, thema's |
| `src/DonkiePane.Core` | Model en logica: panes, items, raster, opslag |
| `src/DonkiePane.Shell` | Win32: bureaubladlaag, iconen, verborgen iconen, muishaak |
| `src/DonkiePane.Tasks` | Contract voor takenbronnen, plug-in-lader, lokale opslag, offline wachtrij |
| `src/DonkiePane.Plugins.*` | De bronnen zelf: Google Taken, REST, Home Assistant |
| `installer`, `scripts` | Installer en hulpscripts |
| `tests` | Tests voor Core, Tasks en App |

Het ontwerp en de keuzes staan in [PLAN.md](PLAN.md).

## Gegevens en veiligheid

- Instellingen, panes en taken staan per gebruiker in `%APPDATA%\DonkiePane`. Geheimen (tokens, wachtwoorden van bronnen) staan daar versleuteld met Windows (DPAPI) en nooit in een gewoon bestand of in git.
- Verbindingen met bronnen gaan alleen over HTTPS (Home Assistant over http alleen na een uitdrukkelijke keuze). Een bron kan met een certificaat-vingerafdruk worden vastgepind.
- Plug-ins worden alleen geladen uit de map `plugins` naast het programma en uit `%APPDATA%\DonkiePane\plugins`, nooit van internet.
- De update-controle vraagt alleen de nieuwste release op bij GitHub, zonder gegevens over jou. Een nieuwe versie wordt pas gedownload als je op Nu bijwerken klikt, en alleen geïnstalleerd als het SHA-256-controlegetal van GitHub klopt.
- Crasht DonkiePane, dan komt de foutmelding in `%APPDATA%\DonkiePane\crash.log`. Dat bestand blijft op je pc; er wordt niets verstuurd.
- Gaat er iets mis en blijven je Windows-iconen verborgen? Rechtsklik op het bureaublad → **Beeld** → **Bureaubladpictogrammen weergeven**.
