using System.Windows;
using System.Windows.Input;
using DonkiePane.Core;

namespace DonkiePane.App;

public partial class PaneWindow : Window
{
    private const double TitleBarHeight = 34;
    private readonly Pane _pane;

    /// <summary>Positie, grootte of status is veranderd en moet bewaard worden.</summary>
    public event EventHandler? PaneChanged;
    public event EventHandler? PaneDeleted;

    public PaneWindow(Pane pane)
    {
        InitializeComponent();
        _pane = pane;

        TitleText.Text = pane.Title;
        Left = pane.Left;
        Top = pane.Top;
        Width = pane.Width;
        ApplyRolledUp();

        LocationChanged += (_, _) =>
        {
            _pane.Left = Left;
            _pane.Top = Top;
        };
        SizeChanged += (_, _) =>
        {
            _pane.Width = ActualWidth;
            if (!_pane.IsRolledUp) _pane.Height = ActualHeight;
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _pane.IsRolledUp = !_pane.IsRolledUp;
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
        Placeholder.Visibility = _pane.IsRolledUp ? Visibility.Collapsed : Visibility.Visible;
        ResizeMode = _pane.IsRolledUp ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        Height = _pane.IsRolledUp ? TitleBarHeight : _pane.Height;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        PaneDeleted?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
