# DonkiePane — plan

Aanpasbare vakken ("panes", naar de ruitjes van een theedoek) op het Windows-bureaublad, met snelkoppelingen, je bureaubladiconen en taken.

## Uitgangspunten

- **Panes staan óp het bureaublad**: altijd zichtbaar op de achtergrond, achter je gewone vensters, en ze blijven staan bij Win+D / "bureaublad weergeven".
- **Bureaubladiconen komen in panes.** DonkiePane neemt de weergave van het bureaublad over:
  - De bestanden blijven staan waar ze staan (`%USERPROFILE%\Desktop` en `C:\Users\Public\Desktop`). DonkiePane verplaatst niets en kan dus niets kwijtraken.
  - De eigen iconenweergave van Windows wordt verborgen. DonkiePane tekent de iconen in de panes.
  - Een **Inbox-pane** vangt nieuwe bestanden op die op het bureaublad verschijnen, zodat niets onzichtbaar wordt.
  - Bij afsluiten of verwijderen van DonkiePane komt de Windows-iconenweergave terug.
- **Taken zijn lokaal of gekoppeld** aan een bron die de gebruiker zelf toevoegt (Google Taken, of elk dashboard dat het protocol Tasks REST v1 spreekt), per pane te kiezen. Er is geen bron ingebouwd.
- **Eigen repo op GitHub,** sinds 15 november 2026 publiek, zodat de update-controle de releases zonder token kan lezen.

## Eigen draai

- **Ruitjesraster:** panes klikken vast op een raster; bij verslepen licht het raster op.
- **Rustige thema's:** een effen antracietkleurige pane is de standaard (verder Blauw, Groen, Rood en Mosterd). Per pane kun je via het submenu Thema een eigen kleur en de transparantie instellen. Het theedoek-ruitpatroon blijft bestaan als keuze (Ruitpatroon), niet als standaard: te druk voor dagelijks gebruik.
- **Dubbelklik op het bureaublad:** op een leeg stuk bureaublad verbergt alle panes met hun inhoud, nog een keer toont ze weer (uit te zetten in Instellingen).
- **Eigen icoon:** in de stijl van de andere Donkie-apps (tegel met verloop, wit glyph), gemaakt met `scripts\make-icon.ps1`.
- **Oprollen tot titelbalk:** dubbelklik op de titel.
- **Soorten panes:** iconen/snelkoppelingen, taken; later notities, live-map, klok.

## Techniek

C# met .NET (LTS) en WPF.

```
DonkiePane/
├─ src/DonkiePane.App/        WPF: tray-icoon, pane-vensters, thema's
├─ src/DonkiePane.Core/       Model en logica: Pane, items, raster, opslag
├─ src/DonkiePane.Shell/      Win32: bureaubladlaag, iconen, .lnk, desktopweergave verbergen
├─ src/DonkiePane.Tasks/      ITaskProvider, plug-in-contract, lader en LocalTaskProvider
├─ src/DonkiePane.Plugins.*/  de bronnen als plug-ins (Google Taken, REST, Home Assistant)
├─ installer/                 Inno Setup-script voor Setup.exe
├─ scripts/                   install, uninstall, build-installer, make-icon
└─ tests/                     Core, Tasks en App
```

