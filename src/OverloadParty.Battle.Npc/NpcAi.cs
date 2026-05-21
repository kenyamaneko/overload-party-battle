using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc.Strategies;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// YAML-config-driven NPC strategy.
/// フェーズ毎のエントリポイントのみを持ち、具体的な判断は Strategies/ 配下に委譲する。
/// </summary>
public class NpcAi : INpcStrategy
{
    private readonly AiConfig _config;
    private readonly ICardCache _cc;

    private readonly ImmediateActionStrategy _immediate;
    private readonly DeployStrategy _deploy;
    private readonly AttachmentDeployStrategy _attachmentDeploy;
    private readonly ReactiveDeployStrategy _reactiveDeploy;
    private readonly IgnitionStrategy _ignition;
    private readonly ScaleUpStrategy _scaleUp;
    private readonly MonetizeStrategy _monetize;

    public NpcAi(AiConfig config, ICardCache cc, IEffectRegistry effects)
    {
        _config = config;
        _cc = cc;

        _immediate = new ImmediateActionStrategy(cc, effects);
        _deploy = new DeployStrategy(config, cc);
        _attachmentDeploy = new AttachmentDeployStrategy(config, cc);
        _reactiveDeploy = new ReactiveDeployStrategy(config, cc);
        _ignition = new IgnitionStrategy(cc, effects);
        _scaleUp = new ScaleUpStrategy(config, cc);
        _monetize = new MonetizeStrategy(config, cc);
    }

    // ═══════════════════════════════════════════════════════════════
    //  メインフェーズ
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// メインフェーズで実行するアクション列を決定します。
    /// </summary>
    public List<NpcAction> DecideMainPhaseActions(GD.ClientGameState clientState)
    {
        var ctx = BuildContext(clientState);
        var available = clientState.MyView.AvailableActions ?? new List<GD.AvailableAction>();
        var activeConfig = ResolveActiveConfig(ctx);
        var playActions = ActionFilter.FilterByType(available, ActionTypes.PlayCard);
        var usedZones = new HashSet<string>();
        var actions = new List<NpcAction>();

        actions.AddRange(_immediate.Decide(ctx, playActions, usedZones, activeConfig));
        actions.AddRange(_deploy.Decide(ctx, playActions, usedZones));
        if (_config.Attachments is not null)
        {
            actions.AddRange(_attachmentDeploy.Decide(ctx, playActions, usedZones));
        }
        if (_config.Reactive is not null)
        {
            actions.AddRange(_reactiveDeploy.Decide(playActions, usedZones));
        }
        actions.AddRange(_ignition.Decide(ctx, available, activeConfig));
        actions.AddRange(_scaleUp.Decide(ctx, available));

        var insightPool = clientState.MyView.InsightPool;
        if (insightPool > 0)
        {
            actions.AddRange(_monetize.Decide(ctx, available, insightPool));
        }

        actions.Add(MakeEndPhaseAction());
        return actions;
    }

    // ═══════════════════════════════════════════════════════════════
    //  バトルフェーズ
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// バトルフェーズで実行するアクション列を決定します。
    /// </summary>
    public List<NpcAction> DecideBattlePhaseActions(GD.ClientGameState clientState)
    {
        var available = clientState.MyView.AvailableActions ?? new List<GD.AvailableAction>();
        var selfField = clientState.MyView.Field;
        var oppField = clientState.OppView.Field;
        var attackActions = ActionFilter.FilterByType(available, ActionTypes.Attack);

        var actions = attackActions
            .Select(a => new NpcAction
            {
                ActionType = ActionTypes.Attack,
                Data = new AttackRequest
                {
                    AttackerInstanceID = a.SourceInstanceID!,
                    TargetInstanceID = ResolveAttackTarget(a.ValidTargets, selfField, oppField),
                },
            })
            .ToList();

        actions.Add(MakeEndPhaseAction());
        return actions;
    }

