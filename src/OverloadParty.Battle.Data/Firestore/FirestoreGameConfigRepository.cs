using Google.Cloud.Firestore;
using Grpc.Core;

namespace OverloadParty.Battle.Data.Firestore;

/// <summary>
/// Firestore-backed implementation of <see cref="IGameConfigRepository"/>.
/// Collection: <c>game_config</c>, document ID = key, field <c>value</c> (int64).
/// </summary>
public class FirestoreGameConfigRepository : IGameConfigRepository
{
    private const string CollectionName = "game_config";
    private const string ValueField = "value";

    private readonly FirestoreDb _db;

    public FirestoreGameConfigRepository(FirestoreDb db)
    {
        _db = db;
    }

    /// <summary>指定キーの int64 値を Firestore から取得する。ドキュメント不在時は <see cref="NotFoundException"/> を投げる。</summary>
    /// <param name="key">取得対象のドキュメント ID 兼設定キー。</param>
    /// <param name="ct">キャンセレーショントークン。</param>
    /// <returns>キーに紐づく int64 値。</returns>
    public async Task<long> GetInt64Async(string key, CancellationToken ct = default)
    {
        DocumentSnapshot snap;
        try
        {
            snap = await _db.Collection(CollectionName).Document(key).GetSnapshotAsync(ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new NotFoundException($"game config \"{key}\" not found", ex);
        }

        if (!snap.Exists)
        {
            throw new NotFoundException($"game config \"{key}\" not found");
        }

        return snap.GetValue<long>(ValueField);
    }
}
