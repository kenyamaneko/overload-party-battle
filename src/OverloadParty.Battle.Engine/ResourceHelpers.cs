using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// リソースインスタンスのライフサイクル（生成・配置・破壊・ランク変更）を扱うヘルパー。
/// </summary>
public static class ResourceHelpers
{
    /// <summary>
    /// カード定義からリソースインスタンスを生成する。
    /// </summary>
    public static ResourceInstance CreateResourceInstance(CardDefinition card, string instanceID, long deployTurn, long artNo = 0)
    {
        var resource = new ResourceInstance
        {
            InstanceID = instanceID,
            CardID = card.CardNo,
            ArtNo = artNo,
            Rank = card.Resizable ? Rank.Small : null,
            FaceUp = card.DeployTurns <= 0,
            DeployingTurnsLeft = card.DeployTurns,
            DeployedOnTurn = deployTurn,
        };

        if (card.IsComputeType && card.ComputeStats is { } cs)
        {
            resource.MaxAV = cs.Availability;
            resource.CurrentAV = cs.Availability;
            resource.MaxTP = cs.Throughput;
            resource.CurrentTP = cs.Throughput;
        }
        else if (card.IsDataType && card.DataStats is { } ds)
        {
            resource.MaxAV = ds.Availability;
            resource.CurrentAV = ds.Availability;
            resource.MaxYield = ds.Yield;
            resource.CurrentYield = ds.Yield;
        }

        return resource;
    }

    /// <summary>
    /// リソースをフィールドの適切なゾーンに自動配置する。
    /// </summary>
    public static void PlaceResourceOnField(Field field, ResourceInstance instance, string cardType)
    {
        if (FieldHelpers.IsComputeType(cardType))
        {
            if (field.Frontend.TryPlace(instance)) { return; }
            if (field.Backend.TryPlace(instance)) { return; }
            throw new GameRuleException("No empty slot for compute resource");
        }

        if (cardType == CardTypes.ObjectStorage)
        {
            if (field.Backend.TryPlace(instance)) { return; }
            if (field.Frontend.TryPlace(instance)) { return; }
            throw new GameRuleException("No empty slot for ObjectStorage");
        }

        if (FieldHelpers.IsDataType(cardType))
        {
            if (field.Backend.TryPlace(instance)) { return; }
            throw new GameRuleException("No empty backend slot");
        }

        throw new GameRuleException($"Card type {cardType} cannot be auto-deployed");
    }

    /// <summary>
    /// 手札からカードを配置する。
    /// </summary>
    public static void DeployFromHand(GameState state, long playerNum, Field field, long cardNo, ICardCache cc)
    {
        var hand = state.GetHand(playerNum);
        int handIdx = hand.FindIndex(c => c.CardID == cardNo);
        if (handIdx < 0)
        {
            throw new GameRuleException($"Card {cardNo} not in hand");
        }

        var handCard = hand[handIdx];
        hand.RemoveAt(handIdx);

        var card = cc.MustGet(cardNo);
        var instance = CreateResourceInstance(card, state.NextInstanceID(), state.CurrentTurn, handCard.ArtNo);
        PlaceResourceOnField(field, instance, card.CardType);
    }

    /// <summary>
    /// リポジトリからカードを配置する。
    /// </summary>
    public static void DeployFromRepo(GameState state, long playerNum, Field field, HandCard repoCard, long overrideAV, ICardCache cc)
    {
        var repo = state.GetRepository(playerNum);
        repo.Remove(repoCard);

        var card = cc.MustGet(repoCard.CardID);
        var instance = CreateResourceInstance(card, state.NextInstanceID(), state.CurrentTurn, repoCard.ArtNo);

        if (overrideAV > 0)
        {
            instance.MaxAV = overrideAV;
            instance.Damage = 0;
        }

        PlaceResourceOnField(field, instance, card.CardType);
    }

    /// <summary>
    /// リソースを破壊する（SLAペナルティ適用、マイグレーションリンククリア、トラッシュ移動、フィールド除去）。
    /// </summary>
    public static void DestroyResource(GameState state, long ownerNum, Field field, ResourceInstance resource, ICardCache cc)
    {
        var card = cc.MustGet(resource.CardID);

        // SLAペナルティを所有者のバジェットから差し引く
        long budget = state.GetBudget(ownerNum);
        state.SetBudget(ownerNum, budget - card.SLAPenalty);

        // マイグレーションリンクをクリア
        FieldHelpers.ClearMigrationOnSourceDestroyed(field, resource);

        // ホスト＋アタッチメントをトラッシュに移動
        CardMoveHelpers.AddToTrash(state, ownerNum, resource.CardID, resource.InstanceID, resource.ArtNo);
        foreach (var att in resource.Attachments)
        {
            CardMoveHelpers.AddToTrash(state, ownerNum, att.CardID, att.InstanceID, att.ArtNo);
        }

        // フィールドから除去
        FieldHelpers.RemoveResourceFromField(field, resource.InstanceID);
    }

    /// <summary>
    /// リソースのランクを変更し、MaxAV/MaxTP/MaxYield を再計算する。
    /// Elastic カードは TP/Yield を ElasticBonus から動的に算出するため MaxTP/MaxYield の再計算は不要。
    /// </summary>
    public static void ChangeRank(ResourceInstance resource, Rank targetRank, ICardCache cc)
    {
        resource.Rank = targetRank;
        resource.MaxAV = StatCalculator.CalculateMaxAV(resource, cc);

        var card = cc.MustGet(resource.CardID);
        if (!card.Elastic)
        {
            if (card.IsComputeType)
            {
                long newTP = StatCalculator.RecalculateMaxTP(resource, card);
                resource.MaxTP = newTP;
                resource.CurrentTP = newTP;
            }
            if (card.IsDataType)
            {
                long newYield = StatCalculator.RecalculateMaxYield(resource, card);
                resource.MaxYield = newYield;
                resource.CurrentYield = newYield;
            }
        }
    }
}
