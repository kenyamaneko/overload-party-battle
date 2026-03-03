using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data.Mock;

/// <summary>
/// In-memory game repository for local development.
/// </summary>
public class MockGameRepository : IGameRepository
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Game> _games = new();
    private readonly Dictionary<string, GameState> _states = new();
    private readonly Dictionary<string, List<GameEvent>> _events = new();

    public Task CreateGame(Game game, GameState state, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _games[game.GameID] = game;
            _states[game.GameID] = state;
            _events[game.GameID] = [];
        }
        return Task.CompletedTask;
    }

    public Task<Game?> GetGame(string gameID, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(_games.GetValueOrDefault(gameID)); }
    }

    public Task<GameState?> GetGameState(string gameID, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(_states.GetValueOrDefault(gameID)); }
    }

    public async Task UpdateGameState(string gameID, Func<GameState, Task> fn, CancellationToken ct = default)
    {
        GameState state;
        lock (_lock)
        {
            state = _states.GetValueOrDefault(gameID)
                ?? throw new InvalidOperationException($"game state {gameID} not found");
        }

        await fn(state);

        lock (_lock)
        {
            state.Version++;
            state.UpdatedAt = DateTime.UtcNow;
        }
    }

    public Task AppendEvent(GameEvent evt, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_events.TryGetValue(evt.GameID, out var list))
            {
                list = [];
                _events[evt.GameID] = list;
            }
            list.Add(evt);
        }
        return Task.CompletedTask;
    }

    public Task FinishGame(string gameID, string winnerID, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_games.TryGetValue(gameID, out var game))
            {
                game.Status = GameStatus.Finished;
                game.WinnerID = winnerID;
                game.FinishedAt = DateTime.UtcNow;
            }
        }
        return Task.CompletedTask;
    }

    public Task<long> GetEventCount(string gameID, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var count = _events.GetValueOrDefault(gameID)?.Count ?? 0;
            return Task.FromResult((long)count);
        }
    }

    public Task UpdateGameStatus(string gameID, GameStatus status, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_games.TryGetValue(gameID, out var game))
                game.Status = status;
        }
        return Task.CompletedTask;
    }

    public Task UpdateWinLoss(string playerID, long wins, long losses, CancellationToken ct = default)
    {
        // No-op for mock
        return Task.CompletedTask;
    }

    public Task<List<GameEvent>> GetEvents(string gameID, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_events.GetValueOrDefault(gameID) ?? []);
        }
    }
}
