using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Persistence contract for player decks and owned cards.
/// </summary>
public interface IDeckRepository
{
    /// <summary>Creates a new deck with its card list.</summary>
    /// <param name="deck">The deck metadata.</param>
    /// <param name="cards">The cards belonging to the deck.</param>
    Task Create(Deck deck, List<DeckCard> cards, CancellationToken ct = default);

    /// <summary>Returns all decks owned by the specified player.</summary>
    /// <param name="playerID">The player's ID.</param>
    Task<List<Deck>> FindByPlayerID(string playerID, CancellationToken ct = default);

    /// <summary>Returns a specific deck, or <c>null</c> if not found.</summary>
    /// <param name="playerID">The player's ID.</param>
    /// <param name="deckID">The deck ID.</param>
    Task<Deck?> FindByID(string playerID, long deckID, CancellationToken ct = default);

    /// <summary>Returns the full card list for a deck (with art numbers and instance info).</summary>
    /// <param name="playerID">The player's ID.</param>
    /// <param name="deckID">The deck ID.</param>
    Task<List<DeckCard>> GetDeckCards(string playerID, long deckID, CancellationToken ct = default);

    /// <summary>Returns the card numbers for a deck (lightweight snapshot).</summary>
    /// <param name="playerID">The player's ID.</param>
    /// <param name="deckID">The deck ID.</param>
    Task<List<DeckSnapshotCard>> GetDeckCardNos(string playerID, long deckID, CancellationToken ct = default);

    /// <summary>Returns all cards owned by the specified player.</summary>
    /// <param name="playerID">The player's ID.</param>
    Task<List<OwnedCard>> GetOwnedCards(string playerID, CancellationToken ct = default);

    /// <summary>Updates an existing deck and its card list.</summary>
    /// <param name="deck">The updated deck metadata.</param>
    /// <param name="cards">The updated card list.</param>
    Task Update(Deck deck, List<DeckCard> cards, CancellationToken ct = default);

    /// <summary>Deletes a deck.</summary>
    /// <param name="playerID">The player's ID.</param>
    /// <param name="deckID">The deck ID to delete.</param>
    Task Delete(string playerID, long deckID, CancellationToken ct = default);
}
