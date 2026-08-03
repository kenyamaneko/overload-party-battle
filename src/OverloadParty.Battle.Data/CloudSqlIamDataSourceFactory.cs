using Google.Apis.Auth.OAuth2;
using Npgsql;

namespace OverloadParty.Battle.Data;

/// <summary>
/// Cloud SQL の IAM データベース認証で接続するデータソースを組み立てる。
/// </summary>
public static class CloudSqlIamDataSourceFactory
{
    private const string SqlLoginScope = "https://www.googleapis.com/auth/sqlservice.login";

    // アクセストークンの有効期間が 1 時間のため、その半分で更新して期限切れの接続が生まれないようにする。
    private static readonly TimeSpan SuccessRefreshInterval = TimeSpan.FromMinutes(30);

    // 起動直後の資格情報取得失敗から短時間で回復させるため、成功時より大幅に短い間隔で取り直す。
    private static readonly TimeSpan FailureRefreshInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// IAM データベース認証のアクセストークンをパスワードとして供給するデータソースを返す。
    /// </summary>
    /// <param name="connectionString">接続先と接続ユーザー (Username) を含む接続文字列。パスワードは含めない。</param>
    /// <returns>アクセストークンを定期的に取り直して接続するデータソース。</returns>
    public static NpgsqlDataSource Create(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        if (string.IsNullOrEmpty(builder.ConnectionStringBuilder.Username))
        {
            throw new InvalidOperationException(
                "IAM database auth requires Username in the connection string (the IAM database user of this service)");
        }

        builder.UsePeriodicPasswordProvider(
            async (_, cancellationToken) =>
            {
                var credential = (await GoogleCredential.GetApplicationDefaultAsync(cancellationToken))
                    .CreateScoped(SqlLoginScope);
                return await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(
                    cancellationToken: cancellationToken);
            },
            SuccessRefreshInterval,
            FailureRefreshInterval);

        return builder.Build();
    }
}
