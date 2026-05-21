using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc.Strategies;

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
    //  Main Phase
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// メインフェーズで実行するアクション列を決定します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲーム。</param>
    /// <param name="npcPlayerNum">NPC のプレイヤー番号。</param>
    /// <param name="available">エンジンが事前計算した実行可能アクション一覧。</param>
    /// <returns>NPC が試行するアクション列。</returns>
    public List<NpcAction> DecideMainPhaseActions(
        BattleGameState state, Game game, long npcPlayerNum, List<AvailableAction> available)
    {
        var ctx = BuildContext(state, npcPlayerNum);
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

        var insightPool = state.GetInsightPool(npcPlayerNum);
        if (insightPool > 0)
        {
            actions.AddRange(_monetize.Decide(ctx, available, insightPool));
        }

        actions.Add(MakeEndPhaseAction());
        return actions;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Battle Phase
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// バトルフェーズで実行するアクション列を決定します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲーム。</param>
    /// <param name="npcPlayerNum">NPC のプレイヤー番号。</param>
    /// <param name="available">エンジンが事前計算した実行可能アクション一覧。</param>
    /// <returns>NPC が試行するアクション列。</returns>
    public List<NpcAction> DecideBattlePhaseActions(
        BattleGameState state, Game game, long npcPlayerNum, List<AvailableAction> available)
    {
        var selfField = state.GetField(npcPlayerNum);
        var oppField = state.GetField(state.OpponentOf(npcPlayerNum));
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
    //  Discard
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 手札上限超過分として捨てるカードを決定します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="npcPlayerNum">NPC のプレイヤー番号。</param>
    /// <param name="discardCount">捨てるべき枚数。</param>
    /// <returns>捨てるカードの InstanceID 列。</returns>
    public List<string> DecideDiscard(BattleGameState state, long npcPlayerNum, int discardCount)
    {
        var ctx = BuildContext(state, npcPlayerNum);
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
    /// アタッチメント/プラットフォーム/リアクティブが手札に残っている
    /// = フィールドが埋まっていてすぐに出せない可能性が高いので先に捨てる。
    /// インシデント/ストラテジーはいつでも使えるカードなので、
    /// 手札に残しているのはタイミングを狙っている可能性が高く、最後まで残す。
    /// </summary>
    private (int TypeRank, int Priority) EvaluateCardKeepPriority(UndeployedCard handCard, DecisionContext ctx)
    {
        var card = NpcSharedHelpers.ResolveCard(_cc, handCard.CardID);

        return card.CardType switch
        {
            CardTypes.Attachment => (0, GetAttachmentPriority(card)),
            CardTypes.Platform => (1, NpcSharedHelpers.ResolveDeployPriority(_config, _cc, card, ctx)),
            CardTypes.Reactive => (2, GetReactivePriority(card)),
            CardTypes.Incident => (4, 0),
            CardTypes.Strategy => (5, 0),
            // リソース（Compute, Database, ObjectStorage 等）
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
    //  Slot Select
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 保留中のスロット選択への応答を決定します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="npcPlayerNum">NPC のプレイヤー番号。</param>
    /// <returns>選択アクション。応答対象がなければ null。</returns>
    public NpcAction? DecideSlotSelect(BattleGameState state, long npcPlayerNum)
    {
        var pending = state.PendingSlotSelects.FirstOrDefault();
        if (pending is null || pending.PlayerNum != npcPlayerNum || pending.ValidZones.Count == 0)
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
    /// 保留中の reactive 選択への応答を決定します。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="npcPlayerNum">NPC のプレイヤー番号。</param>
    /// <param name="pending">解決対象の選択待ち状態。</param>
    /// <returns>解決アクション。候補が無い場合は null。</returns>
    public NpcAction? DecidePendingEffectChoice(
        BattleGameState state, long npcPlayerNum, PendingEffectChoice pending)
    {
        if (pending.ChooserPlayerNum != npcPlayerNum || pending.Candidates.Count == 0)
        {
            return null;
        }

        // baseline AI は先頭候補を deterministic に選ぶ。
        return new NpcAction
        {
            ActionType = ActionTypes.ResolvePendingChoice,
            Data = new ResolvePendingChoiceRequest
            {
                ChosenId = pending.Candidates[0],
            },
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  Attack target selection
    // ═══════════════════════════════════════════════════════════════

    private string ResolveAttackTarget(List<string>? validTargets, Field selfField, Field oppField)
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
    //  Game phase overlay
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

    private DecisionContext BuildContext(BattleGameState state, long npcPlayerNum)
    {
        var field = state.GetField(npcPlayerNum);
        var oppField = state.GetField(state.OpponentOf(npcPlayerNum));
        var hand = state.GetHand(npcPlayerNum);
        var budget = state.GetBudget(npcPlayerNum);
        return new DecisionContext(field, oppField, hand, budget, _cc)
        {
            CurrentTurn = state.CurrentTurn,
        };
    }

    private static NpcAction MakeEndPhaseAction() =>
        new() { ActionType = ActionTypes.EndPhase };
}
