using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DonkiePane.Core;

namespace DonkiePane.App;

/// <summary>Eén regel maken of wijzigen: waar hij naar kijkt, de tekst, en naar welk pane passende bestanden gaan.</summary>
public sealed class RuleEditWindow : Window
{
    private static readonly Brush Text = Frozen(Color.FromRgb(0xF5, 0xF0, 0xE8));
    private static readonly Brush Muted = Frozen(Color.FromArgb(0xA6, 0xF5, 0xF0, 0xE8));
    private static readonly Brush Field = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static readonly (RuleKind Kind, string Name, string Hint)[] Kinds =
    [
        (RuleKind.Extension, "Extensie is", "Een of meer extensies, gescheiden door komma's, bijvoorbeeld: pdf, docx, xlsx"),
        (RuleKind.NameContains, "Naam bevat", "Een stuk van de naam, bijvoorbeeld: factuur (hoofdletters maken niet uit)"),
        (RuleKind.NameIs, "Naam is precies", "De hele naam met extensie, bijvoorbeeld: Notities.txt"),
        (RuleKind.Folder, "Is een map", "Alle nieuwe mappen op het bureaublad"),
    ];

    private readonly FileRule _rule;
    private readonly ComboBox _kind = new();
    private readonly TextBox _value = new();
    private readonly TextBlock _hint = new() { Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly ComboBox _target = new();
    private readonly TextBlock _error = new() { Foreground = Frozen(Color.FromRgb(0xFF, 0x9A, 0x8A)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };

    public RuleEditWindow(FileRule rule, IReadOnlyList<Pane> targets)
    {
        _rule = rule;
        Title = Loc.T("Regel");
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x1A, 0x1A));
        Foreground = Text;
        FontSize = 13;

        var panel = new StackPanel { Margin = new Thickness(24, 16, 24, 20) };
        panel.Children.Add(Label(Loc.T("Als een nieuw bestand op het bureaublad…")));
        foreach (var (kind, name, _) in Kinds) _kind.Items.Add(new ComboBoxItem { Content = Loc.T(name), Tag = kind });
        _kind.SelectedIndex = Math.Max(0, Array.FindIndex(Kinds, k => k.Kind == rule.Kind));
        _kind.SelectionChanged += (_, _) => UpdateKind();
        panel.Children.Add(_kind);

        _value.Text = rule.Value;
        _value.Background = Field;
        _value.Foreground = Text;
        _value.CaretBrush = Text;
        _value.BorderThickness = new Thickness(0);
        _value.Padding = new Thickness(6, 5, 6, 5);
        _value.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(_value);
        panel.Children.Add(_hint);

        panel.Children.Add(Label(Loc.T("…dan naar dit pane:"), new Thickness(0, 14, 0, 4)));
        foreach (var pane in targets) _target.Items.Add(new ComboBoxItem { Content = pane.Title, Tag = pane.Id });
        _target.SelectedItem = _target.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (Guid)i.Tag == rule.TargetPane) ?? _target.Items.OfType<ComboBoxItem>().FirstOrDefault();
        panel.Children.Add(_target);
        panel.Children.Add(_error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = Loc.T("Opslaan"), IsDefault = true, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => Save();
        var cancel = new Button { Content = Loc.T("Annuleren"), IsCancel = true, Padding = new Thickness(14, 5, 14, 5) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;

        UpdateKind();
        Loaded += (_, _) => (_value.IsEnabled ? _value : (Control)_kind).Focus();
    }

    private static TextBlock Label(string text, Thickness? margin = null) =>
        new() { Text = text, Foreground = Text, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0, 0, 0, 4) };

    private RuleKind SelectedKind => (_kind.SelectedItem as ComboBoxItem)?.Tag is RuleKind kind ? kind : RuleKind.Extension;

    private void UpdateKind()
    {
        var kind = SelectedKind;
        _value.Visibility = kind == RuleKind.Folder ? Visibility.Collapsed : Visibility.Visible;
        _value.IsEnabled = kind != RuleKind.Folder;
        _hint.Text = Loc.T(Kinds.First(k => k.Kind == kind).Hint);
    }

    private void Save()
    {
        var kind = SelectedKind;
        var value = _value.Text.Trim();
        if (kind != RuleKind.Folder && (kind == RuleKind.Extension ? FileRule.Extensions(value).Count == 0 : value.Length == 0))
        {
            _error.Text = Loc.T("Vul in waar de regel naar moet kijken.");
            return;
        }
        if ((_target.SelectedItem as ComboBoxItem)?.Tag is not Guid target)
        {
            _error.Text = Loc.T("Kies een pane. Alleen iconenpanes kunnen bestanden ontvangen.");
            return;
        }
        _rule.Kind = kind;
        _rule.Value = kind == RuleKind.Folder ? "" : kind == RuleKind.Extension ? string.Join(", ", FileRule.Extensions(value)) : value;
        _rule.TargetPane = target;
        DialogResult = true;
    }
}
