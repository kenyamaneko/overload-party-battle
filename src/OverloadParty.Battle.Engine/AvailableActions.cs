using System.Linq;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// AvailableAction はプレイヤーが実行可能な有効なアクションを表現します
/// </summary>
public class AvailableAction
{
    /// <summary>The wire action type (e.g. "play_card", "attack").</summary>
    public string Type { get; set; } = "";

    /// <summary>The instance ID of the hand card to play (play_card only).</summary>
    public string? HandInstanceID { get; set; }

    /// <summary>The card definition ID.</summary>
    public string CardID { get; set; } = "";

    /// <summary>Valid zone+slot combinations for placement (e.g. "frontend_0").</summary>
    public List<string>? ValidZones { get; set; }

    /// <summary>The source resource/support instance ID (attack, scale_up, use_effect, etc.).</summary>
    public string? SourceInstanceID { get; set; }

    /// <summary>Valid target instance IDs (attack targets, attachment targets, etc.).</summary>
    public List<string>? ValidTargets { get; set; }

    /// <summary>The target rank for scale-up actions.</summary>
    public string? TargetRank { get; set; }

    /// <summary>The target instance family for scale-up actions.</summary>
    public string? InstanceFamily { get; set; }

    /// <summary>Whether a family selection is required (scale-up to Medium/Large).</summary>
    public bool NeedsFamily { get; set; }

    /// <summary>Remaining monetize capacity for the resource (monetize only).</summary>
    public long RemainingCapacity { get; set; }

    /// <summary>The type of target the effect expects (use_effect only).</summary>
    public string? EffectTargetType { get; set; }

    /// <summary>Number of targets required for multi-target effects.</summary>
    public int RequiredCount { get; set; }

    /// <summary>Selectable options for choice-based effects.</summary>
    public List<string>? ChoiceOptions { get; set; }
}

/// <summary>
/// AvailableActions はゲーム状態とフェーズに基づいて実行可能なアクションを算出します
/// </summary>
public static class AvailableActions
{
    /// <summary>Computes whether the player can end the phase and how many cards must be discarded.</summary>
    /// <param name="state">The current game state.</param>
    /// <param name="hand">The active player's hand.</param>
    /// <returns>Turn control information for the UI.</returns>
    public static TurnControlsMessage ComputeTurnControls(BattleGameState state, List<UndeployedCard> hand)
    {
        return new TurnControlsMessage
        {
            CanEndPhase = state.CurrentPhase is Phase.Main or Phase.Battle,
            DiscardRequired = state.CurrentPhase == Phase.End
                ? Math.Max(0, hand.Count - BattleConstants.HandLimit)
                : 0,
        };
    }

    /// <summary>
    /// Enumerates all valid actions the active player can perform in the current phase.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="myField">The active player's field.</param>
    /// <param name="oppField">The opponent's field.</param>
    /// <param name="hand">The active player's hand.</param>
    /// <param name="budget">The active player's budget.</param>
    /// <param name="insightPool">The active player's insight pool.</param>
    /// <param name="cc">Card definitions cache.</param>
    /// <param name="effects">Effect registry (may be null).</param>
    /// <returns>A list of all valid actions.</returns>
    public static List<AvailableAction> GetAllAvailableActions(
        BattleGameState state,
        Field myField, Field oppField, List<UndeployedCard> hand,
        long budget, long insightPool,
        ICardCache cc, IEffectRegistry effects)
    {
        var actions = new List<AvailableAction>();

        // reactive 選択待ちのときは、選択者のみが選択を解決するアクションを提示する。
        // 非選択者は空アクションになるが「相手の割り込み処理中」フラグは別レイヤー (TurnControls) で伝える。
        if (state.PendingEffectChoice is { } pendingChoice)
        {
            return EnumerateResolvePendingChoiceActions(pendingChoice);
        }

        switch (state.CurrentPhase)
        {
            case Phase.Main:
                actions.AddRange(EnumeratePlayCardActions(state, myField, hand, budget, cc, effects));
                actions.AddRange(EnumerateScaleUpActions(myField, cc));
                actions.AddRange(EnumerateMonetizeActions(state, myField, insightPool, cc));
                actions.AddRange(EnumerateUseEffectActions(state, myField, oppField, budget, cc, effects));
                break;

            case Phase.Battle:
                actions.AddRange(EnumerateAttackActions(myField, oppField, cc));
                actions.AddRange(EnumerateUseEffectActions(state, myField, oppField, budget, cc, effects));
                break;
        }

        return actions;
    }

