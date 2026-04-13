using Google.Cloud.Firestore;
using OverloadParty.Battle.Data;
using OverloadParty.Battle.Data.Firestore;

namespace OverloadParty.Battle.Tests.Data;

/// <summary>
/// Integration test for <see cref="FirestoreGameConfigRepository"/>. Requires the
/// Firestore emulator to be reachable via <c>FIRESTORE_EMULATOR_HOST</c>. CI starts
/// the emulator; local runs can use <c>gcloud emulators firestore start
/// --host-port=localhost:9041</c>. Tests are skipped when the env var is unset,
/// matching the PgGameRepositoryTests skip pattern (TryCreate + early return).
/// </summary>
public class FirestoreGameConfigRepositoryTests
{
    private const string ProjectId = "overload-party-test";

    private static async Task<FirestoreDb?> TryCreateDbAsync()
    {
        var host = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
        if (string.IsNullOrEmpty(host)) return null;

        await ResetEmulatorAsync(host);
        return FirestoreDb.Create(ProjectId);
    }

    private static async Task ResetEmulatorAsync(string host)
    {
        using var http = new HttpClient();
        var url = $"http://{host}/emulator/v1/projects/{ProjectId}/databases/(default)/documents";
        var resp = await http.DeleteAsync(url);
        resp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetInt64Async_returns_seeded_value()
    {
        var db = await TryCreateDbAsync();
        if (db is null) return;

        await db.Collection("game_config").Document("exp_win").SetAsync(new Dictionary<string, object>
        {
            ["value"] = 40L,
        });

        var repo = new FirestoreGameConfigRepository(db);

        var got = await repo.GetInt64Async("exp_win");
        got.Should().Be(40L);
    }

    [Fact]
    public async Task GetInt64Async_throws_NotFoundException_when_missing()
    {
        var db = await TryCreateDbAsync();
        if (db is null) return;

        var repo = new FirestoreGameConfigRepository(db);

        var act = () => repo.GetInt64Async("does_not_exist");
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
