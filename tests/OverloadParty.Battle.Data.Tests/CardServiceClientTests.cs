using System.Net;
using System.Net.Http;
using System.Text;
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

    [Fact(DisplayName = "snake_case の payload を受けたとき、カード一覧にデシリアライズする")]
    public async Task ListAllCardsAsync_deserializes_snake_case_payload()
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
        handler.LastRequestUri!.AbsoluteUri.Should().Be("http://card:9003/internal/v1/cards");
    }

    [Fact(DisplayName = "非成功ステータスのとき、HttpRequestException を投げる")]
    public async Task ListAllCardsAsync_throws_on_non_success_status()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}");
        using var http = new HttpClient(handler);
        using var client = new CardServiceClient("http://card:9003/", http);

        await client.Invoking(c => c.ListAllCardsAsync())
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact(DisplayName = "body が null リテラルのとき、InvalidOperationException を投げる")]
    public async Task ListAllCardsAsync_throws_when_body_is_null_literal()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "null");
        using var http = new HttpClient(handler);
        using var client = new CardServiceClient("http://card:9003", http);

        await client.Invoking(c => c.ListAllCardsAsync())
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
