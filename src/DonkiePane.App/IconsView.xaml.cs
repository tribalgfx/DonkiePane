using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DonkiePane.Shell;

namespace DonkiePane.App;

/// <summary>Inhoud van een iconenpane: bureaubladitems openen en verslepen.</summary>
public partial class IconsView : UserControl
{
    private const string ItemDataFormat = "DonkiePane.Item";

    private Point _dragStart;
    private IReadOnlyList<ItemView> _items = [];

    /// <summary>Een item is op dit pane neergezet (item-id, index; -1 = achteraan).</summary>
    public event EventHandler<(string ItemId, int Index)>? ItemDropped;

    public IconsView() => InitializeComponent();

    public void SetItems(IReadOnlyList<ItemView> items)
    {
        _items = items;
        ItemsHost.ItemsSource = items;
        EmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

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

    /// <summary>Het menu van Verkenner, zodat je bijv. de Prullenbak kunt leegmaken zonder hem te openen.</summary>
    private void Tile_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement tile || ItemOf(sender) is not { } item) return;
        e.Handled = true;
        if (ShellContextMenu.TryShow(item.Path)) return;

        var menu = (ContextMenu)FindResource("FallbackMenu");
        menu.DataContext = item;
        menu.PlacementTarget = tile;
        menu.IsOpen = true;
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
            Process.Start(info)?.Dispose(); // het proces zelf hoeven we niet te volgen
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Bijv. geannuleerd UAC-venster of verwijderd doel van een snelkoppeling.
            MessageBox.Show(ex.Message, "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
