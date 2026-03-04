using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data;

/// <summary>
/// In-memory card definition cache. Loaded at startup, optionally refreshed periodically.
/// Implements ICardCache so the Engine can access card definitions without Data layer dependency.
/// </summary>
public class CardCache : ICardCache
{
    private readonly ReaderWriterLockSlim _lock = new();
    private Dictionary<long, CardDefinition> _cards = new();

    public CardDefinition? Get(long cardNo)
    {
        _lock.EnterReadLock();
        try
        {
            return _cards.GetValueOrDefault(cardNo);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public CardDefinition MustGet(long cardNo)
    {
        return Get(cardNo) ?? throw new InvalidOperationException($"card_no {cardNo} not found in cache");
    }

    public IReadOnlyDictionary<long, CardDefinition> All()
    {
        _lock.EnterReadLock();
        try
        {
            return new Dictionary<long, CardDefinition>(_cards);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _cards.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    /// <summary>
    /// Load all cards from a repository.
    /// </summary>
    public async Task LoadFromRepository(ICardRepository repo, CancellationToken ct = default)
    {
        var cards = await repo.FindAll(ct);
        ReplaceCards(cards);
    }

    /// <summary>
    /// Load cards from a pre-built list (e.g., parsed from JSON in the Server layer).
    /// </summary>
    public void LoadFromList(IEnumerable<CardDefinition> cards)
    {
        ReplaceCards(cards.ToList());
    }

    /// <summary>
    /// Inject a single card for unit testing.
    /// </summary>
    public void InjectForTest(long cardNo, CardDefinition card)
    {
        _lock.EnterWriteLock();
        try { _cards[cardNo] = card; }
        finally { _lock.ExitWriteLock(); }
    }

    private void ReplaceCards(List<CardDefinition> cards)
    {
        var dict = cards.ToDictionary(c => c.CardNo);

        _lock.EnterWriteLock();
        try { _cards = dict; }
        finally { _lock.ExitWriteLock(); }
    }
}
