using System.Text.Json;
using OverloadParty.Battle.Data.Json;
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
    private readonly Dictionary<string, GameState> _initialStates = new();
    private readonly Dictionary<string, List<GameEvent>> _events = new();
    private readonly Dictionary<string, List<GameAction>> _actions = new();

    public Task CreateGame(Game game, GameState state, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _games[game.GameID] = game;
            _states[game.GameID] = state;
            // Deep-copy via JSON round-trip to preserve the initial snapshot
            var json = JsonSerializer.Serialize(state, DbJsonOptions.Default);
            _initialStates[game.GameID] = JsonSerializer.Deserialize<GameState>(json, DbJsonOptions.Default)!;
            _events[game.GameID] = [];
            _actions[game.GameID] = [];
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

    public async Task UpdateGameState(string gameID, Func<GameState, Task> fn, PendingAction? pendingAction = null, CancellationToken ct = default)
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

            if (pendingAction is not null)
            {
                if (!_actions.TryGetValue(gameID, out var list))
                {
                    list = [];
                    _actions[gameID] = list;
                }
                var json = JsonSerializer.Serialize(pendingAction.ActionData, DbJsonOptions.Default);
                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json, DbJsonOptions.Default);
                list.Add(new GameAction
                {
                    GameID = gameID,
                    Seq = list.Count + 1,
                    PlayerID = pendingAction.PlayerID,
                    ActionType = pendingAction.ActionType,
                    ActionData = dict,
                    CreatedAt = DateTime.UtcNow,
                });
            }
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
            {
                game.Status = status;
            }
        }
        return Task.CompletedTask;
    }

    public Task<List<GameEvent>> GetEvents(string gameID, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_events.GetValueOrDefault(gameID) ?? []);
        }
    }

    public Task<GameState?> GetInitialState(string gameID, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_initialStates.GetValueOrDefault(gameID));
        }
    }

    public Task<List<GameAction>> GetActions(string gameID, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_actions.GetValueOrDefault(gameID) ?? []);
        }
    }
}
