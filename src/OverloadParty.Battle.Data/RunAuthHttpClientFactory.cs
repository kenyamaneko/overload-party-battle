using Google.Apis.Auth.OAuth2;

namespace OverloadParty.Battle.Data;

/// <summary>
/// Cloud Run のサービス間呼び出しに使う <see cref="HttpClient"/> を組み立てる。
/// </summary>
public static class RunAuthHttpClientFactory
{
    /// <summary>
    /// audience 宛の Google 発行 ID トークンを自動で付与する HttpClient を返す。
    /// Cloud Run の呼び出し IAM はこのトークンを見るため、下流を呼ぶ経路はこれを通す。
    /// audience には呼び出し先 Cloud Run サービスの URL を渡す。
    /// </summary>
    public static async Task<HttpClient> CreateAsync(string audience, CancellationToken cancellationToken = default)
    {
        var credential = await GoogleCredential.GetApplicationDefaultAsync(cancellationToken);
        var initializer = credential.UnderlyingCredential as IOidcTokenProvider
            ?? throw new InvalidOperationException(
                $"runauth: application default credential does not support ID tokens for {audience}");

        var token = await initializer.GetOidcTokenAsync(OidcTokenOptions.FromTargetAudience(audience), cancellationToken);
        return new HttpClient(new OidcTokenHandler(token));
    }
}

/// <summary>
/// 各リクエストに ID トークンを Authorization ヘッダとして付与する <see cref="DelegatingHandler"/>。
/// </summary>
internal sealed class OidcTokenHandler(OidcToken token) : DelegatingHandler(new HttpClientHandler())
{
    private readonly OidcToken _token = token;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = await _token.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
