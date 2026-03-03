using OverloadParty.Battle.Matchmaking;

namespace OverloadParty.Battle.Tests.Matchmaking;

/// <summary>
/// Tests for Matcher — FIFO pairing of queued players.
/// </summary>
public class MatcherTests
{
    [Fact]
    public async Task Matcher_PairsTwoPlayers()
    {
        var queue = new MatchQueue();
        queue.Join("p1", 10);
        queue.Join("p2", 20);

        MatchResult? matched = null;
        var matcher = new Matcher(
            queue,
            (result, _) => { matched = result; return Task.CompletedTask; },
            interval: TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var task = matcher.RunAsync(cts.Token);

        // Wait for at least one tick
        await Task.Delay(200);
        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }

        Assert.NotNull(matched);
        Assert.Equal("p1", matched!.Player1ID);
        Assert.Equal(10, matched.Player1Deck);
        Assert.Equal("p2", matched.Player2ID);
        Assert.Equal(20, matched.Player2Deck);

        // Players should be removed from queue
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Matcher_OddPlayerCount_LeavesLastInQueue()
    {
        var queue = new MatchQueue();
        queue.Join("p1", 1);
        queue.Join("p2", 2);
        queue.Join("p3", 3);

        var matches = new List<MatchResult>();
        var matcher = new Matcher(
            queue,
            (result, _) => { matches.Add(result); return Task.CompletedTask; },
            interval: TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var task = matcher.RunAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }

        Assert.Single(matches); // Only one pair
        Assert.Equal(1, queue.Count); // p3 still waiting
        Assert.True(queue.IsQueued("p3"));
    }

    [Fact]
    public async Task Matcher_NoPlayers_DoesNothing()
    {
        var queue = new MatchQueue();
        var matches = new List<MatchResult>();
        var matcher = new Matcher(
            queue,
            (result, _) => { matches.Add(result); return Task.CompletedTask; },
            interval: TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try { await matcher.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }

        Assert.Empty(matches);
    }

    [Fact]
    public async Task Matcher_HandlerException_DoesNotCrash()
    {
        var queue = new MatchQueue();
        queue.Join("p1", 1);
        queue.Join("p2", 2);

        var matcher = new Matcher(
            queue,
            (_, _) => throw new InvalidOperationException("handler error"),
            interval: TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        // Should not throw — exception is caught internally
        try { await matcher.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }

        // Players should still be removed from queue (removed before handler call)
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Matcher_CancellationStops()
    {
        var queue = new MatchQueue();
        var matcher = new Matcher(
            queue,
            (_, _) => Task.CompletedTask,
            interval: TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Should exit immediately
        try { await matcher.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }
    }
}
