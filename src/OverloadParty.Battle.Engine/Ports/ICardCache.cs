using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Ports;

/// <summary>
/// Provides read-only access to card definitions.
/// Engine depends only on this interface; implementation lives in the Data layer.
/// </summary>
public interface ICardCache
{
    /// <summary>Returns the card definition for <paramref name="cardId"/>, or <c>null</c> if not found.</summary>
    /// <param name="cardId">The card ID to look up.</param>
    CardDefinition? Get(string cardId);

    /// <summary>Returns the card definition for <paramref name="cardId"/>. Throws if not found.</summary>
    /// <param name="cardId">The card ID to look up.</param>
    /// <returns>The matching <see cref="CardDefinition"/>.</returns>
    CardDefinition MustGet(string cardId);

    /// <summary>Returns all loaded card definitions keyed by card ID.</summary>
    /// <returns>カード ID をキーとするカード定義の読み取り専用辞書。</returns>
    IReadOnlyDictionary<string, CardDefinition> All();

    /// <summary>Gets the total number of loaded card definitions.</summary>
    int Count { get; }
}
