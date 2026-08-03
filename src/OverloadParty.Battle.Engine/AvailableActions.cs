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

    /// <summary>The source resource/support instance ID (attack, scale_up, use_ignition, etc.).</summary>
    public string? SourceInstanceID { get; set; }

    /// <summary>Valid target instance IDs (attack targets, attachment targets, etc.).</summary>
    public List<string>? ValidTargets { get; set; }

    /// <summary>The target rank for scale-up actions.</summary>
    public string? TargetRank { get; set; }

    /// <summary>The target instance family for scale-up actions.</summary>
    public string? InstanceFamily { get; set; }

    /// <summary>Whether a family selection is required (scale-up to Medium/Large).</summary>
    public bool IsFamilyRequired { get; set; }

    /// <summary>Maximum amount allocatable in one monetize action, i.e. the effective throughput (monetize only).</summary>
    public long RemainingCapacity { get; set; }

    /// <summary>プレイヤーが対象を選ぶことを表す <see cref="EffectTargetType"/> の値。</summary>
    public const string ChoiceEffectTargetType = "Choice";

    /// <summary>The type of target the effect expects (use_ignition only).</summary>
    public string? EffectTargetType { get; set; }

    /// <summary>Number of targets required for multi-target effects.</summary>
    public int RequiredCount { get; set; }

    /// <summary>The choice category for resolve_pending_choice actions.</summary>
    public string? ChoiceKind { get; set; }

    /// <summary>Selectable options for resolve_pending_choice actions.</summary>
    public List<ChoiceOption>? ChoiceOptions { get; set; }

    /// <summary>deck_top 選択で選択するプレイヤーに開示するデッキ上端カード。各 InstanceID が ChoiceOption.Key に対応する。</summary>
    public List<UndeployedCard>? RevealedDeckTop { get; set; }

    /// <summary>The initiative kind for use_initiative actions (routine / special).</summary>
    public string? Kind { get; set; }

    /// <summary>The insight cost for use_initiative actions.</summary>
    public long Cost { get; set; }
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
    /// <param name="initiatives">Initiative catalog (may be null; use_initiative actions are omitted when null).</param>
    /// <param name="playerNum">アクションを列挙する対象のプレイヤー番号。</param>
    /// <returns>A list of all valid actions.</returns>
    public static List<AvailableAction> GetAllAvailableActions(
        BattleGameState state, long playerNum,
        Field myField, Field oppField, List<UndeployedCard> hand,
        long budget, long insightPool,
        ICardCache cc, IEffectRegistry effects, IInitiativeCatalog? initiatives = null)
    {
        var actions = new List<AvailableAction>();

        // reactive 選択待ちのときは、選択者のみが選択を解決するアクションを提示する。
        // 非選択者は空アクションになるが「相手の割り込み処理中」フラグは別レイヤー (TurnControls) で伝える。
        // 解決アクションには相手に伏せている情報 (deck_top の開示) が載るため、
        // 上位レイヤーのゲートに頼らずここで選択者に限定する。
        if (state.PendingEffectChoice is { } pendingChoice)
        {
            return pendingChoice.ChooserPlayerNum == playerNum
                ? EnumerateResolvePendingChoiceActions(state, pendingChoice)
                : [];
        }

        switch (state.CurrentPhase)
        {
            case Phase.Main:
                actions.AddRange(EnumeratePlayCardActions(state, myField, hand, budget, cc, effects));
                actions.AddRange(EnumerateScaleUpActions(myField, cc));
                actions.AddRange(EnumerateMonetizeActions(state, myField, insightPool, cc));
                actions.AddRange(EnumerateUseIgnitionActions(state, myField, oppField, budget, cc, effects));
                if (initiatives is not null)
                {
                    actions.AddRange(EnumerateUseInitiativeActions(state, insightPool, cc, effects, initiatives));
                }
                break;

            case Phase.Battle:
                actions.AddRange(EnumerateAttackActions(myField, oppField, cc));
                actions.AddRange(EnumerateUseIgnitionActions(state, myField, oppField, budget, cc, effects));
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
            CardTypeCategory.Compute or CardTypeCategory.DataResource => BuildResourcePlayAction(field, handCard, card),
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

            // 即時型は盤面に実体を持たないまま手札から発動するので、発火元のリソースは無い。
            if (!TryPopulateResourceChoice(
                    action, state, card.CardId, source: null, supSource: null, cc, effects))
            {
                return null;
            }

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

        action.EffectTargetType = AvailableAction.ChoiceEffectTargetType;
        action.ValidTargets = validTargets;
        return true;
    }

    /// <summary>
    /// 盤面のリソースから対象を選ぶ効果について <see cref="AvailableAction.ValidTargets"/> と
    /// <see cref="AvailableAction.EffectTargetType"/> を埋めます。絞り込みを満たすリソースが
    /// 1 件も無いときは false を返し、そのカードをプレイできないものとして扱います。
    /// 効果の一部が対象に依存しない場合も同じ扱いにするのは、プレイできるかどうかが
    /// カードごとに変わるとプレイヤーが理由を説明できないため。
    /// </summary>
    private static bool TryPopulateResourceChoice(
        AvailableAction action, BattleGameState state, string cardId,
        DeployedResource? source, DeployedSupport? supSource,
        ICardCache cc, IEffectRegistry effects)
    {
        var ops = effects.GetOps(cardId, TriggerType.Ignition);
        if (ops is null) { return true; }

        var selector = ops
            .Select(EffectClassifier.GetSelector)
            .OfType<ByChoiceSelector>()
            .FirstOrDefault();
        if (selector is null) { return true; }

        var opCtx = new OpContext(BuildIgnitionContext(state, cardId, source, supSource, cc, effects));
        var validTargets = selector.EnumerateCandidates(opCtx)
            .Select(r => r.InstanceID)
            .ToList();

        if (validTargets.Count == 0) { return false; }

        action.EffectTargetType = AvailableAction.ChoiceEffectTargetType;
        action.ValidTargets = validTargets;
        return true;
    }

    /// <summary>
    /// リソースの配置スロットを要する効果を、置ける場所が無い間は発動できないものとして扱います。
    /// </summary>
    /// <param name="field">効果を使うプレイヤーのフィールド。</param>
    /// <param name="cardId">効果を持つカードの ID。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>効果が配置スロットを要さないか、置ける場所があれば true。</returns>
    public static bool CanPlaceEffectDeploy(Field field, string cardId, IEffectRegistry effects)
    {
        return !SlotRequestHelpers.RequiresPlacementSlot(effects.GetOps(cardId, TriggerType.Ignition))
            || FieldHelpers.HasEmptyResourceSlot(field);
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
            if (FieldHelpers.HasTemporaryEffect(attacker, BuffTypes.CannotAttack)) { continue; }

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
            if (FieldHelpers.HasTemporaryEffect(resource, BuffTypes.Dormant)) { continue; }

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
                            IsFamilyRequired = true,
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
            if (res.MonetizedThisTurn) { continue; }

            long effectiveTP = StatCalculator.CalculateEffectiveTP(res, field, cc);
            // 割当量は 1 以上でなければならないため、上限 0 のリソースは提示しない
            if (effectiveTP <= 0) { continue; }

            yield return new AvailableAction
            {
                Type = ActionTypes.Monetize,
                SourceInstanceID = res.InstanceID,
                RemainingCapacity = effectiveTP,
            };
        }
    }

    private static IEnumerable<AvailableAction> EnumerateUseIgnitionActions(
        BattleGameState state, Field myField, Field oppField,
        long budget, ICardCache cc, IEffectRegistry effects)
    {

        // フロントエンドおよびバックエンドリソース
        foreach (var resource in FieldHelpers.AllFaceUpResources(myField))
        {
            if (resource.EffectUsedThisTurn) { continue; }
            if (FieldHelpers.HasTemporaryEffect(resource, BuffTypes.Dormant)) { continue; }

            var card = cc.MustGet(resource.CardID);
            if (!effects.Has(card.CardId, TriggerType.Ignition)) { continue; }

            if (!AllPreCheckableGuardsSatisfied(state, source: resource, supSource: null, card.CardId, cc, effects)) { continue; }
            if (!CanPlaceEffectDeploy(myField, card.CardId, effects)) { continue; }

            var action = new AvailableAction
            {
                Type = ActionTypes.UseIgnition,
                SourceInstanceID = resource.InstanceID,
                CardID = card.CardId,
            };
            if (!TryPopulateTrashChoice(action, state, card.CardId, cc, effects)) { continue; }
            if (!TryPopulateResourceChoice(action, state, card.CardId, resource, supSource: null, cc, effects)) { continue; }
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
            if (!CanPlaceEffectDeploy(myField, card.CardId, effects)) { continue; }

            var action = new AvailableAction
            {
                Type = ActionTypes.UseIgnition,
                SourceInstanceID = support.InstanceID,
                CardID = card.CardId,
            };
            if (!TryPopulateTrashChoice(action, state, card.CardId, cc, effects)) { continue; }
            if (!TryPopulateResourceChoice(action, state, card.CardId, source: null, support, cc, effects)) { continue; }
            yield return action;
        }
    }

    /// <summary>
    /// メインフェーズに使用可能な施策 (ルーチン / スペシャル) を列挙します。
    /// 先攻 T1 制限・使用回数・insight コストを満たすものだけを返します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="insightPool">手番プレイヤーの insight プール。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果レジストリ。</param>
    /// <param name="initiatives">施策カタログ。</param>
    /// <returns>使用可能な施策アクションの列挙。</returns>
    private static IEnumerable<AvailableAction> EnumerateUseInitiativeActions(
        BattleGameState state, long insightPool, ICardCache cc, IEffectRegistry effects, IInitiativeCatalog initiatives)
    {
        if (TurnManager.IsFirstTurn(state.CurrentTurn)) { yield break; }

        long activePlayer = state.ActivePlayer;
        (string Kind, bool IsUsed)[] kinds =
        [
            (InitiativeKinds.Routine, state.GetRoutineUsedThisTurn(activePlayer)),
            (InitiativeKinds.Special, state.GetSpecialUsedThisGame(activePlayer)),
        ];

        foreach (var (kind, isUsed) in kinds)
        {
            if (isUsed) { continue; }

            string initiativeId = InitiativeSelection.ResolveId(state, activePlayer, kind);
            var initiative = initiatives.GetById(initiativeId)
                ?? throw new GameRuleException($"initiative '{initiativeId}' not found");
            if (insightPool < initiative.InsightCost) { continue; }

            var action = new AvailableAction
            {
                Type = ActionTypes.UseInitiative,
                Kind = kind,
                CardID = initiativeId,
                Cost = initiative.InsightCost,
            };

            // 施策効果は EffectSourceId をキーに登録されるため、選択候補も同キーで引く。
            if (!TryPopulateTrashChoice(action, state, initiative.EffectSourceId, cc, effects)) { continue; }
            if (!TryPopulateResourceChoice(
                action, state, initiative.EffectSourceId, source: null, supSource: null, cc, effects))
            {
                continue;
            }

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

        var ctx = BuildIgnitionContext(state, cardId, source, supSource, cc, effects);

        return guards.All(g => g.Check(ctx));
    }

    /// <summary>
    /// 効果を実行せずに guard 述語やセレクタを評価するためのコンテキストを組み立てます。
    /// Ignition は手番プレイヤーのフィールド上のカードからのみ発動するため PlayerNum を
    /// ActivePlayer で固定し、guard 述語とセレクタが参照しない Game メタデータは GameID だけ埋めます。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="cardId">効果を持つカードの ID。</param>
    /// <param name="source">効果を持つリソース。サポート由来なら null。</param>
    /// <param name="supSource">効果を持つサポート。リソース由来なら null。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <returns>guard 述語とセレクタの評価に足る効果コンテキスト。</returns>
    private static EffectContext BuildIgnitionContext(
        BattleGameState state,
        string cardId,
        DeployedResource? source,
        DeployedSupport? supSource,
        ICardCache cc,
        IEffectRegistry effects)
    {
        return new EffectContext
        {
            State = state,
            Game = new Game { GameID = state.GameID },
            PlayerNum = state.ActivePlayer,
            Source = source,
            SupSource = supSource,
            CardCache = cc,
            Effects = effects,
            Trigger = TriggerType.Ignition,
            EffectCardId = cardId,
        };
    }

    /// <summary>
    /// 保留中の選択を、選択肢を載せた 1 件の ResolvePendingChoice アクションとして列挙します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="pending">保留中の choice 情報。</param>
    /// <returns>選択肢 (ChoiceOption) を持つ ResolvePendingChoice アクション 1 件。</returns>
    private static List<AvailableAction> EnumerateResolvePendingChoiceActions(
        BattleGameState state, PendingEffectChoice pending)
    {
        var options = pending.Candidates.Select(key => new ChoiceOption { Key = key }).ToList();

        return [new AvailableAction
        {
            Type = ActionTypes.ResolvePendingChoice,
            CardID = pending.EffectCardId,
            ChoiceKind = pending.ChoiceKind,
            ChoiceOptions = options,
            RevealedDeckTop = BuildRevealedDeckTop(state, pending),
        }];
    }

    /// <summary>
    /// deck_top 選択で選択するプレイヤーに開示するデッキ上端カードを候補順に返します。deck_top 以外では null。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="pending">保留中の choice 情報。</param>
    /// <returns>候補のインスタンス ID に対応するデッキ上端カード。deck_top 以外では null。</returns>
    private static List<UndeployedCard>? BuildRevealedDeckTop(
        BattleGameState state, PendingEffectChoice pending)
    {
        if (pending.ChoiceKind != ChoiceKinds.DeckTop)
        {
            return null;
        }
        var deck = state.GetRepository(pending.ChooserPlayerNum);
        return pending.Candidates
            .Select(key => deck.First(card => card.InstanceID == key))
            .ToList();
    }
}
