using Npgsql;

namespace OverloadParty.Battle.Tests.Data;

/// <summary>
/// Shared helper for PostgreSQL integration tests.
/// Tests are skipped when TEST_DB_URL is not set.
/// Start the container with:
///   docker compose -f ../overload-party-common/db/docker-compose.test.yml up -d
/// Then run tests with:
///   TEST_DB_URL="Host=localhost;Port=5433;Database=testdb;Username=testuser;Password=testpass" dotnet test
/// </summary>
public static class PgTestFixture
{
    private static readonly string[] TruncateTables =
    [
        "game_events", "game_states", "games",
    ];

    private static readonly Lazy<NpgsqlDataSource?> _lazyDs = new(InitDataSource);

    /// <summary>
    /// Returns the shared NpgsqlDataSource, or null if TEST_DB_URL is not set.
    /// Tables are truncated once on first access.
    /// </summary>
    public static NpgsqlDataSource? DataSource => _lazyDs.Value;

    private static NpgsqlDataSource? InitDataSource()
    {
        var connStr = Environment.GetEnvironmentVariable("TEST_DB_URL");
        if (string.IsNullOrEmpty(connStr))
        {
            return null;
        }

        var ds = NpgsqlDataSource.Create(connStr);

        // Truncate tables for isolation (runs once)
        using var conn = ds.OpenConnection();
        foreach (var table in TruncateTables)
        {
            using var cmd = new NpgsqlCommand($"TRUNCATE {table} CASCADE", conn);
            cmd.ExecuteNonQuery();
        }

        return ds;
    }
}
