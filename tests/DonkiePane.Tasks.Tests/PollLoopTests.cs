using DonkiePane.Plugins.RestTasks;

namespace DonkiePane.Tasks.Tests;

public sealed class PollLoopTests
{
    [Fact]
    public async Task A_shorter_interval_applies_at_once_not_after_the_old_long_wait()
    {
        var interval = TimeSpan.FromHours(1);
        var polled = new TaskCompletionSource();
        using var stop = new CancellationTokenSource();
        var loop = PollLoop.RunAsync(() => interval, _ => { polled.TrySetResult(); return Task.CompletedTask; }, null, stop.Token);

        await Task.Delay(1500);
        Assert.False(polled.Task.IsCompleted); // een uur is nog lang niet om
        interval = TimeSpan.FromSeconds(1);     // de gebruiker kiest een korter interval

        var finished = await Task.WhenAny(polled.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(polled.Task, finished);

        stop.Cancel();
        await loop;
    }

    [Fact]
    public async Task Cancelling_stops_the_loop_without_an_error_and_without_another_poll()
    {
        var calls = 0;
        using var stop = new CancellationTokenSource();
        var loop = PollLoop.RunAsync(() => TimeSpan.FromHours(1), _ => { calls++; return Task.CompletedTask; }, null, stop.Token);

        stop.Cancel();
        await loop; // gooit niet

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Polling_repeats_while_running()
    {
        var calls = 0;
        using var stop = new CancellationTokenSource();
        var loop = PollLoop.RunAsync(() => TimeSpan.FromSeconds(1), _ => { Interlocked.Increment(ref calls); return Task.CompletedTask; }, null, stop.Token);

        await Task.Delay(3500);
        stop.Cancel();
        await loop;

        Assert.InRange(calls, 2, 4);
    }
}

public sealed class RefreshTests
{
    [Fact]
    public async Task The_refresh_button_picks_up_a_change_from_another_device_without_waiting()
    {
        var server = new FakeTasksServer();
        server.AddTask("a", "A");
        var client = new RestTasksClient(new HttpClient(server), new Uri("https://dash.test:4173/"), () => FakeTasksServer.ValidToken);
        using var provider = new RestTaskProvider(client) { PollInterval = TimeSpan.FromHours(1) };
        var changed = new List<string>();
        provider.ListChanged += (_, id) => changed.Add(id);
        await provider.GetTasksAsync(RestTaskProvider.ListId);

        server.AddTask("b", "Van mijn telefoon");
        await ((IRefreshableTaskProvider)provider).RefreshAsync();

        Assert.Equal([RestTaskProvider.ListId], changed);
    }

    [Fact]
    public async Task The_refresh_button_does_not_bypass_a_block_after_a_401()
    {
        var server = new FakeTasksServer();
        var client = new RestTasksClient(new HttpClient(server), new Uri("https://dash.test:4173/"), () => "fout");
        using var provider = new RestTaskProvider(client) { PollInterval = TimeSpan.FromHours(1) };
        await Assert.ThrowsAsync<SourceUnauthorizedException>(() => provider.GetTasksAsync(RestTaskProvider.ListId));
        var sent = server.Requests.Count;

        await provider.RefreshAsync();
        await provider.RefreshAsync();

        Assert.Equal(sent, server.Requests.Count); // geen enkel extra verzoek
    }

    [Fact]
    public void The_interval_can_be_changed_while_running()
    {
        var client = new RestTasksClient(new HttpClient(new FakeTasksServer()), new Uri("https://dash.test:4173/"), () => "t");
        using var provider = new RestTaskProvider(client);

        Assert.Equal(TimeSpan.FromSeconds(60), provider.PollInterval);
        provider.PollInterval = TimeSpan.FromSeconds(15);

        Assert.Equal(TimeSpan.FromSeconds(15), provider.PollInterval);
    }
}