    private static IEnumerable<AvailableAction> EnumeratePlayCardActions(
        BattleGameState state, Field field, List<UndeployedCard> hand,
        long budget, ICardCache cc, IEffectRegistry effects)
    {
        foreach (var handCard in hand)
        {
            var card = cc.MustGet(handCard.CardID);

            var action = BuildPlayCardAction(state, field, handCard, card, budget, cc, effects);
            if (action is not null) { yield return action; }
        }
    }

    private static AvailableAction? BuildPlayCardAction(
        BattleGameState state, Field field, UndeployedCard handCard, CardDefinition card,
        long budget, ICardCache cc, IEffectRegistry effects)
    {
        return EnumExtensions.GetCategory(card.CardType) switch
        {
            CardTypeCategory.Support => BuildSupportPlayAction(state, field, handCard, card, budget, cc, effects),
            CardTypeCategory.Compute or CardTypeCategory.Data => BuildResourcePlayAction(field, handCard, card),
            _ => null,
        };
    }

    private static AvailableAction? BuildSupportPlayAction(
        BattleGameState state, Field field, UndeployedCard handCard, CardDefinition card,
        long budget, ICardCache cc, IEffectRegistry effects)
    {
        if (card.CardType == CardTypes.Attachment)
        {
            var zones = Enumerable.Range(0, field.Support.Capacity)
                .Select(i => $"{Zones.Support}_{i}")
                .ToList();
            if (zones.Count == 0) { return null; }

            var targets = FieldHelpers.AllFaceUpResources(field)
                .Select(r => r.InstanceID)
                .ToList();

            return targets.Count > 0
                ? new AvailableAction
                {
                    Type = ActionTypes.PlayCard,
                    HandInstanceID = handCard.InstanceID,
                    CardID = handCard.CardID,
                    ValidZones = zones,
                    ValidTargets = targets,
                }
                : null;
        }

        if (card.CardType == CardTypes.Incident)
        {
            if (state.GetIncidentPlayedThisTurn(state.ActivePlayer)) { return null; }
            if (TurnManager.IsFirstTurn(state.CurrentTurn)) { return null; }
        }

        // Strategy/Incident はサポートゾーンを使わず手札から直接発動
        if (FieldHelpers.IsImmediateType(card.CardType))
        {
            if (effects is not null)
            {
                var budgetReq = effects.GetBudgetRequirement(card.CardId, TriggerType.Ignition);
                if (budgetReq is not null && !budgetReq.IsSatisfied(budget)) { return null; }
            }

            var action = new AvailableAction
            {
                Type = ActionTypes.PlayCard,
                HandInstanceID = handCard.InstanceID,
                CardID = handCard.CardID,
            };

            if (!TryPopulateTrashChoice(action, state, card.CardId, cc, effects)) { return null; }

            return action;
        }

        return BuildSupportSlotAction(field, handCard);
    }

    /// <summary>
    /// Populates <see cref="AvailableAction.ValidTargets"/> and
    /// <see cref="AvailableAction.EffectTargetType"/> for effects containing a
    /// <see cref="TrashToHandOp"/>. Returns false if the op exists but no trash card
    /// passes the filter (in which case the action must be suppressed).
    /// </summary>
    private static bool TryPopulateTrashChoice(
        AvailableAction action, BattleGameState state, string cardId, ICardCache cc, IEffectRegistry effects)
    {

        var ops = effects.GetOps(cardId, TriggerType.Ignition);
        if (ops is null) { return true; }

        var trashOp = ops.OfType<TrashToHandOp>().FirstOrDefault();
        if (trashOp is null) { return true; }

        var trash = state.GetTrash(state.ActivePlayer);

        var validTargets = trashOp.Filter is null
            ? trash.Select(c => c.InstanceID).ToList()
            : trash
                .Where(c => trashOp.Filter(cc.MustGet(c.CardID)))
                .Select(c => c.InstanceID)
                .ToList();

        if (validTargets.Count == 0) { return false; }

        action.EffectTargetType = "Choice";
        action.ValidTargets = validTargets;
        return true;
    }

