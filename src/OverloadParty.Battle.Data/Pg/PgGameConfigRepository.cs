using System.Text.Json;
using Npgsql;
using OverloadParty.Battle.Engine;

namespace OverloadParty.Battle.Data.Pg;

/// <summary>
/// PostgreSQL implementation of IGameConfigRepository using Npgsql.
/// </summary>
public class PgGameConfigRepository(NpgsqlDataSource ds) : IGameConfigRepository
{
    public async Task<long> GetInt64(string key, long fallback, CancellationToken ct = default)
    {
        await using var conn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT value FROM game_config WHERE key = $1", conn);
        cmd.Parameters.AddWithValue(key);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull) return fallback;

        return JsonSerializer.Deserialize<long>(result.ToString()!);
    }
}
