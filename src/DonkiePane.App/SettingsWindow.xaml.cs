using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DonkiePane.Core;
using DonkiePane.Shell;
using DonkiePane.Tasks.Plugins;

namespace DonkiePane.App;

/// <summary>Instellingen. Elke wijziging geldt direct; er is geen OK-knop.</summary>
public partial class SettingsWindow : Window
{
    private readonly Layout _layout;
    private readonly Action _save;
    private readonly Action _bringPanes;
    private readonly TaskSourceHost _sources;
    private readonly UpdateService? _updates;
    private readonly Action? _showUpdate;
    private readonly Action? _restart;
    private readonly Action<string>? _exportLayout;
    private readonly Action<Layout>? _importLayout;
    private readonly Action? _lookChanged;
    private readonly Func<string?, string?>? _setPeekHotkey;
    private readonly Func<int>? _applyRules;
    private bool _loading = true;

    /// <summary>Een regel in de lijst met regels.</summary>
    private sealed record RuleRow(FileRule Rule, string Label);

    /// <summary>Een regel in de lijst met bronnen.</summary>
    private sealed record SourceRow(SourceConnection Connection, string Label);

    internal SettingsWindow(Layout layout, Action save, Action bringPanes, TaskSourceHost sources,
        UpdateService? updates = null, Action? showUpdate = null, Action? restart = null,
        Action<string>? exportLayout = null, Action<Layout>? importLayout = null,
        Action? lookChanged = null, Func<string?, string?>? setPeekHotkey = null, Func<int>? applyRules = null)
    {
        _lookChanged = lookChanged;
        _setPeekHotkey = setPeekHotkey;
        _applyRules = applyRules;
        InitializeComponent();
        // Nooit hoger dan het scherm; wat er niet in past, scrolt.
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        _layout = layout;
        _save = save;
        _bringPanes = bringPanes;
        _sources = sources;
        _updates = updates;
        _showUpdate = showUpdate;
        _restart = restart;
        _exportLayout = exportLayout;
        _importLayout = importLayout;
        ExportButton.IsEnabled = exportLayout is not null;
        ImportButton.IsEnabled = importLayout is not null;
        UpdatesBox.IsChecked = layout.CheckForUpdates;
        RemindDueBox.IsChecked = layout.RemindDueTasks;

        // Taal: de namen van de talen staan altijd in hun eigen taal.
        LanguageDutch.Content = Loc.Languages.First(l => l.Code == Loc.Dutch).Name;
        LanguageDutch.Tag = Loc.Dutch;
        LanguageEnglish.Content = Loc.Languages.First(l => l.Code == Loc.English).Name;
        LanguageEnglish.Tag = Loc.English;
        LanguageAuto.Tag = "";
        (layout.Language switch { Loc.Dutch => LanguageDutch, Loc.English => LanguageEnglish, _ => LanguageAuto }).IsChecked = true;
        RefreshUpdateStatus();

        HideIconsBox.IsChecked = layout.HideDesktopIcons;
        SnapBox.IsChecked = layout.SnapToGrid;
        DoubleClickBox.IsChecked = layout.HideOnDesktopDoubleClick;
        GridSlider.Value = layout.GridSize;
        GridValue.Text = $"{layout.GridSize:0}";
        GridSlider.IsEnabled = layout.SnapToGrid;
        SyncSlider.Value = layout.SyncInterval.TotalSeconds;
        SyncValue.Text = FormatSeconds(layout.SyncInterval.TotalSeconds);
        RefreshAutoStart();
        RefreshSources();

        (layout.Appearance switch { AppTheme.Light => AppearanceLight, AppTheme.System => AppearanceSystem, _ => AppearanceDark }).IsChecked = true;
        CornerSlider.Value = layout.EffectiveCornerRadius;
        CornerValue.Text = $"{layout.EffectiveCornerRadius:0}";
        (layout.RollUpOpen switch { RollUpOpen.Hover => RollHover, RollUpOpen.Click => RollClick, _ => RollButtonOnly }).IsChecked = true;
        // Opnieuw instellen laat meteen zien of de sneltoets werkt (of al door iets anders gebruikt wordt).
        ShowHotkey(layout.PeekHotkey, error: setPeekHotkey?.Invoke(layout.PeekHotkey));
        RefreshRules();
        DataPath.Text = Path.GetDirectoryName(LayoutStore.DefaultPath);
        VersionText.Text = "DonkiePane " + (Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "");
        _loading = false;
    }

