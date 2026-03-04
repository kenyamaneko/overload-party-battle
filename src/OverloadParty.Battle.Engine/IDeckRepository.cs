using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

public interface IDeckRepository
{
    Task Create(Deck deck, List<DeckCard> cards, CancellationToken ct = default);
    Task<List<Deck>> FindByPlayerID(string playerID, CancellationToken ct = default);
    Task<Deck?> FindByID(string playerID, long deckID, CancellationToken ct = default);
    Task<List<DeckCard>> GetDeckCards(string playerID, long deckID, CancellationToken ct = default);
    Task<List<long>> GetDeckCardNos(string playerID, long deckID, CancellationToken ct = default);
    Task<List<PlayerCard>> GetPlayerCards(string playerID, CancellationToken ct = default);
    Task Update(Deck deck, List<DeckCard> cards, CancellationToken ct = default);
    Task Delete(string playerID, long deckID, CancellationToken ct = default);
}
