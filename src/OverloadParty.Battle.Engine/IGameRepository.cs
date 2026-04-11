using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Action data to be atomically appended within an UpdateGameState transaction.
/// </summary>
public record PendingAction(long PlayerNum, string ActionType, object ActionData);

/// <summary>
/// Data access contract for the game engine.
/// Engine depends only on this interface; implementation lives in the Data layer.
/// </summary>
public interface IGameRepository
{
    /// <summary>Creates a new game with its initial state.</summary>
    /// <param name="game">The game metadata.</param>
    /// <param name="state">The initial game state.</param>
    Task CreateGame(Game game, BattleGameState state, CancellationToken ct = default);

    /// <summary>Returns the game metadata, or <c>null</c> if not found.</summary>
    /// <param name="gameID">The game ID.</param>
    Task<Game?> GetGame(string gameID, CancellationToken ct = default);

    /// <summary>Returns the current game state, or <c>null</c> if not found.</summary>
    /// <param name="gameID">The game ID.</param>
    Task<BattleGameState?> GetGameState(string gameID, CancellationToken ct = default);

    /// <summary>
    /// Updates BattleGameState within a read-write transaction.
    /// The callback receives the current state; it must modify it in place.
    /// The implementation handles optimistic locking (version check + increment).
    /// If <paramref name="pendingAction"/> is provided, it is appended atomically
    /// within the same transaction with a safe auto-incremented seq number.
    /// </summary>
    Task UpdateGameState(string gameID, Func<BattleGameState, Task> fn, PendingAction? pendingAction = null, CancellationToken ct = default);

    /// <summary>Persists a game event to the event log.</summary>
    /// <param name="evt">The event to append.</param>
    Task AppendEvent(GameEvent evt, CancellationToken ct = default);

    /// <summary>Marks the game as finished and records the winner and reason.</summary>
    /// <param name="gameID">The game ID.</param>
    /// <param name="winnerNum">The winner number (0=draw, 1 or 2).</param>
    /// <param name="winReason">The wire-format win reason string.</param>
    Task FinishGame(string gameID, long winnerNum, string winReason, CancellationToken ct = default);

    /// <summary>Returns the total number of events recorded for a game.</summary>
    /// <param name="gameID">The game ID.</param>
    Task<long> GetEventCount(string gameID, CancellationToken ct = default);

    /// <summary>Updates the game's status (e.g. playing → finished).</summary>
    /// <param name="gameID">The game ID.</param>
    /// <param name="status">The new status.</param>
    Task UpdateGameStatus(string gameID, GameStatus status, CancellationToken ct = default);

    /// <summary>Returns all events for a game in order.</summary>
    /// <param name="gameID">The game ID.</param>
    Task<List<GameEvent>> GetEvents(string gameID, CancellationToken ct = default);

    /// <summary>Returns the initial game state (for replay).</summary>
    /// <param name="gameID">The game ID.</param>
    Task<BattleGameState?> GetInitialState(string gameID, CancellationToken ct = default);

}
