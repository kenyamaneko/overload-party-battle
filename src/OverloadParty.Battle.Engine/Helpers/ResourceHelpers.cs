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
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <param name="resource">ダメージ適用先のリソース。</param>
    /// <param name="ownerNum">リソースの所有プレイヤー番号。</param>
    /// <param name="amount">適用するダメージ量。</param>
    /// <returns>on_damaged トリガーで生成されたイベント一覧。</returns>
    public static List<GameEvent> ApplyDamage(
        BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects,
        DeployedResource resource, long ownerNum, long amount)
    {
        resource.Damage += amount;
        return FireOnDamaged(state, game, cc, effects, resource, ownerNum);
    }

    /// <summary>
    /// 被ダメージリソースの所有者フィールド（リソース＋サポートゾーン）を走査して on_damaged を発火する。
    /// </summary>
    private static List<GameEvent> FireOnDamaged(
        BattleGameState state, Game game, ICardCache cc, IEffectRegistry effects,
        DeployedResource damaged, long ownerNum)
    {

        var ownerField = state.GetField(ownerNum);
        var candidates = new List<EventTriggerCandidate>();

        foreach (var res in FieldHelpers.AllFaceUpResources(ownerField))
        {
            candidates.Add(EventTriggerCandidate.ForResource(res, ownerNum));
        }
        foreach (var sup in FieldHelpers.AllSupports(ownerField))
        {
            candidates.Add(EventTriggerCandidate.ForSupport(sup, ownerNum));
        }

        var (_, events) = EventTriggerFiring.Fire(
            state, game, effects, cc, TriggerType.OnDamaged, candidates,
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
    /// <param name="card">元になるカード定義。</param>
    /// <param name="instanceID">付与するインスタンス ID。</param>
    /// <param name="deployTurn">デプロイされたターン番号。</param>
    /// <param name="artNo">アート番号。</param>
    /// <returns>生成されたリソースインスタンス。</returns>
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
        else if (card.IsDataResource && card.DataResourceStats is { } ds)
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
    /// <param name="field">配置先のフィールド。</param>
    /// <param name="instance">配置するリソースインスタンス。</param>
    /// <param name="card">元になるカード定義。</param>
    public static void PlaceResourceOnField(Field field, DeployedResource instance, CardDefinition card)
    {
        if (card.IsComputeType)
        {
            if (field.Frontend.TryPlace(instance)) { return; }
            if (field.Backend.TryPlace(instance)) { return; }
            throw new GameRuleException("No empty slot for compute resource");
        }

        // ObjectStorage は Data カテゴリの中で唯一 frontend にも置ける。優先は backend。
        if (card.IsDataResource && card.Subtype == "ObjectStorage")
        {
            if (field.Backend.TryPlace(instance)) { return; }
            if (field.Frontend.TryPlace(instance)) { return; }
            throw new GameRuleException("No empty slot for ObjectStorage");
        }

        if (card.IsDataResource)
        {
            if (field.Backend.TryPlace(instance)) { return; }
            throw new GameRuleException("No empty backend slot");
        }

        throw new GameRuleException($"Card type {card.CardType}/{card.Subtype} cannot be auto-deployed");
    }

    /// <summary>
    /// 手札からカードを配置する。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">対象プレイヤー番号 (1 または 2)。</param>
    /// <param name="field">配置先のフィールド。</param>
    /// <param name="cardId">手札から配置するカードの ID。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
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
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="playerNum">対象プレイヤー番号 (1 または 2)。</param>
    /// <param name="field">配置先のフィールド。</param>
    /// <param name="repoCard">配置するリポジトリのカード。</param>
    /// <param name="overrideAV">配置リソースに上書きする可用性。0 ならカード定義の値を使う。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
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
    /// <param name="field">対象フィールド。</param>
    /// <param name="card">配置するカードの定義。</param>
    /// <returns>配置可能なスロットのワイヤー文字列一覧。</returns>
    public static List<string> BuildValidZones(Field field, CardDefinition card)
    {
        var validZones = new List<string>();

        if (ZoneValidator.IsZoneEligible(card, Zones.Frontend))
        {
            validZones.AddRange(field.Frontend.GetEmptySlotIndices().Select(i => $"{Zones.Frontend}_{i}"));
        }

        if (ZoneValidator.IsZoneEligible(card, Zones.Backend))
        {
            validZones.AddRange(field.Backend.GetEmptySlotIndices().Select(i => $"{Zones.Backend}_{i}"));
        }

        return validZones;
    }

    /// <summary>
    /// リソースを破壊する（SLAペナルティ適用、トラッシュ移動、フィールド除去）。
    /// 対象が既にフィールドに存在しなければ何もせず false を返す。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="ownerNum">リソースの所有プレイヤー番号。</param>
    /// <param name="field">破壊対象が置かれているフィールド。</param>
    /// <param name="resource">破壊するリソース。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <returns>破壊を実行すれば true、対象が既にフィールドに存在しなければ false。</returns>
    public static bool DestroyResource(BattleGameState state, long ownerNum, Field field, DeployedResource resource, ICardCache cc)
    {
        if (FieldHelpers.FindResourceByID(field, resource.InstanceID) is null)
        {
            return false;
        }

        var card = cc.MustGet(resource.CardID);

        // SLAペナルティを所有者のバジェットから差し引く（sla_penalty_reduction で軽減）
        long penalty = FieldHelpers.ApplyReduction(
            resource.TemporaryEffects, BuffTypes.SlaPenaltyReduction, card.SLAPenalty);
        long budget = state.GetBudget(ownerNum);
        state.SetBudget(ownerNum, budget - penalty);

        MoveResourceToTrash(state, ownerNum, field, resource);
        return true;
    }

    /// <summary>
    /// リソースを装備中のアタッチメントごとトラッシュへ移し、フィールドから除去する。
    /// SLAペナルティは伴わないため、破壊ではなく効果のコストとしてフィールドを離れる場合に使う。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="ownerNum">リソースの所有プレイヤー番号。</param>
    /// <param name="field">対象が置かれているフィールド。</param>
    /// <param name="resource">トラッシュへ送るリソース。</param>
    public static void MoveResourceToTrash(
        BattleGameState state, long ownerNum, Field field, DeployedResource resource)
    {
        var attachments = field.Support.Where(a => a.TargetInstanceID == resource.InstanceID).ToList();

        CardMoveHelpers.AddToTrash(state, ownerNum, resource.CardID, resource.InstanceID, resource.ArtNo);
        foreach (var att in attachments)
        {
            CardMoveHelpers.AddToTrash(state, ownerNum, att.CardID, att.InstanceID, att.ArtNo);
        }
        field.Support.RemoveAll(a => a.TargetInstanceID == resource.InstanceID);

        FieldHelpers.RemoveResourceFromField(field, resource.InstanceID);
    }

    /// <summary>
    /// リソースのランクを変更し、MaxAV/MaxTP/MaxYield を再計算する。
    /// Elastic カードは TP/Yield を ElasticBonus から動的に算出するため MaxTP/MaxYield の再計算は不要。
    /// </summary>
    /// <param name="resource">ランクを変更するリソース。</param>
    /// <param name="targetRank">変更後のランク。</param>
    /// <param name="field">リソースが置かれているフィールド。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
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
            if (card.IsDataResource)
            {
                long newYield = StatCalculator.RecalculateMaxYield(resource, card);
                resource.MaxYield = newYield;
                resource.CurrentYield = newYield;
            }
        }
    }
}
