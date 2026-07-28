using System.Net;
using System.Net.Http;
using System.Text;
using OverloadParty.ApiCard;
using OverloadParty.Battle.Data;

namespace OverloadParty.Battle.Tests.Data;

[Trait("対象", "カードサービスクライアント")]
public class CardServiceClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public Uri? LastRequestUri { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    [Fact(DisplayName = "stats を含む payload を受けたとき、実効ステータスを持つカード定義にマッピングする")]
    public async Task ListAllCardsAsync_maps_stats_into_card_definition()
    {
        const string body = """
        [
          {
            "card_id": "TST-0001",
            "card_name": "VM Instance",
            "resource_label": "VM",
            "faction": "SHE",
            "card_type": "Compute",
            "resizable": true,
            "elastic": false,
            "stats": {
              "throughput": 400,
              "availability": 800,
              "sla_penalty": 300,
              "maintenance_cost": 100
            },
            "restriction": "unlimited",
            "is_active": true
          }
        ]
        """;

        var handler = new StubHandler(HttpStatusCode.OK, body);
        using var http = new HttpClient(handler);
        using var client = new CardServiceClient("http://card:9003", http);

        var cards = await client.ListAllCardsAsync();

        cards.Should().HaveCount(1);
        cards[0].CardId.Should().Be("TST-0001");
        cards[0].CardName.Should().Be("VM Instance");
        cards[0].Faction.Should().Be("SHE");
        cards[0].Resizable.Should().BeTrue();
        cards[0].IsActive.Should().BeTrue();
        cards[0].ComputeStats.Should().NotBeNull();
        cards[0].ComputeStats!.Throughput.Should().Be(400);
        cards[0].ComputeStats!.Availability.Should().Be(800);
        handler.LastRequestUri!.AbsoluteUri.Should().Be("http://card:9003/internal/v1/cards");
    }

    [Fact(DisplayName = "非成功ステータスのとき、ApiException を投げる")]
    public async Task ListAllCardsAsync_throws_on_non_success_status()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}");
        using var http = new HttpClient(handler);
        using var client = new CardServiceClient("http://card:9003/", http);

        await client.Invoking(c => c.ListAllCardsAsync())
            .Should().ThrowAsync<ApiException>();
    }

    [Fact(DisplayName = "body が null リテラルのとき、ApiException を投げる")]
    public async Task ListAllCardsAsync_throws_when_body_is_null_literal()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        using var http = new HttpClient(handler);
        using var client = new CardServiceClient("http://card:9003", http);

        await client.Invoking(c => c.ListAllCardsAsync())
            .Should().ThrowAsync<ApiException>();
    }

    [Fact(DisplayName = "カード一覧 API が空配列を返すと、0 件のリストが返る")]
    public async Task ListAllCardsAsync_emptyArray_returnsEmptyList()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        using var http = new HttpClient(handler);
        using var client = new CardServiceClient("http://card:9003", http);

        var cards = await client.ListAllCardsAsync();

        cards.Should().BeEmpty();
    }

    [Fact(DisplayName = "カード一覧 API が 2 件返すと、2 件とも順序どおり返る")]
    public async Task ListAllCardsAsync_twoCards_returnsBothInOrder()
    {
        const string body = """
        [
          { "card_id": "TST-0001", "card_name": "A", "resource_label": "VM", "faction": "SHE", "card_type": "Compute", "resizable": true, "elastic": false, "restriction": "unlimited", "is_active": true },
          { "card_id": "TST-0002", "card_name": "B", "resource_label": "VM", "faction": "SHE", "card_type": "Compute", "resizable": true, "elastic": false, "restriction": "unlimited", "is_active": true }
        ]
        """;
        var handler = new StubHandler(HttpStatusCode.OK, body);
        using var http = new HttpClient(handler);
        using var client = new CardServiceClient("http://card:9003", http);

        var cards = await client.ListAllCardsAsync();

        cards.Should().HaveCount(2);
        cards[0].CardId.Should().Be("TST-0001");
        cards[1].CardId.Should().Be("TST-0002");
    }

    [Fact(DisplayName = "施策一覧 API が snake_case の payload を返すと、施策リストにデシリアライズされる")]
    public async Task ListAllInitiativesAsync_deserializes_snake_case_payload()
    {
        const string body = """
        [
          { "initiative_id": "IN-TST-0001", "product_id": "PD-TST-0001", "kind": "routine", "name": "R1", "insight_cost": 400 },
          { "initiative_id": "IN-TST-0002", "product_id": "PD-TST-0001", "kind": "special", "name": "S1", "insight_cost": 800 }
        ]
        """;
        var handler = new StubHandler(HttpStatusCode.OK, body);
        using var http = new HttpClient(handler);
        using var client = new CardServiceClient("http://card:9003", http);

        var initiatives = await client.ListAllInitiativesAsync();

        initiatives.Should().HaveCount(2);
        initiatives[0].InitiativeId.Should().Be("IN-TST-0001");
        initiatives[0].InsightCost.Should().Be(400);
        handler.LastRequestUri!.AbsoluteUri.Should().Be("http://card:9003/internal/v1/initiatives");
    }

    [Fact(DisplayName = "施策一覧 API が null リテラルを返すと、InvalidOperationException を投げる")]
    public async Task ListAllInitiativesAsync_throws_when_body_is_null_literal()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        using var http = new HttpClient(handler);
        using var client = new CardServiceClient("http://card:9003", http);

        await client.Invoking(c => c.ListAllInitiativesAsync())
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact(DisplayName = "接続先 URL が空文字のとき、生成時に ArgumentException を投げる")]
    public void Constructor_emptyBaseUrl_throwsArgumentException()
    {
        var act = () => new CardServiceClient("");

        act.Should().Throw<ArgumentException>().WithParameterName("baseUrl");
    }

    [Fact(DisplayName = "注入した HttpClient は、クライアントの破棄後も利用できる")]
    public async Task Dispose_withInjectedHttpClient_leavesHttpClientUsable()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        using var http = new HttpClient(handler);
        var client = new CardServiceClient("http://card:9003", http);
        client.Dispose();

        var cards = await new CardServiceClient("http://card:9003", http).ListAllCardsAsync();

        cards.Should().BeEmpty();
    }
}
