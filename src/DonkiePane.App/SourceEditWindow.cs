using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DonkiePane.Tasks;
using DonkiePane.Tasks.Plugins;

namespace DonkiePane.App;

/// <summary>
/// Het instellingenvenster van één bron. De velden komen uit de plug-in zelf (<see cref="ITaskSourcePlugin.Fields"/>);
/// geheimen gaan naar de versleutelde opslag en komen nooit in een instellingenbestand.
/// Opslaan, testen en de knoppen van de plug-in (bijv. inloggen) passen eerst de ingevulde waarden toe.
/// </summary>
public sealed class SourceEditWindow : Window
{
    // Gedeelde kleuren moeten bevroren zijn, anders mag alleen de thread die ze maakte ze gebruiken.
    private static readonly Brush Text = Frozen(Color.FromRgb(0xF5, 0xF0, 0xE8));
    private static readonly Brush Muted = Frozen(Color.FromArgb(0xA6, 0xF5, 0xF0, 0xE8));
    private static readonly Brush Good = Frozen(Color.FromRgb(0x8F, 0xD6, 0x9B));
    private static readonly Brush Bad = Frozen(Color.FromRgb(0xFF, 0x9A, 0x8A));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private readonly TaskSourceHost _host;
    private readonly SourceConnection _connection;
    private readonly ITaskSourcePlugin _plugin;
    private readonly TextBox _name = new();
    private readonly Dictionary<string, Control> _inputs = [];
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly List<Button> _buttons = [];