- **Opslag:** JSON in `%APPDATA%\DonkiePane\` (layout + taken), met automatische back-ups.
- **Bureaubladitems:** per item het pad (relatief aan de bureaubladmap) en de pane waarin het staat. Een `FileSystemWatcher` volgt nieuwe, hernoemde en verwijderde bestanden.
- **Taken klaar voor synchronisatie:** elke taak krijgt een vaste ID, `createdAt`/`updatedAt`, een `source` en een `externalId`. Alle toegang loopt via `ITaskProvider`, zodat een koppeling later alleen een nieuwe provider is.

## Fases

| Fase | Wat | Klaar als |
|---|---|---|
| 0. Opzet | .NET SDK, solution, git-repo, privé GitHub-repo | Er start een leeg venster |
| 1. Eerste pane | Randloos, versleepbaar, aanpasbare grootte; positie bewaard | Pane staat na herstart op dezelfde plek |
| 2. Op het bureaublad | Pane in de bureaubladlaag; overleeft Win+D; achter vensters | Gedraagt zich als een echte bureaubladlaag |
| 3. Iconen in panes | Bureaubladmappen lezen, iconen tonen, openen met dubbelklik, slepen tussen panes, Inbox-pane, Windows-iconen verbergen en terugzetten | Je echte bureaublad zit in panes |
| 4. Taakpane | Taken toevoegen, afvinken, bewerken, ordenen, via `LocalTaskProvider` | Dagelijks bruikbaar |
| 5. Ruitjes-identiteit | Raster met snappen, theedoek-thema's, oprollen | Het voelt als DonkiePane |
| 6. Afwerking | Starten met Windows, meerdere monitoren, DPI, instellingen, installer | Kan dagelijks draaien |
| 7. Takenbronnen als plug-ins | Contract + lader, bronnenbeheer in Instellingen, generieke REST-bron, Google Taken, Home Assistant, instelbare synchronisatie met verversknop, offline wachtrij, subtaken | Elke gebruiker voegt zelf bronnen toe (klaar) |
| 8. Uiterlijk en gebruik | Rustige thema's met eigen kleur en transparantie, dubbelklik om panes te verbergen, eigen icoon | Ziet er zakelijk uit en voelt als een eigen programma (klaar) |
| 9. Voor andere gebruikers | Zelfstandige installer (Inno Setup), welkomstscherm bij de eerste start, netjes afsluiten voor updates | Iemand anders installeert zonder .NET of beheerdersrechten (klaar) |

## Risico's

- **Bureaubladlaag:** geen officiële API; via de `WorkerW`/`Progman`-truc. Geïsoleerd in `DonkiePane.Shell` omdat Windows-updates dit kunnen veranderen. Vroeg testen (fase 2).
- **Windows-iconen verbergen:** als DonkiePane crasht, moet de gebruiker zijn bureaublad terugkrijgen. Herstel bij afsluiten, bij crash (handler) en via een tray-optie.
- **Meerdere monitoren / DPI:** posities per monitor en relatief opslaan, anders vallen panes buiten beeld.

## Takenbronnen (fase 7)

Zie [docs/Bronnen.md](docs/Bronnen.md) voor het gebruik. Samengevat:

- **Eén algemene app.** Er is geen bron ingebouwd en er staat geen adres of naam van iemands eigen dashboard in de app. Wat een gebruiker gebruikt, vult hij zelf in.
- **Bronnen zijn plug-ins** (`DonkiePane.Plugins.*.dll`) uit `plugins\` naast het programma en uit `%APPDATA%\DonkiePane\plugins`. Alleen daaruit, nooit van internet; die uit `%APPDATA%` pas nadat je ze hebt toegestaan (afdruk van hun DLLs in `plugins-allowed.dat`, versleuteld met DPAPI). Een plug-in beschrijft zijn invoervelden; de app tekent het formulier en bewaart geheimen versleuteld (DPAPI, `secrets\`), nooit in `sources.json`.
- **Meegeleverd:** Taken via REST (Tasks REST v1, met optionele vastgepinde certificaat-vingerafdruk), Google Taken (de gebruiker maakt zelf een OAuth-client; er wordt geen client-ID meegeleverd) en Home Assistant (to-do lijsten; http alleen na een uitdrukkelijke keuze).
- **Synchronisatie:** instelbaar interval (standaard 60 s, 15 s tot 5 min) en een verversknop. Een wachtrij om elke gekoppelde bron bewaart wat je offline doet, in een bestand per verbinding, en verstuurt het in volgorde zodra het kan.
- **Gedrag naar de bron toe:** na een 401 stopt DonkiePane met verzoeken (een bron kan na een paar mislukte pogingen alles blokkeren), bij 429 wacht het de opgegeven tijd, elke minuut volstaat één vraag met de revisie (304).
- **Microsoft To Do: vervallen.** Een persoonlijk Microsoft-account heeft geen Entra-tenant en kan dus geen app registreren.

Bekende beperkingen: taken verplaatsen wordt niet offline bewaard; een taak die offline wordt aangemaakt en meteen afgevinkt komt open aan (met een melding); bij Home Assistant kan een toevoeging die precies tijdens een netwerkfout gebeurt dubbel aankomen; of Home Assistant een deadline wist met `due_date: null` staat niet in de documentatie en is niet met een echte Home Assistant geprobeerd.

## Distributie (fase 9)

Zie [docs/Installeren.md](docs/Installeren.md) voor installeren, bijwerken en verwijderen. Samengevat:

- `scripts\build-installer.ps1` publiceert zelfstandig (met .NET erin, ongeveer 50 MB) en laat Inno Setup `artifacts\DonkiePane-Setup-<versie>.exe` maken. Per gebruiker, geen beheerdersrechten, een snelkoppeling in het Startmenu en verwijderen via Apps en functies.
- Een update kan eroverheen: het setup-programma vraagt een draaiende DonkiePane met `DonkiePane.exe --quit` netjes af te sluiten (dan komen de Windows-bureaubladiconen terug) en laat panes, taken en instellingen staan.
- Bij de eerste start (er is nog geen `layout.json`) komt een welkomstscherm met twee keuzes: de Windows-iconen verbergen en starten met Windows. Pas daarna verandert er iets aan het bureaublad.
- Bij het verwijderen wordt gevraagd of je gegevens ook weg moeten (standaard niet). Je bureaubladbestanden blijven altijd staan.
- **Controle op updates:** een minuut na het starten en daarna eens per dag vraagt DonkiePane zonder token `api.github.com/repos/tribalgfx/DonkiePane/releases/latest` op (kan zonder token omdat de repo publiek is; bij 404 is er "nog geen versie"). Concepten en voorlopige versies tellen niet mee; de versie komt uit de tag (`v0.2.0`). Eén ballonmelding per nieuwe versie, een menu-item zolang hij er is, en een schakelaar en knop in Instellingen → Updates.
- **Bijwerken alleen na een klik:** de installer moet `DonkiePane-Setup-<versie van de tag>.exe` heten, uit de releases van deze repo op `github.com` komen en een `sha256`-digest van GitHub hebben; zonder digest biedt DonkiePane alleen de releasepagina aan. De download komt in een eigen map met een willekeurige naam, mag niet groter zijn dan GitHub opgeeft (en nooit groter dan 300 MB of langer duren dan 15 minuten), en vlak voor het starten wordt het controlegetal opnieuw berekend. Na de controle start de installer met `/SILENT /NORESTART /STARTAFTER=1`; de installer sluit DonkiePane met `--quit` en start hem daarna weer (`StartAfterUpdate` in het Inno-script). Het controlegetal beschermt tegen een kapotte of verwisselde download, niet tegen iemand die de GitHub-account zelf overneemt; daarvoor is ondertekenen nodig. Op 27-11-2026 echt geprobeerd van 0.5.0 naar 0.5.1: de installer sloot de oude versie, startte de nieuwe, werkte de plug-ins mee bij en liet panes, bronnen en instellingen staan.
- **Een release publiceren:** versie ophogen in `Directory.Build.props`, `scripts\build-installer.ps1`, en dan `gh release create v<versie> artifacts\DonkiePane-Setup-<versie>.exe`. De tag moet met de versie overeenkomen, anders ziet de update-controle het verkeerde nummer.

## Open punten

- De Home Assistant-bron proberen met een echte Home Assistant (door de gebruiker, met een eigen token).
- De installer is niet digitaal ondertekend: Windows kan een SmartScreen-waarschuwing tonen. Ondertekenen kan met een code-signing-certificaat.
- De app is alleen in het Nederlands.
- Het welkomstscherm en de installer zijn getest met stille installaties en een gerenderde afbeelding, nog niet met een echte nieuwe gebruiker op een schone pc.
