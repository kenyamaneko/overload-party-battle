using Microsoft.Extensions.Logging;

namespace OverloadParty.Battle.Matchmaking;

/// <summary>
/// Represents a successful match between two players.
/// </summary>
public class MatchResult
{
    public required string Player1ID { get; init; }
    public long Player1Deck { get; init; }
    public required string Player2ID { get; init; }
    public long Player2Deck { get; init; }
}

/// <summary>
/// Runs a periodic loop to find player pairs using FIFO ordering.
/// </summary>
public class Matcher
{
    private readonly MatchQueue _queue;
    private readonly Func<MatchResult, CancellationToken, Task> _handler;
    private readonly ILogger<Matcher>? _logger;
    private readonly TimeSpan _interval;

    public Matcher(
        MatchQueue queue,
        Func<MatchResult, CancellationToken, Task> handler,
        ILogger<Matcher>? logger = null,
        TimeSpan? interval = null)
    {
        _queue = queue;
        _handler = handler;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Starts the matching loop. Blocks until cancellation is requested.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_interval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(ct))
                    break;

                await MatchPlayers(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task MatchPlayers(CancellationToken ct)
    {
        var waiting = _queue.GetWaiting(); // already sorted by JoinedAt
        if (waiting.Count < 2) return;

        // FIFO pairing
        for (int i = 0; i + 1 < waiting.Count; i += 2)
        {
            var p1 = waiting[i];
            var p2 = waiting[i + 1];

            _queue.Remove(p1.PlayerID, p2.PlayerID);

            var result = new MatchResult
            {
                Player1ID = p1.PlayerID,
                Player1Deck = p1.DeckID,
                Player2ID = p2.PlayerID,
                Player2Deck = p2.DeckID,
            };

            try
            {
                await _handler(result, ct);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Match handler failed for {P1} vs {P2}", p1.PlayerID, p2.PlayerID);
            }
        }
    }
}
