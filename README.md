<p align="center">
  <img src="src/DonkiePane.App/Assets/DonkiePane-256.png" alt="DonkiePane" width="128" />
</p>

# DonkiePane

<p align="center"><b>English</b> · <a href="README.nl.md">Nederlands</a></p>

Panes on your Windows desktop for your shortcuts and your tasks. With a calm look and a nod to the checks of a tea towel.

## What it does

- **Panes on the desktop.** They sit behind your windows and stay put when you press Win+D.
- **Your desktop icons in panes.** Whatever you do with a file or shortcut on the desktop, you can do in a pane too: open, select (Ctrl, Shift, drag a selection box), drag to File Explorer, an app, a folder or the Recycle Bin, drag files into a pane, Delete, F2, Ctrl+C/X/V, Ctrl+Shift+N, Alt+Enter and the File Explorer menu (right-clicking an empty spot gives Paste and New). Drop icons on an empty spot of the desktop and a new pane appears there with them. Windows carries it all out, so confirmations and the Recycle Bin work as always; DonkiePane never moves or deletes anything by itself. New files land in the Inbox pane, or in the pane you pasted or dropped them in.
- **View and sorting per pane.** Large icons, small icons or a list, sorted in your own order, by name, by type or by date modified (right-click the title bar). Make the icons larger or smaller with Ctrl+scroll wheel, and hide the names under the icons if you like (the name then shows on hover). Start typing in a pane to filter its icons by name; Esc clears the filter.
- **Gray out icons you no longer use.** Desktop icons you haven't opened from a pane or changed for a month, three months or six months turn gray (Settings → Panes; off by default).
- **Task panes.** Add, check off, reorder and star tasks. Local, or synced with a source you add yourself:
  - Google Tasks
  - Home Assistant (to-do lists)
  - a service of your own that speaks the Tasks REST v1 protocol

  What you do offline is kept and sent later. See [docs/Sources.md](docs/Sources.md). Tasks due today give a notification next to the clock (once per task per day; can be turned off in Settings).
- **Folder panes.** A pane that shows the contents of a folder of your choice (New folder pane in the menu next to the clock). Double-click a folder to look inside, Backspace goes back; typing searches that folder and the folders below it.
- **Tabs.** Drag a pane by its title bar onto the title bar of another pane and hold for a moment: it becomes a tab. Pull a tab out of the title bar to detach it again. Files you hold over a tab open that tab.
- **Rules.** New files on the desktop go straight to the right pane, by extension, by name or because they are a folder (Settings → Rules). What is already in the Inbox can be sorted there in one go.
- **Peek into rolled-up panes.** A rolled-up pane opens on hover or with a single click and rolls up again when the mouse leaves (Settings → Panes).
- **Bring panes to the front.** A shortcut (Ctrl+Alt+Space by default, configurable) brings all panes above your windows; press it again, press Esc or click beside them to put them back.
- **Calm and customizable.** Plain themes, light or dark (or like Windows), frosted glass (your desktop background blurred behind the pane), your own color and transparency per pane, rounded or square corners, a grid to snap panes to, and an optional tea-towel check pattern.
- **Double-click an empty spot on the desktop** to hide all panes; double-click again to bring them back.
- **English and Dutch.** DonkiePane follows the language of Windows; you can choose in Settings → Language. Translations live in `src/DonkiePane.Core/Localization`.
- **Export and import your layout.** Settings → Data saves your panes and where they are as a file, as a backup or to move them to another PC. Tasks, sources and tokens are not included.
- **Multiple screens.** If a screen disappears, its panes move to another screen and go back as soon as the screen returns.

## Installing

Download `DonkiePane-Setup-<version>.exe` from the [releases](https://github.com/tribalgfx/DonkiePane/releases) and run it. No .NET and no administrator rights are needed; the program goes into `%LOCALAPPDATA%\Programs\DonkiePane`. Windows may show a SmartScreen warning because the installer isn't signed: choose **More info** and then **Run anyway**.

On first start, a welcome screen explains what happens to your desktop and lets you choose whether to hide the Windows icons. DonkiePane tells you when there is a new version and updates when you click (or run a new installer over it); uninstall through Installed apps. See [docs/Installing.md](docs/Installing.md).

## Building it yourself

You need Windows 10 or 11. The scripts check whether the .NET SDK (see `global.json`) and, for the installer, Inno Setup are present, and install what's missing with winget. Windows may ask for permission.

```powershell
.\scripts\install.ps1            # builds and installs to %LOCALAPPDATA%\Programs\DonkiePane
.\scripts\build-installer.ps1    # creates artifacts\DonkiePane-Setup-<version>.exe
dotnet test                      # the tests (the .NET SDK must already be installed)
```

If you only use DonkiePane, you don't need any of this: the installer contains everything, .NET included.

## Structure

| Folder | Contents |
|---|---|
| `src/DonkiePane.App` | WPF: panes, tray icon, settings, themes |
| `src/DonkiePane.Core` | Model and logic: panes, items, grid, storage, translations |
| `src/DonkiePane.Shell` | Win32 and the Windows shell: desktop layer, icons, drag and drop, menus |
| `src/DonkiePane.Tasks` | Contract for task sources, plug-in loader, local storage, offline queue |
| `src/DonkiePane.Plugins.*` | The sources themselves: Google Tasks, REST, Home Assistant |
| `installer`, `scripts` | Installer and helper scripts |
| `tests` | Tests for Core, Tasks and App |

The design and the choices behind it are in [PLAN.md](PLAN.md) (in Dutch). Code comments are in Dutch as well; texts shown to users go through `Loc.T("…")` and have an English translation.

## Data and security

- Settings, panes and tasks are stored per user in `%APPDATA%\DonkiePane`. Secrets (tokens, passwords of sources) are stored there encrypted with Windows (DPAPI), never in a plain file or in git.
- Connections to sources use HTTPS only (Home Assistant over http only after an explicit choice). A source can be pinned to a certificate fingerprint.
- Plug-ins are only loaded from the `plugins` folder next to the program and from `%APPDATA%\DonkiePane\plugins`, never from the internet. A plug-in in that second folder only loads after you allow it in Settings.
- An imported layout never makes DonkiePane look at a network folder: folder panes from the file keep only folders on this PC.
- The update check only asks GitHub for the latest release, without any data about you. A new version is only downloaded when you click Update now, and only installed if GitHub's SHA-256 checksum matches.
- If DonkiePane crashes, the error goes into `%APPDATA%\DonkiePane\crash.log`. That file stays on your PC; nothing is sent.
- Something went wrong and your Windows icons stay hidden? Right-click the desktop → **View** → **Show desktop icons**.

## License

Copyright (C) 2026 tribalgfx

DonkiePane is free software: you can redistribute it and/or modify it under the terms of the
[GNU General Public License](LICENSE) as published by the Free Software Foundation, either version 3 of the License,
or (at your option) any later version. In short: you may use, change and share it, also commercially, as long as
whatever you distribute stays open under the same license, with its source code. DonkiePane comes without any
warranty.
