using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Provides read-only access to card definitions.
/// Engine depends only on this interface; implementation lives in the Data layer.
/// </summary>
public interface ICardCache
{
    /// <summary>Returns the card definition for <paramref name="cardNo"/>, or <c>null</c> if not found.</summary>
    /// <param name="cardNo">The card number to look up.</param>
    CardDefinition? Get(long cardNo);

    /// <summary>Returns the card definition for <paramref name="cardNo"/>. Throws if not found.</summary>
    /// <param name="cardNo">The card number to look up.</param>
    /// <returns>The matching <see cref="CardDefinition"/>.</returns>
    CardDefinition MustGet(long cardNo);

    /// <summary>Returns all loaded card definitions keyed by card number.</summary>
    IReadOnlyDictionary<long, CardDefinition> All();

    /// <summary>Gets the total number of loaded card definitions.</summary>
    int Count { get; }
}
