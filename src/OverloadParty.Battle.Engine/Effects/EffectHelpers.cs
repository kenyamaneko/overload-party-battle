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
        int count = 0;
        for (int i = 0; i < GameConstants.SlotsPerZone; i++)
        {
            if (field.Frontend[i] is { FaceUp: true } fres)
            {
                var card = cc.Get(fres.CardID);
                if (card?.Faction == faction) count++;
            }
            if (field.Backend[i] is { FaceUp: true } bres)
            {
                var card = cc.Get(bres.CardID);
                if (card?.Faction == faction) count++;
            }
            if (field.Support[i] is { } sup && sup.DeployingTurnsLeft <= 0)
            {
                var card = cc.Get(sup.CardID);
                if (card?.Faction == faction) count++;
            }
        }
        return count;
    }

    public static int CountOpponentBackend(GameState state, long playerNum)
    {
        long oppNum = state.OpponentOf(playerNum);
        var field = state.GetField(oppNum);
        int count = 0;
        for (int i = 0; i < GameConstants.SlotsPerZone; i++)
        {
            if (field.Backend[i] is not null) count++;
        }
        return count;
    }

    private static readonly HashSet<long> SecurityPlatformCardNos = [15, 37, 84];

    public static bool HasSecurityPlatform(Field field, ICardCache cc)
    {
        for (int i = 0; i < GameConstants.SlotsPerZone; i++)
        {
            if (field.Support[i] is { } sup && SecurityPlatformCardNos.Contains(sup.CardID))
                return true;
        }
        return false;
    }

    public static bool HasCardTypeOnField(Field field, string cardType, string? faction, ICardCache cc)
    {
        for (int i = 0; i < GameConstants.SlotsPerZone; i++)
        {
            if (field.Frontend[i] is { FaceUp: true } fres)
            {
                var card = cc.Get(fres.CardID);
                if (card is not null && card.CardType == cardType && (faction is null or { Length: 0 } || card.Faction == faction))
                    return true;
            }
            if (field.Backend[i] is { FaceUp: true } bres)
            {
                var card = cc.Get(bres.CardID);
                if (card is not null && card.CardType == cardType && (faction is null or { Length: 0 } || card.Faction == faction))
                    return true;
            }
        }
        return false;
    }

    // --- Deploy Helpers ---

    public static void PlaceResourceOnField(Field field, ResourceInstance instance, string cardType)
    {
        if (FieldHelpers.IsComputeType(cardType))
        {
            // Prefer frontend, fallback backend
            for (int i = 0; i < GameConstants.SlotsPerZone; i++)
            {
                if (field.Frontend[i] is null) { field.Frontend[i] = instance; return; }
            }
            for (int i = 0; i < GameConstants.SlotsPerZone; i++)
            {
                if (field.Backend[i] is null) { field.Backend[i] = instance; return; }
            }
            throw new GameRuleException("No empty slot for compute resource");
        }

        if (cardType == "ObjectStorage")
        {
            // Prefer backend, fallback frontend
            for (int i = 0; i < GameConstants.SlotsPerZone; i++)
            {
                if (field.Backend[i] is null) { field.Backend[i] = instance; return; }
            }
            for (int i = 0; i < GameConstants.SlotsPerZone; i++)
            {
                if (field.Frontend[i] is null) { field.Frontend[i] = instance; return; }
            }
            throw new GameRuleException("No empty slot for ObjectStorage");
        }

        if (FieldHelpers.IsDataType(cardType))
        {
            for (int i = 0; i < GameConstants.SlotsPerZone; i++)
            {
                if (field.Backend[i] is null) { field.Backend[i] = instance; return; }
            }
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
        => cardType is "Database" or "CacheDB";

    public static bool IsResourceType(string cardType)
        => cardType is "Compute" or "Container" or "Orchestrator" or "Serverless" or "AI/ML"
           or "Database" or "ObjectStorage" or "CacheDB" or "Datawarehouse";
}
