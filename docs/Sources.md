# Task sources and plug-ins

**English** · [Nederlands](Bronnen.md)

DonkiePane is one general app. A task pane gets its tasks from a **source**. Local tasks are always available and
live only on this PC. You add every other source yourself in **Settings → Task sources**. No source is built in,
and the app contains no address or example of anyone's own dashboard.

Everything you set up is stored per user on that one PC in `%APPDATA%\DonkiePane`: `sources.json` (the
connections, without secrets) and `secrets\` (tokens and secrets, encrypted with Windows DPAPI: only your Windows
account on this PC can read them). Nothing is shared and nothing goes into git.

## Sources that come with DonkiePane

| Kind | For |
|---|---|
| **Tasks via REST (Tasks REST v1)** | A dashboard or service that speaks the protocol below. You fill in the address, token and optionally a fingerprint. |
| **Google Tasks** | Your own Google Tasks. You create a Google connection yourself (see below). |
| **Home Assistant (to-do lists)** | The to-do lists from your own Home Assistant. You create an access token there (see below). |

To add a source: **Add…**, choose the kind, fill in the details, **Test connection**, **Save**.
Then choose the source (and, if it has several lists, the list) under **New task pane** in the tray icon.

If you remove a pane of a connected source, your tasks stay at the source. Only with local tasks do they disappear
with the pane (DonkiePane asks first).

## The "Tasks REST v1" protocol

Any service that speaks this works with the REST source. HTTPS only; every request carries `Authorization: Bearer <token>`.

| Request | Does |
|---|---|
| `GET {address}/api/v1/tasks` | `{ revision, tasks: [{ id, text, done, dueDate, createdAt }] }`. Also as `ETag`; with `If-None-Match` you get `304` as long as nothing changed. |
| `POST {address}/api/v1/tasks` | `{ text, dueDate?, done?, id? }`. With your own `id`, retrying is safe. |
| `PATCH {address}/api/v1/tasks/{id}` | `text`, `done` and/or `dueDate` (`null` clears the due date). |
| `DELETE {address}/api/v1/tasks/{id}` | Deletes the task. |

`dueDate` is `YYYY-MM-DD` or `null`, `createdAt` is Unix time in milliseconds, text at most 256 characters.
The protocol has no order, no "important" and only one list per token; DonkiePane sorts by itself
(first what has a due date, then the newest) and shows no star.

Error codes: `400` invalid, `401` token wrong or revoked, `403` no access, `404` task doesn't exist (anymore),
`429` too many attempts (with `Retry-After`).

**How DonkiePane behaves:** after a `401` it stops making requests until you've fixed the token, because a source
may block everything after a few failed attempts, even with a valid token. On `429` it waits the given time.
Every minute it only asks whether the revision changed.

### Self-signed certificate: the fingerprint

A service on your own network often has a self-signed certificate. Don't turn the check **off**; instead enter the
**SHA-256 fingerprint** of the certificate (`AA:BB:CC:…`). DonkiePane then accepts exactly that certificate and no
other. Without a fingerprint, the normal certificate check of Windows applies.

The address must start with `https://`; a token never travels over the network unencrypted.

## Setting up Google Tasks