    private static AvailableAction? BuildSupportSlotAction(Field field, UndeployedCard handCard)
    {
        // ワイヤーフォーマット: "{zone}_{slotIndex}" — クライアント/NPC 側で _ 分割してパース
        var zones = Enumerable.Range(0, field.Support.Capacity).Select(i => $"support_{i}").ToList();
        return zones.Count > 0
            ? new AvailableAction
            {
                Type = ActionTypes.PlayCard,
                HandInstanceID = handCard.InstanceID,
                CardID = handCard.CardID,
                ValidZones = zones,
            }
            : null;
    }

    private static AvailableAction? BuildResourcePlayAction(
        Field field, UndeployedCard handCard, CardDefinition card)
    {
        var validZones = ResourceHelpers.BuildValidZones(field, card);

        return validZones.Count > 0
            ? new AvailableAction
            {
                Type = ActionTypes.PlayCard,
                HandInstanceID = handCard.InstanceID,
                CardID = handCard.CardID,
                ValidZones = validZones,
            }
            : null;
    }

    private static IEnumerable<AvailableAction> EnumerateAttackActions(
        Field myField, Field oppField, ICardCache cc)
    {
        // 有効なターゲットを決定
        bool oppHasFrontend = FieldHelpers.HasFrontendResources(oppField);
        var validTargets = new List<string>();

        var targetZone = oppHasFrontend ? oppField.Frontend : oppField.Backend;
        foreach (var res in targetZone.Where(r => r.FaceUp))
        {
            if (FieldHelpers.IsTargetShielded(res, oppField, cc)) { continue; }
            validTargets.Add(res.InstanceID);
        }

        if (validTargets.Count == 0) { yield break; }

        foreach (var attacker in myField.Frontend.Where(r => r.FaceUp))
        {
            var attackerCard = cc.MustGet(attacker.CardID);
            if (!attackerCard.IsComputeType) { continue; }
            if (attacker.HasAttacked) { continue; }
            if (FieldHelpers.HasTemporaryEffect(attacker, BuffTypes.Dormant)) { continue; }

            yield return new AvailableAction
            {
                Type = ActionTypes.Attack,
                SourceInstanceID = attacker.InstanceID,
                ValidTargets = validTargets,
            };
        }
    }

    private static readonly InstanceFamily[] AllFamilies = [InstanceFamily.M, InstanceFamily.C, InstanceFamily.R];

    private static IEnumerable<AvailableAction> EnumerateScaleUpActions(Field field, ICardCache cc)
    {
        foreach (var resource in FieldHelpers.AllFaceUpResources(field))
        {
            var card = cc.MustGet(resource.CardID);
            if (!card.Resizable) { continue; }

            if (resource.Rank is not { } currentRank || currentRank == Rank.Large) { continue; }

            // Small → Medium, Small → Large, Medium → Large の全パターンを提示
            Rank[] possibleRanks = currentRank == Rank.Small
                ? [Rank.Medium, Rank.Large]
                : [Rank.Large];

            foreach (var targetRank in possibleRanks)
            {
                // Family 選択は Small からの昇格時のみ
                if (currentRank == Rank.Small)
                {
                    foreach (var family in AllFamilies)
                    {
                        yield return new AvailableAction
                        {
                            Type = ActionTypes.ScaleUp,
                            SourceInstanceID = resource.InstanceID,
                            TargetRank = targetRank.ToWireString(),
                            InstanceFamily = family.ToWireString(),
                            NeedsFamily = true,
                        };
                    }
                }
                else
                {
                    yield return new AvailableAction
                    {
                        Type = ActionTypes.ScaleUp,
                        SourceInstanceID = resource.InstanceID,
                        TargetRank = targetRank.ToWireString(),
                        InstanceFamily = resource.InstanceFamily!.Value.ToWireString(),
                    };
                }
            }
        }
    }

    private static IEnumerable<AvailableAction> EnumerateMonetizeActions(
        BattleGameState state, Field field, long insightPool, ICardCache cc)
    {
        if (TurnManager.IsFirstTurn(state.CurrentTurn)) { yield break; }
        if (insightPool <= 0) { yield break; }

        foreach (var res in field.Backend.Where(r => r.FaceUp))
        {
            var card = cc.MustGet(res.CardID);
            if (!card.IsComputeType) { continue; }
            if (FieldHelpers.HasTemporaryEffect(res, BuffTypes.Dormant)) { continue; }

            long effectiveTP = StatCalculator.CalculateEffectiveTP(res, field, cc);
            long remaining = effectiveTP - res.MonetizedAmount;
            if (remaining <= 0) { continue; }

            yield return new AvailableAction
            {
                Type = ActionTypes.Monetize,
                SourceInstanceID = res.InstanceID,
                RemainingCapacity = remaining,
            };
        }
    }

