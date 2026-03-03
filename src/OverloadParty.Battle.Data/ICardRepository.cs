using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data;

public interface ICardRepository
{
    Task<List<CardDefinition>> FindAll(CancellationToken ct = default);
    Task<CardDefinition?> FindByCardNo(long cardNo, CancellationToken ct = default);
}
