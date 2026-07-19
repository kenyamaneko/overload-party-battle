using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace OverloadParty.Battle.Tests.Server;

[Trait("対象", "HTTP アクション受付")]
[Collection(ServerTestCollection.Name)]
public class ActionIngestionTests(ServerTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Fact(DisplayName = "ヘルスチェックは 200 を返す")]
    public async Task Health_Returns200()
    {
        var resp = await _client.GetAsync("/health");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "存在しないゲームの構造化ログを取得すると、404 が返る")]
    public async Task GetLog_ForNonexistentGame_Returns404()
    {
        var resp = await _client.GetAsync("/api/v1/games/TST-none/log");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("game not found");
    }
}
