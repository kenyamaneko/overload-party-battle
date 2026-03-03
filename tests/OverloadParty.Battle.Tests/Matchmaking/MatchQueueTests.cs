using OverloadParty.Battle.Matchmaking;

namespace OverloadParty.Battle.Tests.Matchmaking;

/// <summary>
/// Tests for MatchQueue — thread-safe FIFO matchmaking queue.
/// </summary>
public class MatchQueueTests
{
    [Fact]
    public void Join_AddsPlayerToQueue()
    {
        var queue = new MatchQueue();
        queue.Join("p1", 1);

        Assert.Equal(1, queue.Count);
        Assert.True(queue.IsQueued("p1"));
    }

    [Fact]
    public void Join_Idempotent_UpdatesDeckOnly()
    {
        var queue = new MatchQueue();
        queue.Join("p1", 1);
        queue.Join("p1", 2); // Same player, different deck

        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Leave_RemovesPlayer()
    {
        var queue = new MatchQueue();
        queue.Join("p1", 1);
        queue.Leave("p1");

        Assert.Equal(0, queue.Count);
        Assert.False(queue.IsQueued("p1"));
    }

    [Fact]
    public void Leave_NonexistentPlayer_NoError()
    {
        var queue = new MatchQueue();
        queue.Leave("nobody"); // Should not throw
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void IsQueued_NotInQueue_ReturnsFalse()
    {
        var queue = new MatchQueue();
        Assert.False(queue.IsQueued("p1"));
    }

    [Fact]
    public void GetWaiting_SortedByJoinTime()
    {
        var queue = new MatchQueue();
        queue.Join("p1", 1);
        queue.Join("p2", 2);
        queue.Join("p3", 3);

        var waiting = queue.GetWaiting();
        Assert.Equal(3, waiting.Count);
        // FIFO: p1 should be first (earliest join time)
        Assert.Equal("p1", waiting[0].PlayerID);
        Assert.Equal("p2", waiting[1].PlayerID);
        Assert.Equal("p3", waiting[2].PlayerID);
    }

    [Fact]
    public void Remove_RemovesSpecificPlayers()
    {
        var queue = new MatchQueue();
        queue.Join("p1", 1);
        queue.Join("p2", 2);
        queue.Join("p3", 3);

        queue.Remove("p1", "p3");

        Assert.Equal(1, queue.Count);
        Assert.False(queue.IsQueued("p1"));
        Assert.True(queue.IsQueued("p2"));
        Assert.False(queue.IsQueued("p3"));
    }

    [Fact]
    public void Remove_NonexistentPlayers_NoError()
    {
        var queue = new MatchQueue();
        queue.Join("p1", 1);
        queue.Remove("nobody1", "nobody2");

        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Count_ReflectsCurrentSize()
    {
        var queue = new MatchQueue();
        Assert.Equal(0, queue.Count);

        queue.Join("p1", 1);
        Assert.Equal(1, queue.Count);

        queue.Join("p2", 2);
        Assert.Equal(2, queue.Count);

        queue.Leave("p1");
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void GetWaiting_ReturnsSnapshot_NotLiveReference()
    {
        var queue = new MatchQueue();
        queue.Join("p1", 1);

        var snapshot = queue.GetWaiting();
        queue.Join("p2", 2); // Add after snapshot

        Assert.Single(snapshot); // snapshot unaffected
        Assert.Equal(2, queue.Count);
    }
}
