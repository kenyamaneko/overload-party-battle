using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Persistence contract for card definitions.
/// </summary>
public interface ICardRepository
{
    /// <summary>Returns all card definitions.</summary>
    Task<List<CardDefinition>> FindAll(CancellationToken ct = default);

    /// <summary>Returns the card definition for <paramref name="cardId"/>, or <c>null</c> if not found.</summary>
    /// <param name="cardId">The card ID to look up.</param>
    Task<CardDefinition?> FindByCardId(string cardId, CancellationToken ct = default);
}
