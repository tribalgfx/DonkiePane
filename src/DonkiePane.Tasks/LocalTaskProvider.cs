using DonkiePane.Core;

namespace DonkiePane.Tasks;

public sealed class TaskStoreData
{
    public int Version { get; set; } = 1;
    public List<TaskItem> Tasks { get; set; } = [];
}

/// <summary>Alle taken in <c>%APPDATA%\DonkiePane\tasks.json</c>.</summary>
public sealed class TaskStore(string filePath) : JsonFileStore<TaskStoreData>(filePath)
{
    public static string DefaultPath => AppDataPath("tasks.json");
}

/// <summary>Taken die alleen op deze pc staan. Elke wijziging wordt direct opgeslagen.</summary>
public sealed class LocalTaskProvider : ITaskProvider
{
    private readonly TaskStore _store;
    private readonly TaskStoreData _data;

    public event EventHandler<string>? ListChanged;

    public LocalTaskProvider(TaskStore store)
    {
        _store = store;
        _data = store.Load();
    }

    public Task<IReadOnlyList<TaskItem>> GetTasksAsync(string listId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(
            _data.Tasks.Where(t => t.ListId == listId).Select(t => t.Clone()).ToList());

    public Task AddAsync(string listId, TaskItem task, CancellationToken ct = default)
    {
        var copy = task.Clone();
        copy.ListId = listId;
        _data.Tasks.Add(copy);
        SaveAndNotify([listId]);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(IEnumerable<TaskItem> tasks, CancellationToken ct = default)
    {
        var changedLists = new HashSet<string>();
        foreach (var task in tasks)
        {
            var index = _data.Tasks.FindIndex(t => t.Id == task.Id);
            if (index < 0) continue;
            var copy = task.Clone();
            copy.UpdatedAt = DateTimeOffset.UtcNow;
            changedLists.Add(_data.Tasks[index].ListId);
            changedLists.Add(copy.ListId);
            _data.Tasks[index] = copy;
        }
        if (changedLists.Count > 0) SaveAndNotify(changedLists);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string taskId, CancellationToken ct = default)
    {
        var task = _data.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is not null)
        {
            _data.Tasks.Remove(task);
            SaveAndNotify([task.ListId]);
        }
        return Task.CompletedTask;
    }

    public Task DeleteListAsync(string listId, CancellationToken ct = default)
    {
        if (_data.Tasks.RemoveAll(t => t.ListId == listId) > 0)
            SaveAndNotify([listId]);
        return Task.CompletedTask;
    }

    private void SaveAndNotify(IEnumerable<string> listIds)
    {
        _store.Save(_data);
        foreach (var listId in listIds)
            ListChanged?.Invoke(this, listId);
    }
}
