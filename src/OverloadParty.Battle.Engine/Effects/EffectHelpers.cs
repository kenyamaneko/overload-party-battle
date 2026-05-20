using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// エフェクト固有のフィールド走査・カウント・フィルタ構築ヘルパー。
/// </summary>
public static class EffectHelpers
{
    // --- Field Scanning ---

    /// <summary>
    /// Counts face-up resources and active supports of the given faction on a field.
    /// </summary>
    /// <param name="field">The field to scan.</param>
    /// <param name="faction">Faction to match.</param>
    /// <param name="cc">Card cache for definition lookups.</param>
    /// <returns>Total count of matching resources and supports.</returns>
    public static int CountFactionCards(Field field, string faction, ICardCache cc)
    {
        var resourceCount = FieldHelpers.AllFaceUpResources(field)
            .Count(r => cc.Get(r.CardID)?.Faction == faction);

        var supportCount = field.Support
            .Count(s => s.DeployingTurnsLeft <= 0 && cc.Get(s.CardID)?.Faction == faction);

        return resourceCount + supportCount;
    }

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

    /// <summary>
    /// Effect DSL の card_type フィルタは category 名 (Compute/Data/Platform...) と
    /// subtype 名 (VM/Container/Database...) を区別せず受け付けるため CardType と
    /// Subtype の両方に対して dual-match する。
    /// </summary>
    /// <param name="card">判定対象のカード定義。</param>
    /// <param name="filterValue">マッチング対象の値。</param>
    /// <returns>CardType または Subtype が一致すれば true。</returns>
    public static bool MatchesCardType(CardDefinition card, string filterValue)
        => card.CardType == filterValue || card.Subtype == filterValue;

    /// <summary>
    /// 任意の filterValues のいずれかに dual-match するか判定。
    /// </summary>
    /// <param name="card">判定対象のカード定義。</param>
    /// <param name="filterValues">マッチング対象の値の集合。</param>
    /// <returns>いずれかと dual-match すれば true。</returns>
    public static bool MatchesAnyCardType(CardDefinition card, IEnumerable<string> filterValues)
        => filterValues.Any(v => MatchesCardType(card, v));
}
