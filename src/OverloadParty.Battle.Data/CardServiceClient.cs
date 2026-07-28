using ApiCard = OverloadParty.ApiCard;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data;

/// <summary>
/// HTTP client for the card service's internal card-master endpoint.
/// Battle calls this once at startup to populate <see cref="CardCache"/>; on failure the
/// process exits so Kubernetes restarts the pod instead of a hidden in-process retry loop.
/// </summary>
public sealed class CardServiceClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _canDisposeHttpClient;
    private readonly ApiCard.CardClient _client;

    public CardServiceClient(string baseUrl)
        : this(baseUrl, new HttpClient(), ownsHttpClient: true)
    {
    }

    public CardServiceClient(string baseUrl, HttpClient httpClient)
        : this(baseUrl, httpClient, ownsHttpClient: false)
    {
    }

    private CardServiceClient(string baseUrl, HttpClient httpClient, bool ownsHttpClient)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("baseUrl must not be empty", nameof(baseUrl));
        }

        _http = httpClient;
        _canDisposeHttpClient = ownsHttpClient;
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _client = new ApiCard.CardClient(_http);
    }

    /// <summary>カードサービスの internal カード一覧エンドポイントから全カード定義を取得する。</summary>
    /// <param name="ct">キャンセレーショントークン。</param>
    /// <returns>取得したカード定義の一覧。</returns>
    public async Task<List<CardDefinition>> ListAllCardsAsync(CancellationToken ct = default)
    {
        var cards = await _client.ListCardsAsync(ct);
        return cards.Select(CardDefinitionMapper.ToCardDefinition).ToList();
    }

    /// <summary>カードサービスの internal 施策一覧エンドポイントから全施策定義を取得する。</summary>
    /// <param name="ct">キャンセレーショントークン。</param>
    /// <returns>取得した施策定義の一覧。</returns>
    public async Task<List<Initiative>> ListAllInitiativesAsync(CancellationToken ct = default)
    {
        var initiatives = await _client.ListInitiativesAsync(ct);
        return initiatives.Select(CardDefinitionMapper.ToInitiative).ToList();
    }

    public void Dispose()
    {
        if (_canDisposeHttpClient)
        {
            _http.Dispose();
        }
    }
}