DonkiePane deliberately ships without a client ID. You create a connection yourself, free and without a billing
account (as far as I could check; Google doesn't ask for one when you turn on the Tasks API):

1. In [Google Cloud](https://console.cloud.google.com), create a project and turn on the **Google Tasks API**.
2. Open **Google Auth Platform** and set up the consent screen:
   - If you have a **Workspace** organization and create the project inside it, choose audience **Internal**.
     Then the one-week limit doesn't apply and no verification is needed.
   - If you're a regular user, choose **External** and set the status to **In production**. If you leave it on
     *Testing*, Google says the sign-in tokens expire after 7 days and you have to sign in again every week.
3. Under **Data Access**, add only the scope `https://www.googleapis.com/auth/tasks`.
4. Under **Clients**, create a client of type **Desktop app** and keep the client ID and secret.
5. In DonkiePane: **Settings → Task sources → Add → Google Tasks**, fill in the client ID and secret and choose
   **Sign in to Google**. You only enter your password at Google itself, in your browser.

A Workspace administrator can block external apps. If you get a message with `403`, ask the administrator to allow
the client (Admin console → Security → API controls).

## Setting up Home Assistant

1. In Home Assistant, create a **long-lived access token**: your profile, **Security** tab, at the bottom.
2. In DonkiePane: **Settings → Task sources → Add → Home Assistant (to-do lists)**.
3. Fill in the address (for example `https://ha.example.com`) and paste the token.
4. If your Home Assistant runs on your own network **without https** (the default is `http://…:8123`), turn on **Allow
   unencrypted connection (http)**. The token then travels over your network unencrypted: only do this for an address
   inside your home, never for an address outside it. Without that checkbox, DonkiePane refuses an http address.
5. Choose **Test connection**. Then choose the list you want to show under **New task pane**; each pane shows one list.

What Home Assistant doesn't know, DonkiePane doesn't know here either: no order (first what has a due date, then the
rest in Home Assistant's order), no "important", and a due date is only a date (a time is dropped). There's no quick
check like with the REST protocol: every round the list is fetched and compared, which costs nothing on your own
network. Clearing a due date sends `due_date: null`; whether Home Assistant accepts that isn't documented.

## Syncing and working offline

- **From other devices to DonkiePane:** DonkiePane checks every 60 seconds whether something changed. You set this in
  **Settings → Synchronization** (15 seconds to 5 minutes) and it applies right away. The **↻** button next to a pane's
  input box checks immediately.
- **From DonkiePane to the source:** right away.
- **No connection or a struggling source:** you see the last known tasks with a message. That also applies to a server
  error (for example a proxy returning 502 while the service restarts) or a response that isn't tasks (such as a proxy's
  sign-in page). Whatever you add, change, check off or delete then stays visible right away, is kept (in
  `%APPDATA%\DonkiePane\queue`, so also after a restart) and goes to the source in the same order as soon as it's
  reachable again.
- **What isn't kept:** moving tasks (a position means nothing anymore once the list has changed), and a change the
  source refuses for good (for example for a task deleted elsewhere): that one is dropped, with a message. A refused
  token keeps the queue until you fix it.
- **Note:** a task you create offline and check off right away arrives open at the source; DonkiePane tells you so. And
  a Home Assistant that drops out exactly while adding can end up with a duplicate task, because that source has no id
  of its own.
- **Subtasks** from Google Tasks are indented under their parent task. Moving stays within the same parent task.

## Your own plug-ins

Sources are plug-ins: separate assemblies the app loads from
- the `plugins` folder next to `DonkiePane.exe`, and
- `%APPDATA%\DonkiePane\plugins`.

Each plug-in sits in its own subfolder and is called `DonkiePane.Plugins.<Name>.dll`. A plug-in implements
`ITaskSourcePlugin` (from `DonkiePane.Tasks`): which fields the user fills in, how a connection is created, and an
`ITaskProvider` with the tasks. The app draws the settings form itself and stores secrets safely, so a plug-in
needs no screens or storage of its own. Texts shown to users go through `Loc.T("…")`; add their translations to
`src/DonkiePane.Core/Localization/en.json`.

**Security:** a plug-in is ordinary code and runs with your rights. Only put in what you trust. DonkiePane never
loads anything from the internet or from any other folder. A new or changed plug-in in `%APPDATA%\DonkiePane\plugins`
is not loaded until you allow it: you get a notification, and Settings → Task sources has an **Allow plug-ins…** button
(DonkiePane restarts afterwards). Any program running as you can write to that folder, so this way nothing runs there
unnoticed. A broken or missing plug-in is reported in Settings; the
app carries on.
