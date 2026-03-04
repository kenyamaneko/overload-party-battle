namespace OverloadParty.Battle.Matchmaking;

/// <summary>
/// Represents a player waiting in the matchmaking queue.
/// </summary>
public class QueueEntry
{
    public required string PlayerID { get; init; }
    public long DeckID { get; set; }
    public DateTime JoinedAt { get; init; }
}

/// <summary>
/// In-memory FIFO matchmaking queue. Thread-safe.
/// </summary>
public class MatchQueue
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, QueueEntry> _entries = new();

    /// <summary>
    /// Adds a player to the queue. Idempotent: if already queued, updates the deck.
    /// </summary>
    public void Join(string playerID, long deckID)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(playerID, out var existing))
            {
                existing.DeckID = deckID;
                return;
            }

            _entries[playerID] = new QueueEntry
            {
                PlayerID = playerID,
                DeckID = deckID,
                JoinedAt = DateTime.UtcNow,
            };
        }
    }

    /// <summary>
    /// Removes a player from the queue.
    /// </summary>
    public void Leave(string playerID)
    {
        lock (_lock) { _entries.Remove(playerID); }
    }

    /// <summary>
    /// Returns true if the player is in the queue.
    /// </summary>
    public bool IsQueued(string playerID)
    {
        lock (_lock) { return _entries.ContainsKey(playerID); }
    }

    /// <summary>
    /// Returns a snapshot of all waiting players sorted by join time (FIFO).
    /// </summary>
    public List<QueueEntry> GetWaiting()
    {
        lock (_lock)
        {
            return _entries.Values
                .OrderBy(e => e.JoinedAt)
                .ToList();
        }
    }

    /// <summary>
    /// Removes specific players from the queue (used after matching).
    /// </summary>
    public void Remove(params string[] playerIDs)
    {
        lock (_lock)
        {
            foreach (var id in playerIDs)
                _entries.Remove(id);
        }
    }

    /// <summary>
    /// Returns the number of players in the queue.
    /// </summary>
    public int Count
    {
        get { lock (_lock) { return _entries.Count; } }
    }
}
