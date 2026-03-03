using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data;

public interface IPlayerRepository
{
    Task Create(Player player, PlayerDailyBattle dailyBattle, CancellationToken ct = default);
    Task<Player?> FindByID(string playerID, CancellationToken ct = default);
    Task<Player?> FindByFirebaseUID(string firebaseUID, CancellationToken ct = default);
    Task<PlayerDailyBattle?> GetDailyBattle(string playerID, CancellationToken ct = default);
    Task<long> IncrementDailyBattle(string playerID, DateOnly today, CancellationToken ct = default);
    Task<Player?> UpdateUsername(string playerID, string username, CancellationToken ct = default);
}
