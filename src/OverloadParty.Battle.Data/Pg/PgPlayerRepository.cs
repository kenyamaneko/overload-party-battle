using Npgsql;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data.Pg;

/// <summary>
/// PostgreSQL implementation of IPlayerRepository using Npgsql.
/// </summary>
public class PgPlayerRepository(NpgsqlDataSource ds) : IPlayerRepository
{
    public async Task Create(Player player, PlayerDailyBattle dailyBattle, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var cmd = new NpgsqlCommand(@"
            INSERT INTO players (
                player_id, firebase_uid, username, level, exp, wins, losses,
                is_premium, equipped_icon_no, selected_faction,
                premium_expires_at, created_at, updated_at
            ) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13)", conn, tx))
        {
            cmd.Parameters.AddWithValue(player.PlayerID);
            cmd.Parameters.AddWithValue(player.FirebaseUID);
            cmd.Parameters.AddWithValue(player.Username);
            cmd.Parameters.AddWithValue(player.Level);
            cmd.Parameters.AddWithValue(player.Exp);
            cmd.Parameters.AddWithValue(player.Wins);
            cmd.Parameters.AddWithValue(player.Losses);
            cmd.Parameters.AddWithValue(player.IsPremium);
            cmd.Parameters.AddWithValue((object?)player.EquippedIconNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)player.SelectedFaction ?? DBNull.Value);
            cmd.Parameters.AddWithValue((object?)player.PremiumExpiresAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue(player.CreatedAt);
            cmd.Parameters.AddWithValue(player.UpdatedAt);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using (var cmd = new NpgsqlCommand(@"
            INSERT INTO player_daily_battle (player_id, daily_battle_count, last_reset_date)
            VALUES ($1,$2,$3)", conn, tx))
        {
            cmd.Parameters.AddWithValue(dailyBattle.PlayerID);
            cmd.Parameters.AddWithValue(dailyBattle.DailyBattleCount);
            cmd.Parameters.AddWithValue(dailyBattle.LastResetDate);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<Player?> FindByID(string playerID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectPlayerSql + " WHERE player_id = $1", conn);
        cmd.Parameters.AddWithValue(playerID);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadPlayer(reader);
    }

    public async Task<Player?> FindByFirebaseUID(string firebaseUID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            SelectPlayerSql + " WHERE firebase_uid = $1 LIMIT 1", conn);
        cmd.Parameters.AddWithValue(firebaseUID);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadPlayer(reader);
    }

    public async Task<PlayerDailyBattle?> GetDailyBattle(string playerID, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            SELECT player_id, daily_battle_count, last_reset_date
            FROM player_daily_battle WHERE player_id = $1", conn);
        cmd.Parameters.AddWithValue(playerID);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new PlayerDailyBattle
        {
            PlayerID = reader.GetString(0),
            DailyBattleCount = reader.GetInt64(1),
            LastResetDate = DateOnly.FromDateTime(reader.GetDateTime(2)),
        };
    }

    public async Task<long> IncrementDailyBattle(string playerID, DateOnly today, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        long count;
        DateOnly lastReset;

        await using (var cmd = new NpgsqlCommand(@"
            SELECT daily_battle_count, last_reset_date
            FROM player_daily_battle WHERE player_id = $1 FOR UPDATE", conn, tx))
        {
            cmd.Parameters.AddWithValue(playerID);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new InvalidOperationException($"daily battle not found for player {playerID}");
            count = reader.GetInt64(0);
            lastReset = DateOnly.FromDateTime(reader.GetDateTime(1));
        }

        count = lastReset != today ? 1 : count + 1;

        await using (var cmd = new NpgsqlCommand(@"
            UPDATE player_daily_battle SET daily_battle_count = $1, last_reset_date = $2
            WHERE player_id = $3", conn, tx))
        {
            cmd.Parameters.AddWithValue(count);
            cmd.Parameters.AddWithValue(today);
            cmd.Parameters.AddWithValue(playerID);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return count;
    }

    public async Task<Player?> UpdateUsername(string playerID, string username, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            UPDATE players SET username = $1, updated_at = NOW()
            WHERE player_id = $2
            RETURNING player_id, firebase_uid, username, level, exp, wins, losses,
                      is_premium, equipped_icon_no, selected_faction,
                      premium_expires_at, created_at, updated_at", conn);
        cmd.Parameters.AddWithValue(username);
        cmd.Parameters.AddWithValue(playerID);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadPlayer(reader);
    }

    // ─── Helpers ─────────────────────────────────────────────────

    private const string SelectPlayerSql = @"
        SELECT player_id, firebase_uid, username, level, exp, wins, losses,
               is_premium, equipped_icon_no, selected_faction,
               premium_expires_at, created_at, updated_at
        FROM players";

    private static Player ReadPlayer(NpgsqlDataReader r)
    {
        return new Player
        {
            PlayerID = r.GetString(0),
            FirebaseUID = r.GetString(1),
            Username = r.GetString(2),
            Level = r.GetInt64(3),
            Exp = r.GetInt64(4),
            Wins = r.GetInt64(5),
            Losses = r.GetInt64(6),
            IsPremium = r.GetBoolean(7),
            EquippedIconNo = r.IsDBNull(8) ? null : r.GetInt64(8),
            SelectedFaction = r.IsDBNull(9) ? null : r.GetString(9),
            PremiumExpiresAt = r.IsDBNull(10) ? null : r.GetDateTime(10),
            CreatedAt = r.GetDateTime(11),
            UpdatedAt = r.GetDateTime(12),
        };
    }
}
