using System.Globalization;
using DonkiePane.Tasks;

namespace DonkiePane.Tasks.Tests;

public sealed class TaskOrderingTests
{
    private static List<TaskItem> Tasks(params string[] ids) =>
        ids.Select((id, i) => new TaskItem { Id = id, Title = id, SortOrder = i }).ToList();

    [Fact]
    public void New_task_goes_above_all_open_tasks()
    {
        var tasks = Tasks("a", "b");
        tasks.Add(new TaskItem { IsCompleted = true, SortOrder = -10 });

        Assert.Equal(-1, TaskOrdering.TopOrder(tasks));
        Assert.Equal(0, TaskOrdering.TopOrder([]));
    }

    [Fact]
    public void Ordered_skips_completed_and_sorts_by_order()
    {
        var tasks = new List<TaskItem>
        {
            new() { Id = "b", SortOrder = 2 },
            new() { Id = "klaar", SortOrder = 0, IsCompleted = true },
            new() { Id = "a", SortOrder = 1 },
        };

        Assert.Equal(["a", "b"], TaskOrdering.Ordered(tasks).Select(t => t.Id));
    }

    [Theory]
    [InlineData("a", 3, "b,c,a")]
    [InlineData("a", 2, "b,a,c")]
    [InlineData("c", 0, "c,a,b")]
    [InlineData("b", -1, "a,c,b")]
    public void Move_reorders_and_renumbers(string id, int index, string expected)
    {
        var tasks = Tasks("a", "b", "c");

        TaskOrdering.Move(tasks, id, index);

        Assert.Equal(expected, string.Join(",", tasks.OrderBy(t => t.SortOrder).Select(t => t.Id)));
    }

    [Fact]
    public void Move_returns_only_changed_tasks()
    {
        var tasks = Tasks("a", "b", "c");

        var changed = TaskOrdering.Move(tasks, "c", 1);

        Assert.Equal(["c", "b"], changed.Select(t => t.Id).Order().Reverse());
    }

    [Fact]
    public void NextWeek_is_the_coming_monday()
    {
        Assert.Equal(new DateOnly(2026, 11, 16), DueDates.NextWeek(new DateOnly(2026, 11, 15))); // zondag
        Assert.Equal(new DateOnly(2026, 11, 23), DueDates.NextWeek(new DateOnly(2026, 11, 16))); // maandag
    }

    [Fact]
    public void Due_dates_are_shown_in_dutch()
    {
        var nl = CultureInfo.GetCultureInfo("nl-NL");
        var today = new DateOnly(2026, 11, 15);

        Assert.Equal("Vandaag", DueDates.Format(today, today, nl));
        Assert.Equal("Morgen", DueDates.Format(today.AddDays(1), today, nl));
        Assert.Equal("Gisteren", DueDates.Format(today.AddDays(-1), today, nl));
        Assert.Equal("Woensdag", DueDates.Format(new DateOnly(2026, 11, 18), today, nl));
        Assert.Contains("2027", DueDates.Format(new DateOnly(2027, 1, 5), today, nl));
        Assert.True(DueDates.IsOverdue(today.AddDays(-1), today));
        Assert.False(DueDates.IsOverdue(today, today));
    }
}
