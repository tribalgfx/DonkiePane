# Takenbronnen en plug-ins

DonkiePane is één algemene app. Een takenpane haalt zijn taken uit een **bron**. Lokale taken zijn altijd
beschikbaar en staan alleen op deze pc. Alle andere bronnen voeg je zelf toe in **Instellingen → Takenbronnen**.
Er is geen enkele bron ingebouwd en er staat geen enkel adres of voorbeeld van iemands eigen dashboard in de app.

Alles wat je instelt, staat per gebruiker op die ene pc in `%APPDATA%\DonkiePane`: `sources.json` (de
verbindingen, zonder geheimen) en `secrets\` (tokens en secrets, versleuteld met Windows DPAPI: alleen jouw
Windows-account op deze pc kan ze lezen). Er wordt niets gedeeld en niets in git gezet.

## Bronnen die meekomen

| Soort | Waarvoor |
|---|---|
| **Taken via REST (Tasks REST v1)** | Een dashboard of dienst die het protocol hieronder spreekt. Jij vult adres, token en eventueel een vingerafdruk in. |
| **Google Taken** | Je eigen Google Taken. Je maakt zelf een Google-koppeling (zie verderop). |

Een bron toevoegen: **Toevoegen…**, kies het soort, vul de gegevens in, **Verbinding testen**, **Opslaan**.
Daarna kies je bij **Nieuw takenpane** (tray-icoon) de bron en, als die meer lijsten heeft, de lijst.

Verwijder je een pane van een gekoppelde bron, dan blijven je taken bij de bron staan. Alleen bij lokale
taken verdwijnen ze met het pane (daar vraagt DonkiePane eerst).

## Het protocol "Tasks REST v1"

Elke dienst die dit spreekt, werkt met de REST-bron. Alleen HTTPS; elk verzoek heeft `Authorization: Bearer <token>`.

| Verzoek | Doet |
|---|---|
| `GET {adres}/api/v1/tasks` | `{ revision, tasks: [{ id, text, done, dueDate, createdAt }] }`. Ook als `ETag`; met `If-None-Match` volgt `304` zolang er niets veranderd is. |
| `POST {adres}/api/v1/tasks` | `{ text, dueDate?, done?, id? }`. Met een eigen `id` is herhalen veilig. |
| `PATCH {adres}/api/v1/tasks/{id}` | `text`, `done` en/of `dueDate` (`null` wist de deadline). |
| `DELETE {adres}/api/v1/tasks/{id}` | Verwijdert de taak. |

`dueDate` is `YYYY-MM-DD` of `null`, `createdAt` is Unix-tijd in milliseconden, tekst maximaal 256 tekens.
Het protocol kent geen volgorde, geen "belangrijk" en maar één lijst per token; DonkiePane sorteert zelf
(eerst wat een deadline heeft, dan de nieuwste) en toont geen ster.

Foutcodes: `400` ongeldig, `401` token fout of ingetrokken, `403` geen toegang, `404` taak bestaat niet (meer),
`429` te veel pogingen (met `Retry-After`).

**Hoe DonkiePane zich hierbij gedraagt:** na een `401` stopt het met verzoeken tot jij het token verbeterd hebt,
want een bron kan na een paar mislukte pogingen alles blokkeren, ook met een geldig token. Bij `429` wacht het de
opgegeven tijd. Elke minuut vraagt het alleen of de revisie veranderd is.

### Zelfondertekend certificaat: de vingerafdruk

Een dienst in je eigen netwerk heeft vaak een zelfondertekend certificaat. Zet de controle dan **niet** uit,
maar vul de **SHA-256-vingerafdruk** van het certificaat in (`AA:BB:CC:…`). DonkiePane accepteert dan precies
dat certificaat en geen ander. Zonder vingerafdruk geldt de gewone certificaatcontrole van Windows.

Het adres moet met `https://` beginnen; een token gaat nooit onversleuteld over het netwerk.

## Google Taken instellen

Er wordt bewust geen client-ID van DonkiePane meegeleverd. Je maakt zelf een koppeling, gratis en zonder
betaalaccount (voor zover ik heb kunnen nagaan; Google vraagt er bij het aanzetten van de Tasks API niet om):

1. Maak in [Google Cloud](https://console.cloud.google.com) een project en zet de **Google Tasks API** aan.
2. Open **Google Auth Platform** en stel het toestemmingsscherm in:
   - Heb je een **Workspace**-organisatie en maak je het project daarbinnen, kies dan doelgroep **Intern**.
     Dan geldt de beperking van een week niet en is geen verificatie nodig.
   - Ben je een gewone gebruiker, kies dan **Extern** en zet de status op **In production**. Laat je het op
     *Testing* staan, dan verlopen de inlogtokens volgens Google na 7 dagen en moet je elke week opnieuw inloggen.
3. Voeg bij **Data Access** alleen het recht `https://www.googleapis.com/auth/tasks` toe.
4. Maak bij **Clients** een client van het type **Desktop app** en bewaar het client-ID en het secret.
5. In DonkiePane: **Instellingen → Takenbronnen → Toevoegen → Google Taken**, vul client-ID en secret in en kies
   **Inloggen bij Google**. Je wachtwoord vul je alleen bij Google zelf in, in je browser.

Een Workspace-beheerder kan externe apps blokkeren. Krijg je een melding met `403`, vraag de beheerder dan de
client toe te staan (Admin-console → Beveiliging → API-controles).

## Eigen plug-ins

Bronnen zijn plug-ins: aparte assemblies die de app laadt uit
- de map `plugins` naast `DonkiePane.exe`, en
- `%APPDATA%\DonkiePane\plugins`.

Elke plug-in staat in een eigen submap en heet `DonkiePane.Plugins.<Naam>.dll`. Een plug-in implementeert
`ITaskSourcePlugin` (uit `DonkiePane.Tasks`): welke velden de gebruiker invult, hoe een verbinding wordt aangemaakt,
en een `ITaskProvider` met de taken. De app tekent het instellingenformulier zelf en bewaart geheimen veilig,
dus een plug-in heeft geen eigen schermen of opslag nodig.

**Veiligheid:** een plug-in is gewone code en draait met jouw rechten. Zet er alleen in wat je vertrouwt. DonkiePane
laadt nooit iets van internet en nooit uit een andere map. Een kapotte of ontbrekende plug-in wordt gemeld in
Instellingen; de app gaat gewoon door.
