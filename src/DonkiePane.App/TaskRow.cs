using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using DonkiePane.Tasks;

namespace DonkiePane.App;

/// <summary>Eén taak zoals een taakpane hem toont.</summary>
/// <param name="showStar">Of de bron "belangrijk" kent; Google Taken niet, dan is er geen ster.</param>
public sealed class TaskRow(TaskItem task, bool showStar = true) : INotifyPropertyChanged
{
    private bool _isEditing;

    public TaskItem Task { get; } = task;

    public string Title => Task.Title;
    public bool IsCompleted => Task.IsCompleted;
    public bool IsImportant => Task.Importance == TaskImportance.High;
    public bool ShowStar { get; } = showStar;

    /// <summary>Een subtaak springt in onder zijn hoofdtaak.</summary>
    public System.Windows.Thickness RowMargin => new(Task.ParentId is null ? 0 : 22, 1, 0, 1);
    public bool HasDueDate => Task.DueDate is not null;

    public string DueText => Task.DueDate is { } due ? DueDates.Format(due, DueDates.Today, Loc.Culture) : "";

    public bool IsOverdue => !Task.IsCompleted && Task.DueDate is { } due && DueDates.IsOverdue(due, DueDates.Today);

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
