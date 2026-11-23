using DonkiePane.Tasks;

namespace DonkiePane.Tasks.Tests;

public sealed class DueRemindersTests
{
    private static readonly DateOnly Today = new(2026, 11, 23);

    [Fact]
    public void Only_open_tasks_due_today_count()
    {
        var tasks = new[]
        {
            new TaskItem { Id = "1", Title = "Vandaag", DueDate = Today, SortOrder = 2 },
            new TaskItem { Id = "2", Title = "Gisteren", DueDate = Today.AddDays(-1) },
            new TaskItem { Id = "3", Title = "Morgen", DueDate = Today.AddDays(1) },
            new TaskItem { Id = "4", Title = "Al klaar", DueDate = Today, IsCompleted = true },
            new TaskItem { Id = "5", Title = "Geen deadline" },
            new TaskItem { Id = "6", Title = "Ook vandaag", DueDate = Today, SortOrder = 1 },
        };

        Assert.Equal(["6", "1"], DueReminders.DueToday(tasks, Today).Select(t => t.Id)); // in de volgorde van de lijst
    }

    [Fact]
    public void The_body_lists_the_titles_and_counts_the_rest()
    {
        Assert.Equal("• A\n• B", DueReminders.Body(["A", "B"], "en nog {0}"));
        Assert.Equal("• A\n• B\nen nog 3", DueReminders.Body(["A", "B", "C", "D", "E"], "en nog {0}", max: 2));
    }
}
