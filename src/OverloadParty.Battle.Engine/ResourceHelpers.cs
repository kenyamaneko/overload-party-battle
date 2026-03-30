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
    public static DeployedResource CreateDeployedResource(CardDefinition card, string instanceID, long deployTurn, long artNo = 0)
    {
        var resource = new DeployedResource
        {
            InstanceID = instanceID,
            CardID = card.CardId,
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
    public static void PlaceResourceOnField(Field field, DeployedResource instance, string cardType)
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
    public static void DeployFromHand(GameState state, long playerNum, Field field, string cardId, ICardCache cc)
    {
        var hand = state.GetHand(playerNum);
        int handIdx = hand.FindIndex(c => c.CardID == cardId);
        if (handIdx < 0)
        {
            throw new GameRuleException($"Card {cardId} not in hand");
        }

        var handCard = hand[handIdx];
        hand.RemoveAt(handIdx);

        var card = cc.MustGet(cardId);
        var instance = CreateDeployedResource(card, state.NextInstanceID(), state.CurrentTurn, handCard.ArtNo);
        PlaceResourceOnField(field, instance, card.CardType);
    }

    /// <summary>
    /// リポジトリからカードを配置する。
    /// </summary>
    public static void DeployFromRepo(GameState state, long playerNum, Field field, UndeployedCard repoCard, long overrideAV, ICardCache cc)
    {
        var repo = state.GetRepository(playerNum);
        repo.Remove(repoCard);

        var card = cc.MustGet(repoCard.CardID);
        var instance = CreateDeployedResource(card, state.NextInstanceID(), state.CurrentTurn, repoCard.ArtNo);

        if (overrideAV > 0)
        {
            instance.MaxAV = overrideAV;
            instance.Damage = 0;
        }

        PlaceResourceOnField(field, instance, card.CardType);
    }

    /// <summary>
    /// カードタイプに基づいて配置可能なスロット一覧を返す。
    /// ワイヤーフォーマット: "{zone}_{slotIndex}" (例: "frontend_0", "backend_2")
    /// </summary>
    public static List<string> BuildValidZones(Field field, string cardType)
    {
        var validZones = new List<string>();

        if (FieldHelpers.IsFrontendEligible(cardType))
        {
            validZones.AddRange(field.Frontend.EmptySlotIndices().Select(i => $"frontend_{i}"));
        }

        if (FieldHelpers.IsBackendEligible(cardType))
        {
            validZones.AddRange(field.Backend.EmptySlotIndices().Select(i => $"backend_{i}"));
        }

        return validZones;
    }

    /// <summary>
    /// リソースを破壊する（SLAペナルティ適用、マイグレーションリンククリア、トラッシュ移動、フィールド除去）。
    /// </summary>
    public static void DestroyResource(GameState state, long ownerNum, Field field, DeployedResource resource, ICardCache cc)
    {
        var card = cc.MustGet(resource.CardID);

        // SLAペナルティを所有者のバジェットから差し引く（sla_penalty_reduction で軽減）
        long penaltyReduction = resource.TemporaryEffects
            .Where(e => e.EffectType == "sla_penalty_reduction")
            .Sum(e => e.Value);
        long penalty = Math.Max(0, card.SLAPenalty - penaltyReduction);
        long budget = state.GetBudget(ownerNum);
        state.SetBudget(ownerNum, budget - penalty);

        // マイグレーションリンクをクリア
        FieldHelpers.ClearMigrationOnSourceDestroyed(field, resource);

        // while_on_field バフを除去（リソース本体 + アタッチメント）
        FieldHelpers.RemoveWhileOnFieldBuffs(field, resource.InstanceID);
        foreach (var att in resource.Attachments)
        {
            FieldHelpers.RemoveWhileOnFieldBuffs(field, att.InstanceID);
        }

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
    public static void ChangeRank(DeployedResource resource, Rank targetRank, Field field, ICardCache cc)
    {
        resource.Rank = targetRank;
        resource.MaxAV = StatCalculator.CalculateMaxAV(resource, field, cc);

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
