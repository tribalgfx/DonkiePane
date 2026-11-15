using System.Windows;
using System.Windows.Input;
using DonkiePane.Core;
using DonkiePane.Shell;

namespace DonkiePane.App;

/// <summary>
/// Het venster om een pane heen: titelbalk, verslepen, grootte, oprollen,
/// hernoemen en de koppeling met het bureaublad. De inhoud (iconen of taken)
/// komt via <see cref="SetContent"/>.
/// </summary>
public partial class PaneWindow : Window
{
    private const double TitleBarHeight = 34;

    public Pane Pane { get; }

    /// <summary>Positie, grootte, titel of status is veranderd en moet bewaard worden.</summary>
    public event EventHandler? PaneChanged;
    public event EventHandler? DeleteRequested;

    public PaneWindow(Pane pane)
    {
        InitializeComponent();
        Pane = pane;
        DesktopLayer.Attach(this);

        TitleText.Text = pane.Title;
        DeleteMenuItem.Visibility = pane.IsInbox ? Visibility.Collapsed : Visibility.Visible;
        Left = pane.Left;
        Top = pane.Top;
        Width = pane.Width;
        ApplyRolledUp();

        LocationChanged += (_, _) =>
        {
            Pane.Left = Left;
            Pane.Top = Top;
        };
        SizeChanged += (_, _) =>
        {
            Pane.Width = ActualWidth;
            if (!Pane.IsRolledUp) Pane.Height = ActualHeight;
        };
    }

    public void SetContent(UIElement content) => Body.Content = content;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TitleEditor.IsVisible) return;

        if (e.ClickCount == 2)
        {
            Pane.IsRolledUp = !Pane.IsRolledUp;
            ApplyRolledUp();
            PaneChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        DragMove(); // blokkeert tot de muis wordt losgelaten
        PaneChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        // Na vergroten/verkleinen met de grip.
        PaneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyRolledUp()
    {
        Body.Visibility = Pane.IsRolledUp ? Visibility.Collapsed : Visibility.Visible;
        ResizeMode = Pane.IsRolledUp ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        Height = Pane.IsRolledUp ? TitleBarHeight : Pane.Height;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        TitleEditor.Text = Pane.Title;
        TitleText.Visibility = Visibility.Collapsed;
        TitleEditor.Visibility = Visibility.Visible;
        Activate();
        TitleEditor.Focus();
        TitleEditor.SelectAll();
    }

    private void TitleEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) EndRename(commit: true);
        else if (e.Key == Key.Escape) EndRename(commit: false);
    }

    private void TitleEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        EndRename(commit: true);

    private void EndRename(bool commit)
    {
        if (!TitleEditor.IsVisible) return;
        TitleEditor.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;

        var title = TitleEditor.Text.Trim();
        if (!commit || title.Length == 0 || title == Pane.Title) return;
        Pane.Title = title;
        TitleText.Text = title;
        PaneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Delete_Click(object sender, RoutedEventArgs e) =>
        DeleteRequested?.Invoke(this, EventArgs.Empty);
}
