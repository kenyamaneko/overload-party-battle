using Npgsql;
using Testcontainers.PostgreSql;

namespace OverloadParty.Battle.Tests.Data;

/// <summary>Testcontainers ベースの Postgres を共有 xUnit fixture として提供する。</summary>
public class PgTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private NpgsqlDataSource? _dataSource;

    public NpgsqlDataSource DataSource =>
        _dataSource ?? throw new InvalidOperationException("DataSource accessed before InitializeAsync");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());

        var schemaPath = Path.Combine(AppContext.BaseDirectory, "db", "schema.sql");
        var schemaSql = await File.ReadAllTextAsync(schemaPath);
        await using var conn = await _dataSource.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(schemaSql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }
        await _container.DisposeAsync();
    }
}

/// <summary>Postgres コンテナを共有するテスト群の collection 定義。</summary>
[CollectionDefinition(Name)]
public class PgTestCollection : ICollectionFixture<PgTestFixture>
{
    public const string Name = "Postgres";
}
