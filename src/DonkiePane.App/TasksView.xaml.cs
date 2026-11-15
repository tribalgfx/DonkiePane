using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DonkiePane.Tasks;

namespace DonkiePane.App;

/// <summary>
/// Inhoud van een taakpane. Alle wijzigingen gaan via de
/// <see cref="ITaskProvider"/>; de lijst wordt opnieuw opgebouwd zodra die
/// meldt dat de lijst veranderd is.
/// </summary>
public partial class TasksView : UserControl
{
    private const string TaskDataFormat = "DonkiePane.Task";

    private readonly ITaskProvider _provider;
    private readonly string _listId;
    private List<TaskItem> _tasks = [];
    private List<TaskRow> _openRows = [];
    private Point _dragStart;

    public TasksView(ITaskProvider provider, string listId)
    {
        InitializeComponent();
        _provider = provider;
        _listId = listId;

        Loaded += async (_, _) =>
        {
            _provider.ListChanged += Provider_ListChanged;
            await ReloadAsync();
        };
        Unloaded += (_, _) => _provider.ListChanged -= Provider_ListChanged;
    }

    private async void Provider_ListChanged(object? sender, string listId)
    {
        if (listId == _listId) await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _tasks = (await _provider.GetTasksAsync(_listId)).ToList();

        _openRows = TaskOrdering.Ordered(_tasks).Select(t => new TaskRow(t)).ToList();
        var completed = TaskOrdering.Completed(_tasks).Select(t => new TaskRow(t)).ToList();

        OpenList.ItemsSource = _openRows;
        CompletedList.ItemsSource = completed;
        EmptyHint.Visibility = _openRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CompletedExpander.Header = $"Voltooid ({completed.Count})";
        CompletedExpander.Visibility = completed.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static TaskRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as TaskRow;

    private Task SaveAsync(TaskItem task) => _provider.UpdateAsync([task]);

    // ---------- Toevoegen ----------

    private async void NewTaskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            NewTaskBox.Clear();
            return;
        }
        if (e.Key != Key.Enter) return;

        var title = NewTaskBox.Text.Trim();
        if (title.Length == 0) return;
        NewTaskBox.Clear();
        await _provider.AddAsync(_listId, new TaskItem { Title = title, SortOrder = TaskOrdering.TopOrder(_tasks) });
    }

    // ---------- Afvinken, belangrijk, deadline ----------

    private async void Complete_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        var task = row.Task;
        task.IsCompleted = !task.IsCompleted;
        task.CompletedAt = task.IsCompleted ? DateTimeOffset.UtcNow : null;
        if (!task.IsCompleted) task.SortOrder = TaskOrdering.TopOrder(_tasks.Where(t => t.Id != task.Id));
        await SaveAsync(task);
    }

    private async void ToggleImportant_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        row.Task.Importance = row.IsImportant ? TaskImportance.Normal : TaskImportance.High;
        await SaveAsync(row.Task);
    }

    private async void Due_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row || sender is not MenuItem { Tag: string choice }) return;
        var today = DueDates.Today;
        row.Task.DueDate = choice switch
        {
            "today" => today,
            "tomorrow" => today.AddDays(1),
            "nextweek" => DueDates.NextWeek(today),
            _ => null,
        };
        await SaveAsync(row.Task);
    }

    private async void Calendar_SelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not Calendar { SelectedDate: { } date } calendar || RowOf(calendar) is not { } row) return;

        // De kalender houdt de muis vast; loslaten, anders is de volgende klik "weg".
        Mouse.Capture(null);
        CloseContextMenu(calendar);

        row.Task.DueDate = DateOnly.FromDateTime(date);
        await SaveAsync(row.Task);
    }

    private static void CloseContextMenu(DependencyObject element)
    {
        for (var current = element; current is not null; current = LogicalTreeHelper.GetParent(current))
        {
            if (current is ContextMenu menu)
            {
                menu.IsOpen = false;
                return;
            }
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await _provider.DeleteAsync(row.Task.Id);
    }

    // ---------- Naam wijzigen ----------

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) row.IsEditing = true;
    }

    private void Editor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox { IsVisible: true } editor || RowOf(editor) is not { } row) return;
        editor.Text = row.Title;
        Window.GetWindow(editor)?.Activate();
        editor.Focus();
        editor.SelectAll();
    }

    private async void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await EndEditAsync(sender, commit: true);
        else if (e.Key == Key.Escape) await EndEditAsync(sender, commit: false);
    }

    private async void Editor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        await EndEditAsync(sender, commit: true);

    private async Task EndEditAsync(object sender, bool commit)
    {
        if (sender is not TextBox editor || RowOf(editor) is not { IsEditing: true } row) return;
        row.IsEditing = false;

        var title = editor.Text.Trim();
        if (!commit || title.Length == 0 || title == row.Task.Title) return;
        row.Task.Title = title;
        await SaveAsync(row.Task);
    }

    // ---------- Verslepen ----------

    private void Row_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        if (e.ClickCount == 2 && !IsInsideButton(e.OriginalSource) && RowOf(sender) is { IsEditing: false } row)
        {
            row.IsEditing = true;
            e.Handled = true;
        }
    }

    private void Row_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || IsInsideButton(e.OriginalSource)) return;
        if (RowOf(sender) is not { IsEditing: false, IsCompleted: false } row) return;

        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(TaskDataFormat, row.Task.Id), DragDropEffects.Move);
    }

    private void OpenList_DragOver(object sender, DragEventArgs e)
    {
        var ok = e.Data.GetData(TaskDataFormat) is string id && _openRows.Any(r => r.Task.Id == id);
        e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OpenList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(TaskDataFormat) is not string id) return;
        e.Handled = true;

        var ordered = _openRows.Select(r => r.Task).ToList();
        var changed = TaskOrdering.Move(ordered, id, DropIndex(e));
        if (changed.Count > 0) await _provider.UpdateAsync(changed);
    }

    /// <summary>De taak onder de muis bepaalt de plek: de gesleepte taak komt ervóór (of erna, op de onderste helft).</summary>
    private int DropIndex(DragEventArgs e)
    {
        var hit = OpenList.InputHitTest(e.GetPosition(OpenList)) as DependencyObject;
        while (hit is not null and not ContentPresenter)
            hit = VisualTreeHelper.GetParent(hit);
        if (hit is not ContentPresenter { Content: TaskRow target } presenter) return -1;

        var index = _openRows.IndexOf(target);
        var lowerHalf = e.GetPosition(presenter).Y > presenter.ActualHeight / 2;
        return lowerHalf ? index + 1 : index;
    }

    private static bool IsInsideButton(object source)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is ButtonBase) return true;
        return false;
    }
}
