using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Service;

public class BattleLimitResponse
{
    public long DailyBattleCount { get; init; }
    public long DailyBattleLimit { get; init; } // -1 = unlimited
    public bool CanBattle { get; init; }
}

/// <summary>
/// Player management: username, daily battle limits, win/loss.
/// </summary>
public class PlayerService
{
    private readonly IPlayerRepository _playerRepo;
    private readonly IGameConfigRepository _gameConfigRepo;

    private const string ConfigKeyFreeDailyBattleLimit = "free_daily_battle_limit";
    private const string ConfigKeyPremiumDailyBattleLimit = "premium_daily_battle_limit";
    private const long DefaultFreeDailyBattleLimit = 10;
    private const long DefaultPremiumDailyBattleLimit = 30;

    public PlayerService(IPlayerRepository playerRepo, IGameConfigRepository gameConfigRepo)
    {
        _playerRepo = playerRepo;
        _gameConfigRepo = gameConfigRepo;
    }

    public Task<Player?> GetPlayer(string playerID, CancellationToken ct = default)
        => _playerRepo.FindByID(playerID, ct);

    public Task<Player?> UpdateUsername(string playerID, string name, CancellationToken ct = default)
        => _playerRepo.UpdateUsername(playerID, name, ct);

    public async Task<BattleLimitResponse> GetBattleLimit(string playerID, CancellationToken ct = default)
    {
        var player = await _playerRepo.FindByID(playerID, ct)
            ?? throw new InvalidOperationException($"player {playerID} not found");

        var db = await _playerRepo.GetDailyBattle(playerID, ct);
        var today = GameDay();
        var count = db is not null && db.LastResetDate == today ? db.DailyBattleCount : 0;

        if (player.IsPremium)
        {
            var premiumLimit = await _gameConfigRepo.GetInt64(ConfigKeyPremiumDailyBattleLimit, DefaultPremiumDailyBattleLimit, ct);
            return new BattleLimitResponse
            {
                DailyBattleCount = count,
                DailyBattleLimit = premiumLimit,
                CanBattle = count < premiumLimit,
            };
        }

        var freeLimit = await _gameConfigRepo.GetInt64(ConfigKeyFreeDailyBattleLimit, DefaultFreeDailyBattleLimit, ct);
        return new BattleLimitResponse
        {
            DailyBattleCount = count,
            DailyBattleLimit = freeLimit,
            CanBattle = count < freeLimit,
        };
    }

    /// <summary>
    /// Verifies the player can battle and increments the count.
    /// Throws if the daily limit has been reached.
    /// </summary>
    public async Task CheckAndIncrementBattleCount(string playerID, CancellationToken ct = default)
    {
        var player = await _playerRepo.FindByID(playerID, ct)
            ?? throw new InvalidOperationException($"player {playerID} not found");

        var today = GameDay();
        var newCount = await _playerRepo.IncrementDailyBattle(playerID, today, ct);

        long limit;
        if (player.IsPremium)
            limit = await _gameConfigRepo.GetInt64(ConfigKeyPremiumDailyBattleLimit, DefaultPremiumDailyBattleLimit, ct);
        else
            limit = await _gameConfigRepo.GetInt64(ConfigKeyFreeDailyBattleLimit, DefaultFreeDailyBattleLimit, ct);

        if (newCount > limit)
            throw new InvalidOperationException($"daily battle limit reached ({newCount}/{limit})");
    }

    /// <summary>
    /// The game day resets at JST 05:00 (= UTC 20:00).
    /// </summary>
    private static DateOnly GameDay()
    {
        var now = DateTime.UtcNow.AddHours(4); // JST+9 minus 5h offset
        return DateOnly.FromDateTime(now);
    }
}
