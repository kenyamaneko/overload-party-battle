using System.Net.Http.Json;
using System.Text.Json;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data;

/// <summary>
/// HTTP client for the card service's internal card-master endpoint.
/// Battle calls this once at startup to populate <see cref="CardCache"/>; on failure the
/// process exits so Kubernetes restarts the pod instead of a hidden in-process retry loop.
/// </summary>
public sealed class CardServiceClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

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
        _ownsHttpClient = ownsHttpClient;
        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    /// <summary>カードサービスの internal カード一覧エンドポイントから全カード定義を取得する。</summary>
    /// <param name="ct">キャンセレーショントークン。</param>
    /// <returns>取得したカード定義の一覧。</returns>
    public async Task<List<CardDefinition>> ListAllCardsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("internal/v1/cards", ct);
        response.EnsureSuccessStatusCode();

        var cards = await response.Content.ReadFromJsonAsync<List<CardDefinition>>(JsonOptions, ct);
        if (cards is null)
        {
            throw new InvalidOperationException("card service returned null body for /internal/v1/cards");
        }

        return cards;
    }

    /// <summary>カードサービスの internal 施策一覧エンドポイントから全施策定義を取得する。</summary>
    /// <param name="ct">キャンセレーショントークン。</param>
    /// <returns>取得した施策定義の一覧。</returns>
    public async Task<List<Initiative>> ListAllInitiativesAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("internal/v1/initiatives", ct);
        response.EnsureSuccessStatusCode();

        var initiatives = await response.Content.ReadFromJsonAsync<List<Initiative>>(JsonOptions, ct);
        if (initiatives is null)
        {
            throw new InvalidOperationException("card service returned null body for /internal/v1/initiatives");
        }

        return initiatives;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
