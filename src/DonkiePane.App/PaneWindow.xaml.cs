using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DonkiePane.Core;
using DonkiePane.Shell;

namespace DonkiePane.App;

public partial class PaneWindow : Window
{
    private const double TitleBarHeight = 34;
    private const string ItemDataFormat = "DonkiePane.Item";

    private Point _dragStart;
    private IReadOnlyList<ItemView> _items = [];

    public Pane Pane { get; }

    /// <summary>Positie, grootte, titel of status is veranderd en moet bewaard worden.</summary>
    public event EventHandler? PaneChanged;
    public event EventHandler? DeleteRequested;

    /// <summary>Een item is op dit pane neergezet (item-id, index; -1 = achteraan).</summary>
    public event EventHandler<(string ItemId, int Index)>? ItemDropped;

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

    public void SetItems(IReadOnlyList<ItemView> items)
    {
        _items = items;
        ItemsHost.ItemsSource = items;
        EmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Titelbalk ----------

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

    // ---------- Iconen ----------

    private static ItemView? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as ItemView;

    private void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        if (e.ClickCount == 2 && ItemOf(sender) is { } item)
        {
            Open(item);
            e.Handled = true;
        }
    }

    private void Tile_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || ItemOf(sender) is not { } item) return;

        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // Alleen verplaatsen tussen panes; het bestand zelf blijft staan.
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(ItemDataFormat, item.Id), DragDropEffects.Move);
    }

    private void Body_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(ItemDataFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Body_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(ItemDataFormat) is not string itemId) return;
        ItemDropped?.Invoke(this, (itemId, DropIndex(e)));
        e.Handled = true;
    }

    /// <summary>Het icoon onder de muis bepaalt de plek: het nieuwe item komt ervóór.</summary>
    private int DropIndex(DragEventArgs e)
    {
        var hit = ItemsHost.InputHitTest(e.GetPosition(ItemsHost)) as DependencyObject;
        while (hit is not null and not ContentPresenter)
            hit = VisualTreeHelper.GetParent(hit);
        if (hit is ContentPresenter { Content: ItemView target })
        {
            var index = _items.ToList().FindIndex(i => i.Id == target.Id);
            if (index >= 0) return index;
        }
        return -1;
    }

    private void OpenItem_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Open(item);
    }

    private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        if (VirtualDesktopItems.IsVirtual(item.Path)) Open(item); // heeft geen bestandslocatie
        else StartProcess(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\""));
    }

    private static void Open(ItemView item) =>
        StartProcess(VirtualDesktopItems.IsVirtual(item.Path)
            ? new ProcessStartInfo("explorer.exe", "shell:" + item.Path)
            : new ProcessStartInfo(item.Path)
            {
                UseShellExecute = true,
                WorkingDirectory = System.IO.Path.GetDirectoryName(item.Path),
            });

    private static void StartProcess(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Bijv. geannuleerd UAC-venster of verwijderd doel van een snelkoppeling.
            MessageBox.Show(ex.Message, "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
