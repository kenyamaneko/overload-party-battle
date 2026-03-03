using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Provides read-only access to card definitions.
/// Engine depends only on this interface; implementation lives in the Data layer.
/// </summary>
public interface ICardCache
{
    CardDefinition? Get(long cardNo);
    CardDefinition MustGet(long cardNo);
    IReadOnlyDictionary<long, CardDefinition> All();
    int Count { get; }
}
