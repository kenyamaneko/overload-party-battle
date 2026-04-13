namespace OverloadParty.Battle.Data.Firestore;

/// <summary>
/// Read-only access to the dynamic game_config values stored in Cloud Firestore.
/// Values are operational tuning knobs (exp rewards, battle limits, etc.) that can
/// be updated from the GCP console without a redeploy.
/// </summary>
public interface IGameConfigRepository
{
    /// <summary>
    /// Returns the int64 value for the given key. Throws
    /// <see cref="NotFoundException"/> if the document is absent (fail-fast).
    /// </summary>
    Task<long> GetInt64Async(string key, CancellationToken ct = default);
}
