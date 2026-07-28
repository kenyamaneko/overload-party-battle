using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace OverloadParty.Battle.Tests.Server;

/// <summary>
/// Testcontainers ベースの Postgres と WebApplicationFactory&lt;Program&gt; を共有 xUnit fixture として提供する。
/// Program.cs の起動経路はプロセス環境変数を直接読むため、DB 接続文字列以外は環境変数として注入する。
/// </summary>
public class ServerTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var csb = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            SearchPath = "battle",
        };

        await using (var dataSource = NpgsqlDataSource.Create(csb.ConnectionString))
        {
            var schemaPath = Path.Combine(AppContext.BaseDirectory, "db", "schema.sql");
            var schemaSql = await File.ReadAllTextAsync(schemaPath);
            await using var conn = await dataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(schemaSql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        Environment.SetEnvironmentVariable("DATABASE_CONN", csb.ConnectionString);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("CARDS_JSON_PATH", FindCardsJson());
        Environment.SetEnvironmentVariable("INITIATIVES_JSON_PATH",
            Path.Combine(AppContext.BaseDirectory, "TestData", "initiatives_test.json"));
        Environment.SetEnvironmentVariable("NPC_AI_CONFIG_DIR", FindNpcDataDir());

        _factory = new WebApplicationFactory<Program>();
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        await _container.DisposeAsync();
    }

    private static string FindCardsJson()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "overload-party-battle",
                "packages", "game-state-dotnet", "cache", "cards_gen.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("cards_gen.json not found under any ancestor of the test binary");
    }

    private static string FindNpcDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "OverloadParty.Battle.Npc", "Data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("NPC AI config directory not found under any ancestor of the test binary");
    }
}

/// <summary>Postgres コンテナと Web ホストを共有するテスト群の collection 定義。環境変数を全体で共有するため並列実行しない。</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ServerTestCollection : ICollectionFixture<ServerTestFixture>
{
    public const string Name = "Server";
}