    // ═══════════════════════════════════════════════════════════════
    //  手札調整
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 手札上限超過分として捨てるカードを決定します。
    /// </summary>
    public List<string> DecideDiscard(GD.ClientGameState clientState, int discardCount)
    {
        var ctx = BuildContext(clientState);
        return ctx.Hand
            .OrderBy(h => EvaluateCardKeepPriority(h, ctx))
            .Take(discardCount)
            .Select(h => h.InstanceID)
            .ToList();
    }

    /// <summary>
    /// 手札のカードを「残したい度」で評価。(TypeRank, Priority) のタプルが低いほど先に捨てる。
    ///
    /// 捨てる順:
    ///   アタッチメント → プラットフォーム → リアクティブ → リソース → インシデント → ストラテジー
    /// 同タイプ内は config の優先度が低いものから捨てる。
    ///
    /// アタッチメント / プラットフォーム / リアクティブが手札に残っている
    /// = フィールドが埋まっていてすぐに出せない可能性が高いので先に捨てる。
    /// インシデント / ストラテジーはいつでも使えるカードなので、
    /// 手札に残しているのはタイミングを狙っている可能性が高く、最後まで残す。
    /// </summary>
    private (int TypeRank, int Priority) EvaluateCardKeepPriority(GD.UndeployedCard handCard, DecisionContext ctx)
    {
        var card = NpcSharedHelpers.ResolveCard(_cc, handCard.CardID);

        return card.CardType switch
        {
            CardTypes.Attachment => (0, GetAttachmentPriority(card)),
            CardTypes.Platform => (1, NpcSharedHelpers.ResolveDeployPriority(_config, _cc, card, ctx)),
            CardTypes.Reactive => (2, GetReactivePriority(card)),
            CardTypes.Incident => (4, 0),
            CardTypes.Strategy => (5, 0),
            _ when !FieldHelpers.IsImmediateType(card.CardType) =>
                (3, NpcSharedHelpers.ResolveDeployPriority(_config, _cc, card, ctx)),
            _ => throw new InvalidOperationException(
                $"Unexpected card type '{card.CardType}' for card '{card.CardId}' in discard evaluation"),
        };
    }

    private int GetAttachmentPriority(CardDefinition card)
    {
        if (_config.Attachments is null || !_config.Attachments.TryGetValue(card.CardId, out var entry))
        {
            throw new InvalidOperationException(
                $"No attachment config for card '{card.CardId}' in model '{_config.Model}'");
        }
        return entry.Priority;
    }

    private int GetReactivePriority(CardDefinition card)
    {
        if (_config.Reactive is null || !_config.Reactive.Priorities.TryGetValue(card.CardId, out var pri))
        {
            throw new InvalidOperationException(
                $"No reactive config for card '{card.CardId}' in model '{_config.Model}'");
        }
        return pri;
    }

    // ═══════════════════════════════════════════════════════════════
    //  スロット選択 / 効果中のプレイヤー選択
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 効果由来のスロット選択への応答を決定します。
    /// </summary>
    public NpcAction? DecideSlotSelect(GD.ClientGameState clientState)
    {
        var pending = clientState.MyView.PendingSlotSelect;
        if (pending is null || pending.ValidZones.Count == 0)
        {
            return null;
        }

        var zone = ActionFilter.ParseZoneStr(pending.ValidZones[0])
            ?? throw new InvalidOperationException(
                $"Failed to parse zone '{pending.ValidZones[0]}' for slot select");

        return new NpcAction
        {
            ActionType = ActionTypes.SelectSlot,
            Data = new SelectSlotRequest
            {
                Zone = zone.Zone,
                Index = zone.Index,
            },
        };
    }

