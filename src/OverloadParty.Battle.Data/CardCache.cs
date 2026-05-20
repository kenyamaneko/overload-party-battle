using OverloadParty.Battle.Engine.Ports;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data;

/// <summary>
/// In-memory card definition cache. Loaded at startup, optionally refreshed periodically.
/// Implements ICardCache so the Engine can access card definitions without Data layer dependency.
/// </summary>
public class CardCache : ICardCache
{
    private readonly ReaderWriterLockSlim _lock = new();
    private Dictionary<string, CardDefinition> _cards = new();

    /// <summary>キャッシュからカード定義を取得する。</summary>
    /// <param name="cardId">カード ID。</param>
    /// <returns>該当するカード定義。存在しなければ null。</returns>
    public CardDefinition? Get(string cardId)
    {
        _lock.EnterReadLock();
        try
        {
            return _cards.GetValueOrDefault(cardId);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>キャッシュからカード定義を取得し、なければ例外を投げる。</summary>
    /// <param name="cardId">カード ID。</param>
    /// <returns>該当するカード定義。</returns>
    public CardDefinition MustGet(string cardId)
    {
        return Get(cardId) ?? throw new InvalidOperationException($"card_id {cardId} not found in cache");
    }

    /// <summary>キャッシュに保持している全カード定義のスナップショットを返す。</summary>
    /// <returns>カード ID をキーとするカード定義の読み取り専用辞書。</returns>
    public IReadOnlyDictionary<string, CardDefinition> All()
    {
        _lock.EnterReadLock();
        try
        {
            return new Dictionary<string, CardDefinition>(_cards);
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
    /// Load cards from a pre-built list (e.g., parsed from JSON in the Server layer
    /// or fetched from the card service via <see cref="CardServiceClient"/>).
    /// </summary>
    /// <param name="cards">キャッシュに投入するカード定義の列。</param>
    public void LoadFromList(IEnumerable<CardDefinition> cards)
    {
        ReplaceCards(cards.ToList());
    }

    /// <summary>
    /// Inject a single card for unit testing.
    /// </summary>
    /// <param name="cardId">カード ID。</param>
    /// <param name="card">注入するカード定義。</param>
    public void InjectForTest(string cardId, CardDefinition card)
    {
        _lock.EnterWriteLock();
        try { _cards[cardId] = card; }
        finally { _lock.ExitWriteLock(); }
    }

    private void ReplaceCards(List<CardDefinition> cards)
    {
        var dict = cards.ToDictionary(c => c.CardId);

        _lock.EnterWriteLock();
        try { _cards = dict; }
        finally { _lock.ExitWriteLock(); }
    }
}