    public SourceEditWindow(TaskSourceHost host, SourceConnection connection, ITaskSourcePlugin plugin)
    {
        _host = host;
        _connection = connection;
        _plugin = plugin;

        Title = plugin.DisplayName;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x1A, 0x1A));
        Foreground = Text;
        FontSize = 13;

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Build() };
    }

    private StackPanel Build()
    {
        var panel = new StackPanel { Margin = new Thickness(24, 16, 24, 24) };
        panel.Children.Add(Label(_plugin.Description, muted: true, margin: new Thickness(0, 0, 0, 8)));

        panel.Children.Add(Label("Naam"));
        _name.Text = _connection.Name;
        ApplyStyle(_name);
        panel.Children.Add(_name);

        foreach (var field in _plugin.Fields)
        {
            // Een aan/uit-keuze is een vinkje met zijn eigen label; alle andere velden hebben een label erboven.
            if (field.Kind != SettingKind.Bool)
                panel.Children.Add(Label(field.Label + (field.Required ? "" : " (optioneel)")));

            Control input;
            if (field.Kind == SettingKind.Bool)
            {
                input = new CheckBox
                {
                    Content = new TextBlock { Text = field.Label, Foreground = Text, TextWrapping = TextWrapping.Wrap },
                    IsChecked = string.Equals(_connection.Values.GetValueOrDefault(field.Key, ""), "true", StringComparison.OrdinalIgnoreCase),
                    Margin = new Thickness(0, 14, 0, 0),
                };
            }
            else if (field.Kind == SettingKind.Secret)
            {
                input = new PasswordBox { Background = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)), Foreground = Text, BorderThickness = new Thickness(0), Padding = new Thickness(6, 5, 6, 5) };
            }
            else
            {
                var box = new TextBox { Text = _connection.Values.GetValueOrDefault(field.Key, "") };
                ApplyStyle(box);
                input = box;
            }
            _inputs[field.Key] = input;
            panel.Children.Add(input);

            var hint = field.Help ?? "";
            if (field.Kind == SettingKind.Secret)
                hint = (HasSecret(field.Key) ? "Er is al een waarde opgeslagen: laat dit veld leeg om die te houden. " : "") + hint;
            if (hint.Trim().Length > 0) panel.Children.Add(Label(hint.Trim(), muted: true, margin: new Thickness(0, 3, 0, 0), size: 12));
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(AddButton("Opslaan", Save_Click));
        buttons.Children.Add(AddButton("Verbinding testen", Test_Click));
        foreach (var action in _plugin.Actions)
        {
            var id = action.Id;
            buttons.Children.Add(AddButton(action.Label, async (_, _) => await RunAction(id)));
        }
        buttons.Children.Add(AddButton("Sluiten", (_, _) => Close()));
        panel.Children.Add(buttons);
        panel.Children.Add(_message);
        return panel;
    }

    private bool HasSecret(string key) => !string.IsNullOrEmpty(_host.SecretsFor(_connection.Id).Get(key));

    /// <summary>
    /// Controleert de invoer en bewaart hem. Alleen als er echt iets veranderd is, begint de bron opnieuw
    /// (een nieuwe bron begint weer met vragen aan de server).
    /// </summary>
    /// <summary>Slaat op wat er is ingevuld; een bestand dat niet te schrijven is, wordt een melding in plaats van een crash.</summary>
    private bool Apply()
    {
        try
        {
            return ApplyCore();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Show($"Opslaan is niet gelukt: {ex.Message} Probeer het zo opnieuw.", ok: false);
            return false;
        }
    }

    private bool ApplyCore()
    {
        foreach (var field in _plugin.Fields.Where(f => f.Required))
        {
            var typed = Typed(field);
            if (typed.Length == 0 && !(field.Kind == SettingKind.Secret && HasSecret(field.Key)))
            {
                Show($"Vul '{field.Label}' in.", ok: false);
                return false;
            }
        }

        var changed = !_host.Connections.Contains(_connection);
        var name = _name.Text.Trim();
        if (name.Length == 0) name = _plugin.DisplayName;
        _connection.Name = name;

        var secrets = _host.SecretsFor(_connection.Id);
        foreach (var field in _plugin.Fields)
        {
            var typed = Typed(field);
            if (field.Kind == SettingKind.Secret)
            {
                if (typed.Length == 0) continue; // leeg laten = houden
                secrets.Set(field.Key, typed);
                changed = true;
            }
            else
            {
                if (_connection.Values.GetValueOrDefault(field.Key, "") != typed) changed = true;
                _connection.Values[field.Key] = typed;
            }
        }

        _host.Save(_connection, resetSource: changed);
        foreach (var field in _plugin.Fields.Where(f => f.Kind == SettingKind.Secret))
            if (_inputs[field.Key] is PasswordBox box) box.Clear();
        return true;
    }

    private string Typed(SettingField field) => _inputs[field.Key] switch
    {
        CheckBox box => box.IsChecked == true ? "true" : "false",
        PasswordBox box => box.Password.Trim(),
        TextBox box => box.Text.Trim(),
        _ => "",
    };

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!Apply()) return;
        Show("Opgeslagen.", ok: true);
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (!Apply()) return;
        await Busy("Bezig met testen…", async () =>
        {
            var source = _host.GetSource(_connection.Id, out var error);
            if (source is null) { Show(error ?? "De bron is niet beschikbaar.", ok: false); return; }
            var check = await source.TestAsync();
            Show(check.Message, check.Ok);
        });
    }

    private async Task RunAction(string actionId)
    {
        if (!Apply()) return;
        await Busy("Bezig…", async () =>
        {
            var source = _host.GetSource(_connection.Id, out var error);
            if (source is null) { Show(error ?? "De bron is niet beschikbaar.", ok: false); return; }
            try
            {
                Show(await source.RunActionAsync(actionId), ok: true);
            }
            catch (Exception ex) when (TaskErrors.IsHandled(ex))
            {
                Show(TaskErrors.Message(ex), ok: false);
            }
        });
    }

    private async Task Busy(string message, Func<Task> work)
    {
        foreach (var b in _buttons) b.IsEnabled = false;
        Show(message, ok: null);
        try
        {
            await work();
        }
        catch (Exception ex) when (TaskErrors.IsHandled(ex))
        {
            // De knoppen zijn async void: een fout uit een plug-in mag het venster (en de app) niet laten crashen.
            Show(TaskErrors.Message(ex), ok: false);
        }
        finally
        {
            foreach (var b in _buttons) b.IsEnabled = true;
        }
    }

    private void Show(string text, bool? ok)
    {
        _message.Text = text;
        _message.Foreground = ok switch { true => Good, false => Bad, null => Muted };
    }

    private static TextBlock Label(string text, bool muted = false, Thickness? margin = null, double size = 13) => new()
    {
        Text = text,
        Foreground = muted ? Muted : Text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0, 12, 0, 3),
    };

    private static void ApplyStyle(TextBox box)
    {
        if (Application.Current?.TryFindResource("PaneTextBox") is System.Windows.Style style) box.Style = style;
        box.Padding = new Thickness(6, 5, 6, 5);
    }

    private Button AddButton(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) };
        if (Application.Current?.TryFindResource("ActionButton") is System.Windows.Style style) button.Style = style;
        button.Click += click;
        _buttons.Add(button);
        return button;
    }
}
