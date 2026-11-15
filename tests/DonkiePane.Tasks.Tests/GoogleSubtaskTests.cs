using System.Net;
using DonkiePane.Plugins.Google;

namespace DonkiePane.Tasks.Tests;

public sealed class GoogleSubtaskTests
{
    private static GoogleTaskDto Dto(string id, string position, string? parent = null, string status = "needsAction") => new()
    {
        Id = id,
        Title = id,
        Position = position,
        Parent = parent,
        Status = status,
        Updated = "2026-11-10T08:00:00.000Z",
    };

    // De posities van subtaken tellen alleen onder hun eigen hoofdtaak, zoals bij Google.
    private static readonly GoogleTaskDto[] Family =
    [
        Dto("B", "20"),
        Dto("a2", "2", parent: "A"),
        Dto("A", "10"),
        Dto("a1", "1", parent: "A"),
    ];

    [Fact]
    public void Subtasks_come_directly_under_their_parent_and_are_marked()
    {
        var tasks = GoogleTaskProvider.Map("L1", Family);

        Assert.Equal(["A", "a1", "a2", "B"], TaskOrdering.Ordered(tasks).Select(t => t.Id));
        Assert.Equal([null, "A", "A", null], TaskOrdering.Ordered(tasks).Select(t => t.ParentId));
    }

    [Fact]
    public void A_subtask_whose_parent_is_completed_stands_alone_instead_of_vanishing()
    {
        var tasks = GoogleTaskProvider.Map("L1",
        [
            Dto("A", "10", status: "completed"),
            Dto("a1", "1", parent: "A"),
            Dto("B", "20"),
        ]);

        var open = TaskOrdering.Ordered(tasks).ToList();
        Assert.Contains(open, t => t.Id == "a1");
        Assert.All(open, t => Assert.Null(t.ParentId));
    }

    [Fact]
    public void A_completed_subtask_is_just_a_completed_task()
    {
        var tasks = GoogleTaskProvider.Map("L1", [Dto("A", "10"), Dto("a1", "1", parent: "A", status: "completed")]);

        var done = Assert.Single(TaskOrdering.Completed(tasks));
        Assert.Equal("a1", done.Id);
        Assert.Null(done.ParentId);
        Assert.Equal(["A"], TaskOrdering.Ordered(tasks).Select(t => t.Id));
    }

    [Fact]
    public void Without_subtasks_nothing_changes()
    {
        var tasks = GoogleTaskProvider.Map("L1", [Dto("c", "30"), Dto("a", "10"), Dto("b", "20")]);

        Assert.Equal(["a", "b", "c"], TaskOrdering.Ordered(tasks).Select(t => t.Id));
        Assert.All(tasks, t => Assert.Null(t.ParentId));
    }

    private sealed class Rig : IDisposable
    {
        public List<RecordedRequest> Moves { get; } = [];
        public GoogleTaskProvider Provider { get; }

        public Rig()
        {
            var handler = new FakeHandler(request =>
            {
                if (request.Method == HttpMethod.Get)
                    return Responses.Json(new { items = Family.Select(d => new { id = d.Id, title = d.Title, position = d.Position, parent = d.Parent, status = d.Status, updated = d.Updated }) });
                Moves.Add(request);
                return Responses.Json(new { id = "x" });
            });
            Provider = new GoogleTaskProvider(new GoogleTasksClient(new HttpClient(handler), (_, _) => System.Threading.Tasks.Task.FromResult("tok")))
            {
                PollInterval = TimeSpan.FromHours(1),
            };
        }

        public void Dispose() => Provider.Dispose();
    }

    [Fact]
    public async Task Moving_a_subtask_keeps_it_under_its_parent()
    {
        using var rig = new Rig();
        await rig.Provider.GetTasksAsync("L1"); // vlakke lijst: A, a1, a2, B

        await rig.Provider.MoveAsync("L1", "a2", 1);  // boven a1: eerste onder A
        await rig.Provider.MoveAsync("L1", "a1", 4);  // helemaal naar achteren: onder a2, nog steeds onder A

        // Zonder parent maakt Google er een gewone taak van; dat mag nooit gebeuren.
        Assert.EndsWith("/tasks/a2/move?parent=A", rig.Moves[0].PathAndQuery);
        Assert.EndsWith("/tasks/a1/move?previous=a2&parent=A", rig.Moves[1].PathAndQuery);
    }

    [Fact]
    public async Task Moving_a_normal_task_skips_subtasks_when_choosing_its_neighbour()
    {
        using var rig = new Rig();
        await rig.Provider.GetTasksAsync("L1");

        await rig.Provider.MoveAsync("L1", "B", 2); // tussen a1 en a2 in de lijst: de gewone buur erboven is A

        Assert.EndsWith("/tasks/B/move?previous=A", rig.Moves[0].PathAndQuery); // geen parent
    }

    [Fact]
    public async Task The_client_sends_previous_and_parent_in_the_move_request()
    {
        var handler = new FakeHandler(_ => Responses.Json(new { id = "x" }));
        var client = new GoogleTasksClient(new HttpClient(handler), (_, _) => System.Threading.Tasks.Task.FromResult("tok"));

        await client.MoveAsync("L1", "T1", "P", "A");
        await client.MoveAsync("L1", "T1", null, "A");
        await client.MoveAsync("L1", "T1", "P");
        await client.MoveAsync("L1", "T1", null);

        Assert.EndsWith("/move?previous=P&parent=A", handler.Requests[0].PathAndQuery);
        Assert.EndsWith("/move?parent=A", handler.Requests[1].PathAndQuery);
        Assert.EndsWith("/move?previous=P", handler.Requests[2].PathAndQuery);
        Assert.EndsWith("/T1/move", handler.Requests[3].PathAndQuery);
    }

    [Theory]
    [InlineData("a2", 1, null)]   // boven a1: geen eerdere buur onder A
    [InlineData("a1", 4, "a2")]   // naar achteren: onder a2
    [InlineData("B", 2, "A")]     // gewone taak: de subtaak a1 telt niet als buur
    [InlineData("A", 4, "B")]     // gewone taak naar achteren: onder B
    public void The_neighbour_is_chosen_among_tasks_with_the_same_parent(string id, int index, string? expected)
    {
        var flat = TaskOrdering.Ordered(GoogleTaskProvider.Map("L1", Family)).ToList();

        Assert.Equal(expected, TaskOrdering.PreviousSiblingId(flat, id, index, sameParent: true));
    }
}
