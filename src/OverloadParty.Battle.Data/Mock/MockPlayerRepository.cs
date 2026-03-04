using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data.Mock;

/// <summary>
/// In-memory player repository for local development.
/// </summary>
public class MockPlayerRepository : IPlayerRepository
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Player> _players = new();
    private readonly Dictionary<string, string> _firebaseIndex = new(); // firebaseUID → playerID
    private readonly Dictionary<string, PlayerDailyBattle> _dailyBattles = new();

    public Task Create(Player player, PlayerDailyBattle dailyBattle, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _players[player.PlayerID] = player;
            _firebaseIndex[player.FirebaseUID] = player.PlayerID;
            _dailyBattles[player.PlayerID] = dailyBattle;
        }
        return Task.CompletedTask;
    }

    public Task<Player?> FindByID(string playerID, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(_players.GetValueOrDefault(playerID)); }
    }

    public Task<Player?> FindByFirebaseUID(string firebaseUID, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_firebaseIndex.TryGetValue(firebaseUID, out var pid))
                return Task.FromResult(_players.GetValueOrDefault(pid));
            return Task.FromResult<Player?>(null);
        }
    }

    public Task<PlayerDailyBattle?> GetDailyBattle(string playerID, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(_dailyBattles.GetValueOrDefault(playerID)); }
    }

    public Task<long> IncrementDailyBattle(string playerID, DateOnly today, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_dailyBattles.TryGetValue(playerID, out var db))
            {
                db = new PlayerDailyBattle { PlayerID = playerID };
                _dailyBattles[playerID] = db;
            }

            if (db.LastResetDate != today)
            {
                db.DailyBattleCount = 0;
                db.LastResetDate = today;
            }

            db.DailyBattleCount++;
            return Task.FromResult(db.DailyBattleCount);
        }
    }

    public Task<Player?> UpdateUsername(string playerID, string username, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_players.TryGetValue(playerID, out var player))
            {
                player.Username = username;
                player.UpdatedAt = DateTime.UtcNow;
                return Task.FromResult<Player?>(player);
            }
            return Task.FromResult<Player?>(null);
        }
    }
}
