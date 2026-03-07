using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Persistence contract for card definitions.
/// </summary>
public interface ICardRepository
{
    /// <summary>Returns all card definitions.</summary>
    Task<List<CardDefinition>> FindAll(CancellationToken ct = default);

    /// <summary>Returns the card definition for <paramref name="cardNo"/>, or <c>null</c> if not found.</summary>
    /// <param name="cardNo">The card number to look up.</param>
    Task<CardDefinition?> FindByCardNo(long cardNo, CancellationToken ct = default);
}
