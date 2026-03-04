using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data.Mock;

/// <summary>
/// Mock card repository backed by the in-memory CardCache.
/// Cards are loaded from JSON at startup; this wrapper satisfies the ICardRepository interface.
/// </summary>
public class MockCardRepository(ICardCache cardCache) : ICardRepository
{
    public Task<List<CardDefinition>> FindAll(CancellationToken ct = default)
    {
        var all = cardCache.All().Values.ToList();
        return Task.FromResult(all);
    }

    public Task<CardDefinition?> FindByCardNo(long cardNo, CancellationToken ct = default)
    {
        return Task.FromResult(cardCache.Get(cardNo));
    }
}
