using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Data access contract for the game engine.
/// Engine depends only on this interface; implementation lives in the Data layer.
/// </summary>
public interface IGameRepository
{
    Task CreateGame(Game game, GameState state, CancellationToken ct = default);
    Task<Game?> GetGame(string gameID, CancellationToken ct = default);
    Task<GameState?> GetGameState(string gameID, CancellationToken ct = default);

    /// <summary>
    /// Updates GameState within a read-write transaction.
    /// The callback receives the current state; it must modify it in place.
    /// The implementation handles optimistic locking (version check + increment).
    /// </summary>
    Task UpdateGameState(string gameID, Func<GameState, Task> fn, CancellationToken ct = default);

    Task AppendEvent(GameEvent evt, CancellationToken ct = default);
    Task FinishGame(string gameID, string winnerID, CancellationToken ct = default);
    Task<long> GetEventCount(string gameID, CancellationToken ct = default);
    Task UpdateGameStatus(string gameID, GameStatus status, CancellationToken ct = default);
    Task UpdateWinLoss(string playerID, long wins, long losses, CancellationToken ct = default);
    Task<List<GameEvent>> GetEvents(string gameID, CancellationToken ct = default);
}
