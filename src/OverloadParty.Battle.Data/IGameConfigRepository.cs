namespace OverloadParty.Battle.Data;

public interface IGameConfigRepository
{
    Task<long> GetInt64(string key, long fallback, CancellationToken ct = default);
}
