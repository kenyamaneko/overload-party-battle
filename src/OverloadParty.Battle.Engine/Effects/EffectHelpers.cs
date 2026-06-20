using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// 効果固有のフィールド走査・カウント・フィルタ構築ヘルパー。
/// </summary>
public static class EffectHelpers
{
    // --- Field Scanning ---

    /// <summary>
    /// Counts the number of resources in the opponent's backend zone.
    /// </summary>
    /// <param name="state">Current game state.</param>
    /// <param name="playerNum">The player whose opponent's backend is counted.</param>
    /// <returns>Number of backend resources.</returns>
    public static int CountOpponentBackend(BattleGameState state, long playerNum)
    {
        long oppNum = state.OpponentOf(playerNum);
        var field = state.GetField(oppNum);
        return field.Backend.Count();
    }

    // --- Filter constructors ---

    /// <summary>
    /// Creates a filter predicate matching cards of the given faction.
    /// </summary>
    /// <param name="faction">Faction to match (empty string matches all).</param>
    /// <returns>A predicate for card definition filtering.</returns>
    public static Func<CardDefinition, bool> FactionFilter(string faction)
        => card => faction.Length == 0 || card.Faction == faction;

    /// <summary>
    /// Creates a filter predicate matching cards of the given faction and card type.
    /// </summary>
    /// <param name="faction">Faction to match (empty string matches all).</param>
    /// <param name="isType">Predicate to test the card type string.</param>
    /// <returns>A predicate for card definition filtering.</returns>
    public static Func<CardDefinition, bool> FactionAndTypeFilter(string faction, Func<string, bool> isType)
        => card => (faction.Length == 0 || card.Faction == faction) && isType(card.CardType);

    /// <summary>
    /// Creates a filter predicate matching a specific card number.
    /// </summary>
    /// <param name="cardId">Card ID to match.</param>
    /// <returns>A predicate for card definition filtering.</returns>
    public static Func<CardDefinition, bool> CardIdFilter(string cardId)
        => card => card.CardId == cardId;

    /// <summary>card.CardType が filterValue と一致するか判定 (category 専用、厳密一致)。</summary>
    public static bool MatchesCardType(CardDefinition card, string filterValue)
        => card.CardType == filterValue;

    /// <summary>card.CardType が filterValues のいずれかと一致するか判定。</summary>
    public static bool MatchesAnyCardType(CardDefinition card, IEnumerable<string> filterValues)
        => filterValues.Any(v => card.CardType == v);

    /// <summary>card.Subtype が filterValue と一致するか判定。</summary>
    public static bool MatchesSubtype(CardDefinition card, string filterValue)
        => card.Subtype == filterValue;

    /// <summary>card.Subtype が filterValues のいずれかと一致するか判定。</summary>
    public static bool MatchesAnySubtype(CardDefinition card, IEnumerable<string> filterValues)
        => filterValues.Any(v => card.Subtype == v);
}
