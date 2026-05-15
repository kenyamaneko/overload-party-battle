using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Models.Json;

namespace OverloadParty.Battle.Tests.Fakes;

/// <summary>IGameRepository のインメモリ test double。</summary>
public class FakeGameRepository : IGameRepository
{
    // Zone<T> は固定長配列表現の converter がないと round-trip しないため、ディープコピー用 options に追加する。
    private static readonly JsonSerializerOptions DeepCopyOptions = new()
    {
        Converters = { new ZoneJsonConverterFactory() },
    };

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Game> _games = new();
    private readonly Dictionary<string, BattleGameState> _states = new();
    private readonly Dictionary<string, BattleGameState> _initialStates = new();
    private readonly Dictionary<string, List<GameEvent>> _events = new();
    private readonly Dictionary<string, List<PlayerSummarySnapshot>> _playerSummaries = new();

    public Task CreateGame(Game game, BattleGameState state, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _games[game.GameID] = game;
            _states[game.GameID] = state;
            // 初期スナップショットを保持するため JSON ラウンドトリップでディープコピー
            var json = JsonSerializer.Serialize(state, DeepCopyOptions);
            _initialStates[game.GameID] = JsonSerializer.Deserialize<BattleGameState>(json, DeepCopyOptions)!;
            _events[game.GameID] = [];
        }
        return Task.CompletedTask;
    }

    public Task<Game?> GetGame(string gameID, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(_games.GetValueOrDefault(gameID)); }
    }

    public Task<BattleGameState?> GetGameState(string gameID, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(_states.GetValueOrDefault(gameID)); }
    }

    public async Task UpdateGameState(string gameID, Func<BattleGameState, Task> fn, PendingAction? pendingAction = null, CancellationToken ct = default)
    {
        BattleGameState state;
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

            // pendingAction は本番では DB に書き込まれるがインメモリでは追跡しない
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

    public Task FinishGame(string gameID, long winnerNum, string winReason, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_games.TryGetValue(gameID, out var game))
            {
                game.Status = GameStatus.Finished;
                game.WinningPlayerNum = (int)winnerNum;
                game.WinReason = winReason;
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

    public Task<BattleGameState?> GetInitialState(string gameID, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_initialStates.GetValueOrDefault(gameID));
        }
    }

    public Task SavePlayerSummaries(string gameID, IReadOnlyList<PlayerSummarySnapshot> summaries, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _playerSummaries[gameID] = summaries.Select(s => new PlayerSummarySnapshot
            {
                PlayerNum = s.PlayerNum,
                Name = s.Name,
                Level = s.Level,
            }).ToList();
        }
        return Task.CompletedTask;
    }

    public Task<List<PlayerSummarySnapshot>> GetPlayerSummaries(string gameID, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_playerSummaries.GetValueOrDefault(gameID) ?? []);
        }
    }
}