    private void Language_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string code }) return;
        _layout.Language = code.Length == 0 ? null : code;
        _save();
        // De taal geldt vanaf de volgende start; alleen als die echt anders wordt, is opnieuw starten nodig.
        var changes = Loc.Resolve(_layout.Language) != Loc.Language;
        LanguageHint.Visibility = changes ? Visibility.Visible : Visibility.Collapsed;
        RestartPanel.Visibility = changes && _restart is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => _restart?.Invoke();

    private void RefreshAutoStart()
    {
        var enabled = AutoStart.IsEnabled();
        AutoStartBox.IsChecked = enabled;
        AutoStartHint.Text = AutoStart.RegisteredPath() is { } path
            ? enabled ? Loc.F("Start met: {0}", path) : Loc.T("Uitgezet in Taakbeheer onder Opstarten. Vink aan om het weer aan te zetten.")
            : Loc.T("Je kunt dit ook in Taakbeheer onder Opstarten aan- of uitzetten.");
    }

    private void HideIcons_Click(object sender, RoutedEventArgs e)
    {
        _layout.HideDesktopIcons = HideIconsBox.IsChecked == true;
        DesktopIcons.SetVisible(!_layout.HideDesktopIcons);
        _save();
    }

    private void DoubleClick_Click(object sender, RoutedEventArgs e)
    {
        _layout.HideOnDesktopDoubleClick = DoubleClickBox.IsChecked == true;
        _save();
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AutoStart.SetEnabled(AutoStartBox.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshAutoStart(); // toon wat er werkelijk geregistreerd staat
    }

    private void Snap_Click(object sender, RoutedEventArgs e)
    {
        _layout.SnapToGrid = SnapBox.IsChecked == true;
        GridSlider.IsEnabled = _layout.SnapToGrid;
        _save();
    }

    private void GridSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _layout.GridSize = GridSlider.Value;
        GridValue.Text = $"{GridSlider.Value:0}";
        _save();
    }

    // ---------- Panes: uiterlijk, hoeken, oprollen ----------

    private void Appearance_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _layout.Appearance = AppearanceLight.IsChecked == true ? AppTheme.Light : AppearanceSystem.IsChecked == true ? AppTheme.System : AppTheme.Dark;
        _save();
        _lookChanged?.Invoke();
    }

    private void CornerSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _layout.CornerRadius = CornerSlider.Value;
        CornerValue.Text = $"{CornerSlider.Value:0}";
        _save();
        _lookChanged?.Invoke();
    }

    private void RollUp_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _layout.RollUpOpen = RollHover.IsChecked == true ? RollUpOpen.Hover : RollClick.IsChecked == true ? RollUpOpen.Click : RollUpOpen.Button;
        _save();
        _lookChanged?.Invoke();
    }

    // ---------- Sneltoets ----------

    private void ShowHotkey(string? text, string? error)
    {
        HotkeyBox.Text = string.IsNullOrWhiteSpace(text) ? Loc.T("Geen") : text.Replace("Space", Loc.T("Spatie"));
        HotkeyHint.Text = error ?? Loc.T("Klik in het vak en druk de toetsen in die je wilt gebruiken. Nog een keer de sneltoets, Esc of ernaast klikken zet de panes terug.");
    }

    private void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        HotkeyHint.Text = Loc.T("Druk nu de toetsen in, bijvoorbeeld Ctrl+Alt+Spatie.");

    private void HotkeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (HotkeyHint.Text == Loc.T("Druk nu de toetsen in, bijvoorbeeld Ctrl+Alt+Spatie.")) ShowHotkey(_layout.PeekHotkey, null);
    }

    /// <summary>De ingedrukte combinatie wordt de sneltoets (Esc laat hem zoals hij was).</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            ShowHotkey(_layout.PeekHotkey, null);
            Keyboard.ClearFocus();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return; // wachten op de gewone toets

        var modifiers = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Ctrl;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) modifiers |= HotkeyModifiers.Win;
        var hotkey = new Hotkey(modifiers, key.ToString());
        if (Hotkey.Parse(hotkey.ToString()) is null)
        {
            HotkeyHint.Text = Loc.T("Gebruik Ctrl, Alt of Win samen met een gewone toets.");
            return;
        }
        SetHotkey(hotkey.ToString());
    }

    private void ClearHotkey_Click(object sender, RoutedEventArgs e) => SetHotkey(null);

    private void SetHotkey(string? text)
    {
        var error = _setPeekHotkey is not null ? _setPeekHotkey(text) : null;
        if (_setPeekHotkey is null)
        {
            _layout.PeekHotkey = text ?? "";
            _save();
        }
        ShowHotkey(text, error);
    }

    // ---------- Regels ----------

    private static string KindName(RuleKind kind) => kind switch
    {
        RuleKind.Extension => Loc.T("Extensie is"),
        RuleKind.NameContains => Loc.T("Naam bevat"),
        RuleKind.NameIs => Loc.T("Naam is precies"),
        _ => Loc.T("Is een map"),
    };

    private void RefreshRules(FileRule? select = null)
    {
        var rows = _layout.Rules.Select(r =>
        {
            var target = _layout.Panes.FirstOrDefault(p => p.Id == r.TargetPane)?.Title ?? Loc.T("(pane bestaat niet meer)");
            var condition = r.Kind == RuleKind.Folder ? KindName(r.Kind) : $"{KindName(r.Kind)} \"{r.Value}\"";
            return new RuleRow(r, $"{condition}  →  {target}");
        }).ToList();
        RulesList.ItemsSource = rows;
        if (select is not null) RulesList.SelectedItem = rows.FirstOrDefault(r => r.Rule == select);
        UpdateRuleButtons();
        ApplyRulesButton.IsEnabled = _applyRules is not null && _layout.Rules.Count > 0;
    }

    private FileRule? SelectedRule => (RulesList.SelectedItem as RuleRow)?.Rule;

    private void UpdateRuleButtons()
    {
        EditRuleButton.IsEnabled = RemoveRuleButton.IsEnabled = SelectedRule is not null;
        RuleUpButton.IsEnabled = SelectedRule is { } rule && _layout.Rules.IndexOf(rule) > 0;
    }

    private void Rules_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRuleButtons();

    private List<Pane> RuleTargets() => _layout.Panes.Where(p => p.Kind == PaneKind.Icons).ToList();

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var targets = RuleTargets();
        var rule = new FileRule { TargetPane = targets.FirstOrDefault(p => !p.IsInbox)?.Id ?? targets.FirstOrDefault()?.Id ?? Guid.Empty };
        if (new RuleEditWindow(rule, targets) { Owner = this }.ShowDialog() != true) return;
        _layout.Rules.Add(rule);
        _save();
        RefreshRules(rule);
    }

    private void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is not { } rule) return;
        if (new RuleEditWindow(rule, RuleTargets()) { Owner = this }.ShowDialog() != true) return;
        _save();
        RefreshRules(rule);
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is not { } rule) return;
        _layout.Rules.Remove(rule);
        _save();
        RefreshRules();
    }

    private void RuleUp_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is not { } rule) return;
        var index = _layout.Rules.IndexOf(rule);
        if (index <= 0) return;
        _layout.Rules.RemoveAt(index);
        _layout.Rules.Insert(index - 1, rule);
        _save();
        RefreshRules(rule);
    }

    private void ApplyRules_Click(object sender, RoutedEventArgs e)
    {
        if (_applyRules is null) return;
        var moved = _applyRules();
        RulesStatus.Text = moved == 0 ? Loc.T("In de Inbox stond niets dat bij een regel past.")
            : moved == 1 ? Loc.T("1 icoon is verplaatst.") : Loc.F("{0} iconen zijn verplaatst.", moved);
    }

    // ---------- Synchronisatie ----------

    private void SyncSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Eerst deze controle: het schuifje vuurt al tijdens het opbouwen van het venster, als de rest er nog niet is.
        // Eerst deze controle: het schuifje vuurt al tijdens het opbouwen van het venster, als de rest er nog niet is.
        if (_loading) return;
        SyncValue.Text = FormatSeconds(SyncSlider.Value);
        _layout.SyncSeconds = (int)SyncSlider.Value;
        _save();
        _sources.SetPollInterval(_layout.SyncInterval); // geldt ook voor bronnen die al draaien
    }

    private static string FormatSeconds(double seconds) =>
        seconds < 60 ? $"{seconds:0} s" : seconds % 60 == 0 ? $"{seconds / 60:0} min" : $"{seconds / 60:0.#} min";

    // ---------- Takenbronnen ----------

    private void RefreshSources()
    {
        SourcesList.ItemsSource = _sources.Connections
            .Select(c => new SourceRow(c, c.Name == _sources.TypeName(c) ? c.Name : $"{c.Name}  ({_sources.TypeName(c)})"))
            .ToList();
        EditSourceButton.IsEnabled = RemoveSourceButton.IsEnabled = false;

        var loaded = _sources.Plugins.Plugins.Select(p => p.Plugin.DisplayName).ToList();
        PluginsInfo.Text = (loaded.Count == 0 ? Loc.T("Er zijn geen plug-ins geladen.") : Loc.F("Geladen plug-ins: {0}.", string.Join(", ", loaded)))
            + (_sources.Plugins.Errors.Count == 0 ? "" : "\n" + Loc.F("Problemen: {0}", string.Join("; ",
                _sources.Plugins.Errors.Select(e => $"{Path.GetFileName(e.Path)}: {e.Message}"))));
    }

    private SourceConnection? SelectedSource => (SourcesList.SelectedItem as SourceRow)?.Connection;

    private void Sources_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        EditSourceButton.IsEnabled = RemoveSourceButton.IsEnabled = SelectedSource is not null;

    private void AddSource_Click(object sender, RoutedEventArgs e)
    {
        var plugins = _sources.Plugins.Plugins.Select(p => p.Plugin).ToList();
        if (plugins.Count == 0)
        {
            MessageBox.Show(this, Loc.T("Er zijn geen plug-ins geladen, dus er is geen soort bron om toe te voegen. Zet plug-ins in de map plugins naast DonkiePane.exe."),
                "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picker = new ListPickerWindow(Loc.T("Soort bron kiezen"), Loc.T("Welk soort bron wil je toevoegen?"),
            plugins.Select(p => new TaskListInfo(p.TypeId, p.DisplayName)).ToList()) { Owner = this };
        if (picker.ShowDialog() != true || picker.Selected is not { } chosen) return;

        var plugin = plugins.First(p => p.TypeId == chosen.Id);
        EditSource(_sources.NewConnection(plugin.TypeId, plugin.DisplayName), plugin);
    }

    private void EditSource_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSource is not { } connection) return;
        if (_sources.PluginFor(connection.TypeId) is not { } plugin)
        {
            MessageBox.Show(this, Loc.F("De plug-in voor '{0}' is niet geladen, dus deze bron kan nu niet bewerkt worden.", connection.TypeId),
                "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        EditSource(connection, plugin);
    }

    private void EditSource(SourceConnection connection, ITaskSourcePlugin plugin)
    {
        new SourceEditWindow(_sources, connection, plugin) { Owner = this }.ShowDialog();
        RefreshSources();
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSource is not { } connection) return;
        var answer = MessageBox.Show(this,
            Loc.F("Bron \"{0}\" verwijderen? Je taken bij de bron zelf blijven staan. Panes die deze bron gebruiken laten daarna een melding zien, en de opgeslagen tokens op deze pc worden gewist.", connection.Name),
            "DonkiePane", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        _sources.Remove(connection.Id);
        RefreshSources();
    }

    // ---------- Updates ----------

    private void Updates_Click(object sender, RoutedEventArgs e)
    {
        _layout.CheckForUpdates = UpdatesBox.IsChecked == true;
        _save();
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        if (_updates is null) return;
        CheckNowButton.IsEnabled = false;
        UpdateStatus.Text = Loc.T("Bezig met controleren…");
        var outcome = await _updates.CheckAsync();
        CheckNowButton.IsEnabled = true;
        RefreshUpdateStatus(outcome);
        if (outcome == UpdateCheckOutcome.Available) _showUpdate?.Invoke();
    }

    private void ShowUpdate_Click(object sender, RoutedEventArgs e) => _showUpdate?.Invoke();

    private void RefreshUpdateStatus(UpdateCheckOutcome? outcome = null)
    {
        CheckNowButton.IsEnabled = _updates is not null;
        ShowUpdateButton.Visibility = _updates?.Available is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateStatus.Text = UpdateStatusText(_updates, outcome);
    }

    internal static string UpdateStatusText(UpdateService? updates, UpdateCheckOutcome? outcome)
    {
        if (updates is null) return "";
        if (updates.Available is { } release)
            return Loc.F("Versie {0} is beschikbaar.", DonkiePane.Core.Updates.AppVersion.Format(release.Version));
        if (updates.LastError is { } error) return Loc.F("Controleren lukte niet: {0}", error);
        if (outcome == UpdateCheckOutcome.NoReleases) return Loc.T("Er zijn nog geen versies gepubliceerd.");
        return updates.LastChecked is { } at ? Loc.F("Je hebt de nieuwste versie (gecontroleerd om {0:HH:mm}).", at) : "";
    }

    private void RemindDue_Click(object sender, RoutedEventArgs e)
    {
        _layout.RemindDueTasks = RemindDueBox.IsChecked == true;
        _save();
    }

    // ---------- Indeling exporteren en importeren ----------

    private const string LayoutFileFilter = "JSON (*.json)|*.json";

    private void ExportLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_exportLayout is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.T("Indeling exporteren"),
            Filter = LayoutFileFilter,
            FileName = Loc.F("DonkiePane-indeling-{0}.json", DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _exportLayout(dialog.FileName);
            MessageBox.Show(this, Loc.F("De indeling is bewaard in {0}.", dialog.FileName), "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, Loc.F("Exporteren lukte niet: {0}", ex.Message), "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_importLayout is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("Indeling importeren"), Filter = LayoutFileFilter };
        if (dialog.ShowDialog(this) != true) return;

        Layout imported;
        try
        {
            imported = LayoutTransfer.Parse(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, Loc.F("Importeren lukte niet: {0}", ex.Message), "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var answer = MessageBox.Show(this,
            Loc.F("Je huidige panes ({0}) vervangen door de {1} panes uit dit bestand? Je huidige indeling wordt eerst bewaard als {2}. Iconen die op deze pc niet bestaan, vallen weg; wat hier wel op het bureaublad staat, komt in de Inbox.",
                _layout.Panes.Count, imported.Panes.Count, Path.GetFileName(App.BeforeImportPath)),
            "DonkiePane", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            _importLayout(imported);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Lukt het bewaren van de huidige indeling niet, dan is er nog niets vervangen.
            MessageBox.Show(this, Loc.F("Importeren lukte niet: {0}", ex.Message), "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BringPanes_Click(object sender, RoutedEventArgs e) => _bringPanes();

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(LayoutStore.DefaultPath)!;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\""));
    }
}
