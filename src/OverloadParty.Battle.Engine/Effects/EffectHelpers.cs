using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// エフェクト固有のフィールド走査・カウント・フィルタ構築ヘルパー。
/// </summary>
public static class EffectHelpers
{
    // --- Field Scanning ---

    public static int CountFactionCards(Field field, string faction, ICardCache cc)
    {
        var resourceCount = FieldHelpers.AllFaceUpResources(field)
            .Count(r => cc.Get(r.CardID)?.Faction == faction);

        var supportCount = field.Support
            .Count(s => s.DeployingTurnsLeft <= 0 && cc.Get(s.CardID)?.Faction == faction);

        return resourceCount + supportCount;
    }

    public static int CountOpponentBackend(GameState state, long playerNum)
    {
        long oppNum = state.OpponentOf(playerNum);
        var field = state.GetField(oppNum);
        return field.Backend.Count();
    }

    public static bool HasCardTypeOnField(Field field, string cardType, string? faction, ICardCache cc)
    {
        return FieldHelpers.AllFaceUpResources(field).Any(r =>
        {
            var card = cc.Get(r.CardID);
            return card is not null
                && card.CardType == cardType
                && (faction is null or { Length: 0 } || card.Faction == faction);
        });
    }

    // --- Filter constructors ---

    public static Func<CardDefinition, bool> FactionFilter(string faction)
        => card => faction.Length == 0 || card.Faction == faction;

    public static Func<CardDefinition, bool> FactionAndTypeFilter(string faction, Func<string, bool> isType)
        => card => (faction.Length == 0 || card.Faction == faction) && isType(card.CardType);

    public static Func<CardDefinition, bool> CardNoFilter(long cardNo)
        => card => card.CardNo == cardNo;

    public static bool IsDBType(string cardType)
        => cardType is CardTypes.Database or CardTypes.CacheDB;

    public static bool IsResourceType(string cardType)
        => cardType is CardTypes.Compute or CardTypes.Container or CardTypes.Orchestrator or CardTypes.Serverless or CardTypes.AiMl
           or CardTypes.Database or CardTypes.ObjectStorage or CardTypes.CacheDB;
}
