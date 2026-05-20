namespace OverloadParty.Battle.Data.Firestore;

/// <summary>
/// Read-only access to the dynamic game_config values stored in Cloud Firestore.
/// Values are operational tuning knobs (exp rewards, battle limits, etc.) that can
/// be updated from the Google Cloud console without a redeploy.
/// </summary>
public interface IGameConfigRepository
{
    /// <summary>
    /// Returns the int64 value for the given key. Throws
    /// <see cref="NotFoundException"/> if the document is absent (fail-fast).
    /// </summary>
    /// <param name="key">取得対象の設定キー。</param>
    /// <param name="ct">キャンセレーショントークン。</param>
    /// <returns>キーに紐づく int64 値。</returns>
    Task<long> GetInt64Async(string key, CancellationToken ct = default);
}
