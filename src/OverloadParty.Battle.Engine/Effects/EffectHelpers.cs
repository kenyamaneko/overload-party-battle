using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Helper functions used by effect ops for field scanning, card counting, and deployment.
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

    private static readonly HashSet<long> SecurityPlatformCardNos = [15, 37, 84];

    public static bool HasSecurityPlatform(Field field, ICardCache cc)
    {
        return field.Support
            .Any(sup => SecurityPlatformCardNos.Contains(sup.CardID));
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

    // --- Deploy Helpers ---

    public static void PlaceResourceOnField(Field field, ResourceInstance instance, string cardType)
    {
        if (FieldHelpers.IsComputeType(cardType))
        {
            // Prefer frontend, fallback backend
            if (field.Frontend.TryPlace(instance)) return;
            if (field.Backend.TryPlace(instance)) return;
            throw new GameRuleException("No empty slot for compute resource");
        }

        if (cardType == CardTypes.ObjectStorage)
        {
            // Prefer backend, fallback frontend
            if (field.Backend.TryPlace(instance)) return;
            if (field.Frontend.TryPlace(instance)) return;
            throw new GameRuleException("No empty slot for ObjectStorage");
        }

        if (FieldHelpers.IsDataType(cardType))
        {
            if (field.Backend.TryPlace(instance)) return;
            throw new GameRuleException("No empty backend slot");
        }

        throw new GameRuleException($"Card type {cardType} cannot be auto-deployed");
    }

    public static void DeployResourceFromHand(GameState state, long playerNum, long cardNo, ICardCache cc, OpContext octx)
    {
        var hand = state.GetHand(playerNum);
        int handIdx = hand.FindIndex(c => c.CardID == cardNo);
        if (handIdx < 0)
            throw new GameRuleException($"Card {cardNo} not in hand");

        hand.RemoveAt(handIdx);

        var card = cc.MustGet(cardNo);
        var field = octx.GetField(playerNum);
        var instance = FieldHelpers.CreateResourceInstance(card, state.NextInstanceID(), state.CurrentTurn);
        PlaceResourceOnField(field, instance, card.CardType);
    }

    public static void DeployResourceFromRepo(GameState state, long playerNum, long cardNo, long overrideAV, ICardCache cc, OpContext octx)
    {
        var repo = state.GetRepository(playerNum);
        int repoIdx = repo.FindIndex(c => c.CardID == cardNo);
        if (repoIdx < 0)
            throw new GameRuleException($"Card {cardNo} not in repository");

        repo.RemoveAt(repoIdx);

        var card = cc.MustGet(cardNo);
        var field = octx.GetField(playerNum);
        var instance = FieldHelpers.CreateResourceInstance(card, state.NextInstanceID(), state.CurrentTurn);

        if (overrideAV > 0)
        {
            instance.MaxAV = overrideAV;
            instance.Damage = 0;
        }

        PlaceResourceOnField(field, instance, card.CardType);
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
