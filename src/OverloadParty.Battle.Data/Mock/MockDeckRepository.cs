using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data.Mock;

/// <summary>
/// In-memory deck repository for local development.
/// </summary>
public class MockDeckRepository : IDeckRepository
{
    private readonly Lock _lock = new();
    private long _nextID = 1;
    private readonly Dictionary<(string PlayerID, long DeckID), Deck> _decks = new();
    private readonly Dictionary<(string PlayerID, long DeckID), List<DeckCard>> _deckCards = new();
    private readonly Dictionary<string, List<PlayerCard>> _playerCards = new();

    public Task Create(Deck deck, List<DeckCard> cards, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (deck.DeckID == 0)
                deck.DeckID = _nextID++;
            _decks[(deck.PlayerID, deck.DeckID)] = deck;
            _deckCards[(deck.PlayerID, deck.DeckID)] = cards;
        }
        return Task.CompletedTask;
    }

    public Task<List<Deck>> FindByPlayerID(string playerID, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var result = _decks.Values.Where(d => d.PlayerID == playerID).ToList();
            return Task.FromResult(result);
        }
    }

    public Task<Deck?> FindByID(string playerID, long deckID, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(_decks.GetValueOrDefault((playerID, deckID))); }
    }

    public Task<List<DeckCard>> GetDeckCards(string playerID, long deckID, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(_deckCards.GetValueOrDefault((playerID, deckID)) ?? []); }
    }

    public Task<List<long>> GetDeckCardNos(string playerID, long deckID, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var cards = _deckCards.GetValueOrDefault((playerID, deckID));
            if (cards is null) return Task.FromResult<List<long>>([]);
            var nos = new List<long>();
            foreach (var c in cards)
                for (int i = 0; i < c.Count; i++)
                    nos.Add(c.CardNo);
            return Task.FromResult(nos);
        }
    }

    public Task<List<PlayerCard>> GetPlayerCards(string playerID, CancellationToken ct = default)
    {
        lock (_lock) { return Task.FromResult(_playerCards.GetValueOrDefault(playerID) ?? []); }
    }

    public Task Update(Deck deck, List<DeckCard> cards, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _decks[(deck.PlayerID, deck.DeckID)] = deck;
            _deckCards[(deck.PlayerID, deck.DeckID)] = cards;
        }
        return Task.CompletedTask;
    }

    public Task Delete(string playerID, long deckID, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _decks.Remove((playerID, deckID));
            _deckCards.Remove((playerID, deckID));
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Helper for dev setup: create a deck from raw card numbers.
    /// </summary>
    public void CreateDeckFromCardNos(string playerID, string deckName, long[] cardNos)
    {
        var deckID = _nextID++;
        var deck = new Deck
        {
            PlayerID = playerID,
            DeckID = deckID,
            DeckName = deckName,
            IsValid = cardNos.Length == 30,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var grouped = cardNos.GroupBy(n => n)
            .Select(g => new DeckCard
            {
                PlayerID = playerID,
                DeckID = deckID,
                CardNo = g.Key,
                Count = g.Count(),
            }).ToList();

        _decks[(playerID, deckID)] = deck;
        _deckCards[(playerID, deckID)] = grouped;
    }
}
