using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Helpers;

/// <summary>
/// リソースインスタンスのライフサイクル（生成・配置・破壊・ランク変更）を扱うヘルパー。
/// </summary>
public static class ResourceHelpers
{
    /// <summary>
    /// リソースにダメージを適用し、適用後に on_damaged トリガーを発火する単一チョークポイント。
    /// </summary>
    public static List<GameEvent> ApplyDamage(
        BattleGameState state, Game game, ICardCache cc, IEffectRegistry? effects,
        DeployedResource resource, long ownerNum, long amount)
    {
        resource.Damage += amount;
        return FireOnDamaged(state, game, cc, effects, resource, ownerNum);
    }

    /// <summary>
    /// 被ダメージリソースの所有者フィールド（リソース＋サポートゾーン）を走査して on_damaged を発火する。
    /// </summary>
    private static List<GameEvent> FireOnDamaged(
        BattleGameState state, Game game, ICardCache cc, IEffectRegistry? effects,
        DeployedResource damaged, long ownerNum)
    {
        if (effects is null) { return []; }

        var ownerField = state.GetField(ownerNum);
        var candidates = new List<EventTriggerCandidate>();

        foreach (var res in FieldHelpers.AllFaceUpResources(ownerField))
        {
            candidates.Add(new EventTriggerCandidate
            {
                CardId = res.CardID, DeployOrder = res.DeployOrder, Resource = res, OwnerNum = ownerNum,
            });
        }
        foreach (var sup in FieldHelpers.AllSupports(ownerField))
        {
            candidates.Add(new EventTriggerCandidate
            {
                CardId = sup.CardID, DeployOrder = sup.DeployOrder, Support = sup, OwnerNum = ownerNum,
            });
        }

        var (_, events) = EventTriggerFiring.Fire(
            state, effects, cc, TriggerType.OnDamaged, candidates,
            candidate => new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = ownerNum,
                Source = candidate.Resource,
                SupSource = candidate.Support,
                Target = damaged,
                EventOwnerNum = ownerNum,
                CardCache = cc,
                Effects = effects,
            });

        return events;
    }

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
    public static void PlaceResourceOnField(Field field, DeployedResource instance, CardDefinition card)
    {
        if (card.IsComputeType)
        {
            if (field.Frontend.TryPlace(instance)) { return; }
            if (field.Backend.TryPlace(instance)) { return; }
            throw new GameRuleException("No empty slot for compute resource");
        }

        // ObjectStorage は Data カテゴリの中で唯一 frontend にも置ける。優先は backend。
        if (card.IsDataType && card.Subtype == "ObjectStorage")
        {
            if (field.Backend.TryPlace(instance)) { return; }
            if (field.Frontend.TryPlace(instance)) { return; }
            throw new GameRuleException("No empty slot for ObjectStorage");
        }

        if (card.IsDataType)
        {
            if (field.Backend.TryPlace(instance)) { return; }
            throw new GameRuleException("No empty backend slot");
        }

        throw new GameRuleException($"Card type {card.CardType}/{card.Subtype} cannot be auto-deployed");
    }

    /// <summary>
    /// 手札からカードを配置する。
    /// </summary>
    public static void DeployFromHand(BattleGameState state, long playerNum, Field field, string cardId, ICardCache cc)
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
        PlaceResourceOnField(field, instance, card);
    }

    /// <summary>
    /// リポジトリからカードを配置する。
    /// </summary>
    public static void DeployFromRepo(BattleGameState state, long playerNum, Field field, UndeployedCard repoCard, long overrideAV, ICardCache cc)
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

        PlaceResourceOnField(field, instance, card);
    }

    /// <summary>
    /// カードタイプに基づいて配置可能なスロット一覧を返す。
    /// ワイヤーフォーマット: "{zone}_{slotIndex}" (例: "frontend_0", "backend_2")
    /// </summary>
    public static List<string> BuildValidZones(Field field, CardDefinition card)
    {
        var validZones = new List<string>();

        if (ZoneValidator.IsZoneEligible(card, Zones.Frontend))
        {
            validZones.AddRange(field.Frontend.EmptySlotIndices().Select(i => $"{Zones.Frontend}_{i}"));
        }

        if (ZoneValidator.IsZoneEligible(card, Zones.Backend))
        {
            validZones.AddRange(field.Backend.EmptySlotIndices().Select(i => $"{Zones.Backend}_{i}"));
        }

        return validZones;
    }

    /// <summary>
    /// リソースを破壊する（SLAペナルティ適用、マイグレーションリンククリア、トラッシュ移動、フィールド除去）。
    /// </summary>
    public static void DestroyResource(BattleGameState state, long ownerNum, Field field, DeployedResource resource, ICardCache cc)
    {
        var card = cc.MustGet(resource.CardID);

        // SLAペナルティを所有者のバジェットから差し引く（sla_penalty_reduction で軽減）
        long penalty = FieldHelpers.ApplyReduction(
            resource.TemporaryEffects, BuffTypes.SlaPenaltyReduction, card.SLAPenalty);
        long budget = state.GetBudget(ownerNum);
        state.SetBudget(ownerNum, budget - penalty);

        // while_on_field バフを除去（リソース本体 + アタッチメント）
        FieldHelpers.RemoveWhileOnFieldBuffs(field, resource.InstanceID);
        var attachments = field.Support.Where(a => a.TargetInstanceID == resource.InstanceID).ToList();
        foreach (var att in attachments)
        {
            FieldHelpers.RemoveWhileOnFieldBuffs(field, att.InstanceID);
        }

        // ホスト＋アタッチメントをトラッシュに移動
        CardMoveHelpers.AddToTrash(state, ownerNum, resource.CardID, resource.InstanceID, resource.ArtNo);
        foreach (var att in attachments)
        {
            CardMoveHelpers.AddToTrash(state, ownerNum, att.CardID, att.InstanceID, att.ArtNo);
        }
        field.Support.RemoveAll(a => a.TargetInstanceID == resource.InstanceID);

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