    private static IEnumerable<AvailableAction> EnumerateUseEffectActions(
        BattleGameState state, Field myField, Field oppField,
        long budget, ICardCache cc, IEffectRegistry effects)
    {

        // フロントエンドおよびバックエンドリソース
        foreach (var resource in FieldHelpers.AllFaceUpResources(myField))
        {
            if (resource.EffectUsedThisTurn) { continue; }

            var card = cc.MustGet(resource.CardID);
            if (!effects.Has(card.CardId, TriggerType.Ignition)) { continue; }

            if (!AllPreCheckableGuardsSatisfied(state, source: resource, supSource: null, card.CardId, cc, effects)) { continue; }

            var action = new AvailableAction
            {
                Type = ActionTypes.UseEffect,
                SourceInstanceID = resource.InstanceID,
                CardID = card.CardId,
            };
            if (!TryPopulateTrashChoice(action, state, card.CardId, cc, effects)) { continue; }
            yield return action;
        }

        // サポートゾーン
        foreach (var support in FieldHelpers.AllSupports(myField))
        {
            if (support.DeployingTurnsLeft > 0) { continue; }
            if (support.EffectUsedThisTurn) { continue; }

            var card = cc.MustGet(support.CardID);
            if (!effects.Has(card.CardId, TriggerType.Ignition)) { continue; }

            if (!AllPreCheckableGuardsSatisfied(state, source: null, supSource: support, card.CardId, cc, effects)) { continue; }

            var action = new AvailableAction
            {
                Type = ActionTypes.UseEffect,
                SourceInstanceID = support.InstanceID,
                CardID = card.CardId,
            };
            if (!TryPopulateTrashChoice(action, state, card.CardId, cc, effects)) { continue; }
            yield return action;
        }
    }

    /// <summary>
    /// active 効果の top-level guard 述語を事前評価する。Target を必要としない
    /// state-only な guard (バジェット / リソース数 等) のみが対象 (target 依存 guard は
    /// プレイヤーが target を選ぶまで判定できない)。
    /// </summary>
    private static bool AllPreCheckableGuardsSatisfied(
        BattleGameState state,
        DeployedResource? source,
        DeployedSupport? supSource,
        string cardId,
        ICardCache cc,
        IEffectRegistry effects)
    {
        if (effects is not EffectRegistry registry) { return true; }
        var reg = registry.GetRegistration(cardId, TriggerType.Ignition);
        if (reg?.Block?.Guards is not { Length: > 0 } guards) { return true; }

        // Ignition は手番プレイヤーのフィールド上のカードからのみ発動するため、
        // PlayerNum = ActivePlayer で固定して述語を評価する。
        // Game メタデータは guard 述語からは参照しないため GameID のみ埋めて他は default のままにする。
        var ctx = new EffectContext
        {
            State = state,
            Game = new Game { GameID = state.GameID },
            PlayerNum = state.ActivePlayer,
            Source = source,
            SupSource = supSource,
            CardCache = cc,
            Effects = effects,
            Trigger = TriggerType.Ignition,
        };

        return guards.All(g => g.Check(ctx));
    }

    /// <summary>
    /// pending reactive choice の選択候補をアクションとして列挙します。
    /// </summary>
    /// <param name="pending">保留中の choice 情報。</param>
    /// <returns>候補ごとに 1 件の ResolvePendingChoice アクション。</returns>
    private static List<AvailableAction> EnumerateResolvePendingChoiceActions(PendingEffectChoice pending)
    {
        // クライアントは選択時に候補 ID を ResolvePendingChoiceRequest.chosen_id として送る。
        // ChoiceKind ごとに何の ID なのかが変わるため、UI が解釈しやすいよう別フィールドに載せる。
        return pending.Candidates
            .Select(id => new AvailableAction
            {
                Type = ActionTypes.ResolvePendingChoice,
                SourceInstanceID = pending.EffectInstanceId,
                CardID = pending.ChoiceKind == ChoiceKinds.HandCard ? id : pending.EffectCardId,
                ValidTargets = pending.ChoiceKind == ChoiceKinds.FieldTarget ? [id] : null,
            })
            .ToList();
    }
}
