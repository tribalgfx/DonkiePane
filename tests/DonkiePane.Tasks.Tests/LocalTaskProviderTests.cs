using DonkiePane.Tasks;

namespace DonkiePane.Tasks.Tests;

public sealed class LocalTaskProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DonkiePaneTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "tasks.json");

    private LocalTaskProvider NewProvider() => new(new TaskStore(FilePath));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Added_tasks_survive_a_restart()
    {
        var provider = NewProvider();
        await provider.AddAsync("werk", new TaskItem { Title = "Offerte sturen", Importance = TaskImportance.High, DueDate = new DateOnly(2026, 11, 20) });

        var task = Assert.Single(await NewProvider().GetTasksAsync("werk"));

        Assert.Equal("Offerte sturen", task.Title);
        Assert.Equal("werk", task.ListId);
        Assert.Equal(TaskImportance.High, task.Importance);
        Assert.Equal(new DateOnly(2026, 11, 20), task.DueDate);
    }

    [Fact]
    public async Task Lists_are_kept_apart()
    {
        var provider = NewProvider();
        await provider.AddAsync("werk", new TaskItem { Title = "A" });
        await provider.AddAsync("thuis", new TaskItem { Title = "B" });

        Assert.Equal("A", Assert.Single(await provider.GetTasksAsync("werk")).Title);
        Assert.Equal("B", Assert.Single(await provider.GetTasksAsync("thuis")).Title);
    }

    [Fact]
    public async Task Returned_tasks_are_copies_until_updated()
    {
        var provider = NewProvider();
        await provider.AddAsync("werk", new TaskItem { Title = "Oud" });

        var task = Assert.Single(await provider.GetTasksAsync("werk"));
        task.Title = "Nieuw";
        Assert.Equal("Oud", Assert.Single(await provider.GetTasksAsync("werk")).Title);

        await provider.UpdateAsync([task]);
        Assert.Equal("Nieuw", Assert.Single(await NewProvider().GetTasksAsync("werk")).Title);
    }

    [Fact]
    public async Task Update_stamps_updated_at()
    {
        var provider = NewProvider();
        var original = new TaskItem { Title = "A", UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        await provider.AddAsync("werk", original);

        var task = Assert.Single(await provider.GetTasksAsync("werk"));
        await provider.UpdateAsync([task]);

        Assert.True(Assert.Single(await provider.GetTasksAsync("werk")).UpdatedAt > original.UpdatedAt);
    }

    [Fact]
    public async Task Delete_and_delete_list()
    {
        var provider = NewProvider();
        var a = new TaskItem { Title = "A" };
        await provider.AddAsync("werk", a);
        await provider.AddAsync("werk", new TaskItem { Title = "B" });
        await provider.AddAsync("thuis", new TaskItem { Title = "C" });

        await provider.DeleteAsync(a.Id);
        Assert.Equal("B", Assert.Single(await provider.GetTasksAsync("werk")).Title);

        await provider.DeleteListAsync("werk");
        Assert.Empty(await provider.GetTasksAsync("werk"));
        Assert.Single(await NewProvider().GetTasksAsync("thuis"));
    }

    [Fact]
    public async Task Changes_raise_ListChanged()
    {
        var provider = NewProvider();
        var changed = new List<string>();
        provider.ListChanged += (_, listId) => changed.Add(listId);

        await provider.AddAsync("werk", new TaskItem { Title = "A" });

        Assert.Equal(["werk"], changed);
    }
}
