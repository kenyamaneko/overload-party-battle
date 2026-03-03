namespace OverloadParty.Battle.Data.Mock;

/// <summary>
/// In-memory game config repository for local development.
/// Returns default values for all configs.
/// </summary>
public class MockGameConfigRepository : IGameConfigRepository
{
    private readonly Dictionary<string, long> _values = new();

    public void Set(string key, long value) => _values[key] = value;

    public Task<long> GetInt64(string key, long fallback, CancellationToken ct = default)
    {
        return Task.FromResult(_values.GetValueOrDefault(key, fallback));
    }
}
