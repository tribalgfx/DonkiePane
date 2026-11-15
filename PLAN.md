# DonkiePane — plan

Aanpasbare vakken ("panes", naar de ruitjes van een theedoek) op het Windows-bureaublad, met snelkoppelingen, je bureaubladiconen en taken.

## Uitgangspunten

- **Panes staan óp het bureaublad**: altijd zichtbaar op de achtergrond, achter je gewone vensters, en ze blijven staan bij Win+D / "bureaublad weergeven".
- **Bureaubladiconen komen in panes.** DonkiePane neemt de weergave van het bureaublad over:
  - De bestanden blijven staan waar ze staan (`%USERPROFILE%\Desktop` en `C:\Users\Public\Desktop`). DonkiePane verplaatst niets en kan dus niets kwijtraken.
  - De eigen iconenweergave van Windows wordt verborgen. DonkiePane tekent de iconen in de panes.
  - Een **Inbox-pane** vangt nieuwe bestanden op die op het bureaublad verschijnen, zodat niets onzichtbaar wordt.
  - Bij afsluiten of verwijderen van DonkiePane komt de Windows-iconenweergave terug.
- **Taken zijn lokaal of gekoppeld** aan Google Taken (werk) en later een eigen dashboard (thuis), per pane te kiezen.
- **Eigen privé-repo op GitHub.**

## Eigen draai

- **Ruitjesraster:** panes klikken vast op een raster; bij verslepen licht het raster op.
- **Theedoek-thema's:** randen/achtergronden met ruit- of streeppatroon, kleuren per pane.
- **Oprollen tot titelbalk:** dubbelklik op de titel.
- **Soorten panes:** iconen/snelkoppelingen, taken; later notities, live-map, klok.

## Techniek

C# met .NET (LTS) en WPF.

```
DonkiePane/
├─ src/DonkiePane.App/        WPF: tray-icoon, pane-vensters, thema's
├─ src/DonkiePane.Core/       Model en logica: Pane, items, raster, opslag
├─ src/DonkiePane.Shell/      Win32: bureaubladlaag, iconen, .lnk, desktopweergave verbergen
├─ src/DonkiePane.Tasks/      ITaskProvider + LocalTaskProvider + GoogleTaskProvider (later een eigen dashboard)
└─ tests/DonkiePane.Core.Tests/
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
| 7. Koppelingen | Google Taken (klaar), dashboard-provider (nog te doen) | Taken synchroniseren |

## Risico's

- **Bureaubladlaag:** geen officiële API; via de `WorkerW`/`Progman`-truc. Geïsoleerd in `DonkiePane.Shell` omdat Windows-updates dit kunnen veranderen. Vroeg testen (fase 2).
- **Windows-iconen verbergen:** als DonkiePane crasht, moet de gebruiker zijn bureaublad terugkrijgen. Herstel bij afsluiten, bij crash (handler) en via een tray-optie.
- **Meerdere monitoren / DPI:** posities per monitor en relatief opslaan, anders vallen panes buiten beeld.

## Takenbronnen (fase 7)

Elk takenpane kiest zijn eigen bron; taken en layout staan per pc in `%APPDATA%\DonkiePane`.

- **Lokaal:** klaar (fase 4).
- **Google Taken (werk-pc): klaar.** Eigen OAuth-client (Desktop app) in een intern Cloud-project van de Workspace. Inloggen via de browser met PKCE en een loopback-adres; het inlogtoken staat met DPAPI versleuteld in `google.token`, client-ID en secret in `google.json` (beide buiten git). Wijzigingen gaan direct naar Google, elke 60 s wordt gekeken of er buiten DonkiePane iets veranderd is. Google kent geen "belangrijk" en geen tijdstip bij een deadline, dus daar is geen ster. Een Google-pane verwijderen laat de lijst bij Google staan.
- **Een eigen dashboard (thuis): nog te doen.** Daarvoor is een API met een eigen toegangssleutel nodig.
- **Microsoft To Do: vervallen.** Een persoonlijk hotmail-account heeft geen Entra-tenant en kan dus geen app registreren.

Bekend en nog niet gedaan: offline wijzigingen worden niet in een wachtrij bewaard (een mislukte wijziging geeft een melding en de lijst wordt opnieuw opgehaald), en subtaken van Google worden vlak getoond.

## Open punten

- Hoe een eigen dashboard taken aanbiedt (daar is een API voor nodig).
