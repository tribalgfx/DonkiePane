using System.Globalization;
using System.Windows.Threading;
using DonkiePane.Core;
using DonkiePane.Tasks;
using Forms = System.Windows.Forms;

namespace DonkiePane.App;

/// <summary>Een melding bij het icoon bij de klok voor open taken met een deadline van vandaag.</summary>
public partial class App
{
    private static readonly TimeSpan FirstReminderCheck = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromMinutes(15);

    private DispatcherTimer? _reminderTimer;
    private bool _checkingReminders;

    /// <summary>Een minuut na het starten (dan zijn de bronnen er) en daarna elk kwartier.</summary>
    private void StartDueReminders()
    {
        _reminderTimer = new DispatcherTimer { Interval = FirstReminderCheck };
        _reminderTimer.Tick += async (_, _) =>
        {
            _reminderTimer.Interval = ReminderInterval;
            await CheckDueTasksAsync();
        };
        _reminderTimer.Start();
    }

    /// <summary>
    /// Kijkt in elk takenpane naar open taken met de deadline van vandaag. Over elke taak komt hooguit één melding per
    /// dag, ook na opnieuw starten. Een bron die niet bereikbaar is, slaan we over: het pane zelf toont die fout al.
    /// </summary>
    private async Task CheckDueTasksAsync()
    {
        if (!_layout.RemindDueTasks || _trayIcon is null || _checkingReminders) return;
        _checkingReminders = true;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            var todayKey = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (_layout.DueRemindersDate != todayKey)
            {
                _layout.DueRemindersDate = todayKey;
                _layout.DueRemindedTaskIds = [];
            }

            var reminded = new HashSet<string>(_layout.DueRemindedTaskIds, StringComparer.Ordinal);
            var titles = new List<string>();
            foreach (var pane in _layout.Panes.Where(p => p is { Kind: PaneKind.Tasks, TaskListId: not null }).ToList())
            {
                IReadOnlyList<TaskItem> tasks;
                try
                {
                    tasks = await TaskProviderFor(pane).GetTasksAsync(pane.TaskListId!);
                }
                catch (Exception ex) when (TaskErrors.IsHandled(ex))
                {
                    continue;
                }

                foreach (var task in DueReminders.DueToday(tasks, today))
                {
                    // Twee panes met dezelfde lijst geven één melding.
                    if (reminded.Add($"{pane.SourceId ?? TaskSources.Local}/{pane.TaskListId}/{task.Id}")) titles.Add(task.Title);
                }
            }

            if (titles.Count == 0 || !_layout.RemindDueTasks || _trayIcon is null) return;
            _layout.DueRemindedTaskIds = reminded.ToList();
            SaveLayout();

            _balloonClicked = () => { if (_panesHidden) ShowAllPanes(); };
            _trayIcon.ShowBalloonTip(10_000,
                titles.Count == 1 ? Loc.T("Deadline vandaag") : Loc.F("{0} taken met een deadline vandaag", titles.Count),
                DueReminders.Body(titles, Loc.T("en nog {0}")),
                Forms.ToolTipIcon.Info);
        }
        finally
        {
            _checkingReminders = false;
        }
    }

    private void StopDueReminders() => _reminderTimer?.Stop();
}
