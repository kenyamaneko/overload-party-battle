using System.Text;
using Google.Cloud.Storage.V1;

namespace OverloadParty.Battle.Data;

/// <summary>card が Cloud Storage のバケットへ publish したマスターデータを取得する。</summary>
public static class MasterDataStorageClient
{
    private const string CardsObjectName = "cards.json";
    private const string InitiativesObjectName = "initiatives.json";

    /// <summary>バケットからカード定義と施策定義の JSON を取得する。</summary>
    /// <param name="bucketName">マスターデータを置いたバケット名。</param>
    /// <param name="cancellationToken">キャンセレーショントークン。</param>
    /// <returns>取得したカード定義と施策定義の JSON。</returns>
    public static async Task<(string CardsJson, string InitiativesJson)> DownloadAsync(
        string bucketName, CancellationToken cancellationToken = default)
    {
        using var storage = await StorageClient.CreateAsync();
        return (
            await DownloadTextAsync(storage, bucketName, CardsObjectName, cancellationToken),
            await DownloadTextAsync(storage, bucketName, InitiativesObjectName, cancellationToken));
    }

    private static async Task<string> DownloadTextAsync(
        StorageClient storage, string bucketName, string objectName, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await storage.DownloadObjectAsync(
            bucketName, objectName, buffer, cancellationToken: cancellationToken);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
