# Installing, updating and uninstalling DonkiePane

**English** · [Nederlands](Installeren.md)

## For people who use DonkiePane

1. Double-click `DonkiePane-Setup-<version>.exe`.
   - No administrator and no .NET needed. DonkiePane goes into `%LOCALAPPDATA%\Programs\DonkiePane`.
   - Windows may show a SmartScreen warning, because the installer isn't digitally signed. Choose **More info** and then **Run anyway**.
   - The installer speaks English or Dutch, following the language of Windows.
2. Let DonkiePane start at the end. The very first time, a welcome screen offers two choices:
   - **Hide the Windows desktop icons.** DonkiePane puts your icons in a pane called Inbox. Your files stay exactly where they are; nothing is moved or deleted. If you turn this off, you'll see the icons twice.
   - **Start with Windows.**
3. You add task sources (Google Tasks, Home Assistant, a service of your own) yourself in Settings, under Task sources. See [Sources.md](Sources.md).

DonkiePane follows the language of Windows (Dutch on a Dutch Windows, English otherwise). You can choose another language in **Settings → Language**; it applies after a restart, and there's a **Restart now** button for that.

### Updating

DonkiePane checks for a new version itself: a minute after starting and then once a day, in the releases on GitHub. If there is one, you get a single notification next to the clock, and the tray menu shows **Update to version …** at the top. There you see what's new and choose **Update now**:

1. DonkiePane downloads the installer and verifies it against the SHA-256 checksum GitHub lists with the release. If it doesn't match, the file is deleted.
2. The installer runs without questions (you only see the progress), closes DonkiePane cleanly, replaces the files and then starts DonkiePane again.

Nothing is downloaded or installed unless you click Update now. You can turn the automatic check off in **Settings → Updates**; you can also check right away there. Checking sends nothing about you or your PC, only a request for the latest release.

You can also update yourself: run the new `DonkiePane-Setup-<version>.exe`. The installer asks a running DonkiePane to close cleanly (the Windows desktop icons briefly come back), replaces the files and keeps your panes, tasks and settings.

### Uninstalling

Through **Settings → Apps → Installed apps → DonkiePane → Uninstall**.

- DonkiePane first closes cleanly, so the Windows desktop icons come back.
- "Start with Windows" is removed.
- The uninstaller asks whether your panes, tasks and settings (in `%APPDATA%\DonkiePane`) should go too. By default they stay, also with a silent uninstall. The saved tokens of task sources are part of that data.
- Your desktop files always stay.

### Icons stay hidden

If DonkiePane was ever killed (for example through Task Manager) and your Windows icons stay away, bring them back by right-clicking the desktop → **View** → **Show desktop icons**.

## For people who build the installer

You need the .NET SDK (see `global.json`) and Inno Setup 6. The script checks both and installs what's missing with winget (`scripts\prerequisites.ps1`); Windows may ask for permission. If the PC has no winget, the script tells you what to install yourself.

```powershell
.\scripts\build-installer.ps1
```

The script:

1. checks the .NET SDK and Inno Setup and installs them if needed;
2. reads the version from `Directory.Build.props`;
3. publishes the program self-contained (`win-x64`, .NET included) and the plug-ins in `plugins\<name>` to `artifacts\publish`;
4. lets Inno Setup (`installer\DonkiePane.iss`) turn that into `artifacts\DonkiePane-Setup-<version>.exe` (about 50 MB).

`artifacts\` is in `.gitignore`. The package isn't signed; with a code-signing certificate that can be done with `SignTool` in the Inno Setup script.

The icon comes from `scripts\make-icon.ps1` (writes `src\DonkiePane.App\Assets\DonkiePane.ico`).

### For the developer

`scripts\install.ps1` builds and installs to the same folder without an installer (handy while working); `scripts\uninstall.ps1` removes it again. A running version from before `--quit` can't be closed cleanly by the installer: stop it first through the tray menu (Exit).

### Closing cleanly: `--quit`

`DonkiePane.exe --quit` asks a running DonkiePane to close, the same way as Exit in the tray menu (the Windows desktop icons are restored). If nothing is running, nothing happens. The installer and uninstaller use this.

`DonkiePane.exe --restart-after <process id>` waits until that process has exited before starting; DonkiePane uses it to restart itself after a language change.