    /// <summary>
    /// 効果処理中のプレイヤー選択 (PendingEffectChoice) への応答を決定します。
    /// 候補は ClientGameState.MyView.AvailableActions 中の resolve_pending_choice variant として現れる。
    /// </summary>
    public NpcAction? DecidePendingEffectChoice(GD.ClientGameState clientState)
    {
        var pending = clientState.PendingEffectChoice;
        if (pending is null || pending.ChooserPlayerNum != clientState.MyView.PlayerNum)
        {
            return null;
        }

        var available = clientState.MyView.AvailableActions ?? new List<GD.AvailableAction>();
        var first = available.FirstOrDefault(a => a.Type == ActionTypes.ResolvePendingChoice);
        if (first is null)
        {
            return null;
        }

        // baseline AI は先頭候補を deterministic に選ぶ。
        // ChoiceKind ごとに chosen_id の出所が異なる (AvailableActions.EnumerateResolvePendingChoiceActions)。
        var chosenId = pending.ChoiceKind == ChoiceKinds.HandCard
            ? first.CardID
            : first.ValidTargets?.FirstOrDefault()
              ?? throw new InvalidOperationException(
                  "resolve_pending_choice action missing validTargets for field-target choice");

        return new NpcAction
        {
            ActionType = ActionTypes.ResolvePendingChoice,
            Data = new ResolvePendingChoiceRequest
            {
                ChosenId = chosenId,
            },
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  攻撃ターゲット選択
    // ═══════════════════════════════════════════════════════════════

    private string ResolveAttackTarget(List<string>? validTargets, GD.Field selfField, GD.OpponentField oppField)
    {
        if (!(validTargets?.Count > 0))
        {
            throw new InvalidOperationException("Attack action has no valid targets");
        }

        var spec = _config.TargetSelection.Attack
            ?? throw new InvalidOperationException(
                $"No attack target selection configured in model '{_config.Model}'");

        return TargetSelector.ResolveFromValid(spec, validTargets, selfField, oppField, _cc)
            ?? throw new InvalidOperationException("No valid attack target found on opponent field");
    }

    // ═══════════════════════════════════════════════════════════════
    //  ゲーム進行フェーズによる overlay
    // ═══════════════════════════════════════════════════════════════

    private AiConfig ResolveActiveConfig(DecisionContext ctx)
    {
        var late = _config.GamePhases?.Late;
        if (late is null || !GuardChecker.CheckPhaseCondition(late.Condition, ctx, _cc))
        {
            return _config;
        }

        return new AiConfig
        {
            Model = _config.Model,
            Faction = _config.Faction,
            Deck = _config.Deck,
            Budget = _config.Budget,
            GamePhases = _config.GamePhases,
            Deploy = _config.Deploy,
            ImmediateCards = _config.ImmediateCards,
            EffectPriorities = MergeEffectPriorities(_config.EffectPriorities, late.EffectPriorities),
            TargetSelection = MergeTargetSelection(_config.TargetSelection, late.TargetSelection),
            ScaleUp = _config.ScaleUp,
            Monetize = _config.Monetize,
            Attachments = _config.Attachments,
            Reactive = _config.Reactive,
            SlotSelect = _config.SlotSelect,
        };
    }

    private static TargetSelectionConfig MergeTargetSelection(
        TargetSelectionConfig baseConfig, TargetSelectionConfig? overlay)
    {
        if (overlay is null)
        {
            return baseConfig;
        }
        return new TargetSelectionConfig
        {
            Attack = overlay.Attack ?? baseConfig.Attack,
            SingleDamage = overlay.SingleDamage ?? baseConfig.SingleDamage,
            Debuff = overlay.Debuff ?? baseConfig.Debuff,
            Buff = overlay.Buff ?? baseConfig.Buff,
            Heal = overlay.Heal ?? baseConfig.Heal,
        };
    }

    private static Dictionary<string, EffectPriorityEntry> MergeEffectPriorities(
        Dictionary<string, EffectPriorityEntry> basePri,
        Dictionary<string, EffectPriorityEntry>? overrides)
    {
        if (overrides is null)
        {
            return basePri;
        }

        var merged = new Dictionary<string, EffectPriorityEntry>(basePri);
        foreach (var (key, val) in overrides)
        {
            merged[key] = val;
        }
        return merged;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private DecisionContext BuildContext(GD.ClientGameState clientState)
    {
        return new DecisionContext(
            clientState.MyView.Field,
            clientState.OppView.Field,
            clientState.MyView.Hand,
            clientState.MyView.Budget,
            _cc)
        {
            CurrentTurn = clientState.CurrentTurn,
        };
    }

    private static NpcAction MakeEndPhaseAction() =>
        new() { ActionType = ActionTypes.EndPhase };
}
