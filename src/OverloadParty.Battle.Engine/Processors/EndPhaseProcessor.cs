using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// EndPhaseProcessor はフェーズ進行とターン終了ロジック（維持コスト・インサイト生成・ターン交代）を処理します
/// </summary>
public static class EndPhaseProcessor
{
    /// <summary>
    /// Advances the current phase and processes end-of-turn logic when entering the end phase.
    /// </summary>
    /// <param name="state">The current game state.</param>
    /// <param name="game">The game metadata.</param>
    /// <param name="playerNum">The player number ending their phase.</param>
    /// <param name="cc">The card definition cache.</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <param name="clock">現在時刻の供給元。</param>
    /// <returns>The action result containing phase change events and possible game-over or discard requirements.</returns>
    public static ActionResult Process(
        BattleGameState state, Game game, long playerNum, ICardCache cc, IEffectRegistry effects, IClock clock)
    {
        var previousPhase = TurnManager.AdvancePhase(state);

        var events = new List<GameEvent>();

        if (state.CurrentPhase == Phase.End)
        {
            return ProcessEndPhaseTransition(state, game, playerNum, cc, effects, clock, events);
        }

        events.Add(new GameEvent
        {
            GameID = game.GameID,
            EventType = EventTypes.PhaseChange,
            PlayerNum = playerNum,
            EventData = new PhaseChangeEventData
            {
                PreviousPhase = previousPhase.ToWireString(),
                CurrentPhase = state.CurrentPhase.ToWireString(),
            },
        });
        return new ActionResult { Events = events };
    }

    private static ActionResult ProcessEndPhaseTransition(
        BattleGameState state, Game game, long playerNum, ICardCache cc,
        IEffectRegistry effects, IClock clock, List<GameEvent> events)
    {
        bool needsDiscard = ProcessEndPhaseLogic(state, game, playerNum, cc, effects);
        var result = new ActionResult { Events = events };

        if (needsDiscard)
        {
            result.ShouldDiscard = true;
            events.Add(new GameEvent
            {
                GameID = game.GameID,
                EventType = EventTypes.PhaseEnd,
                PlayerNum = playerNum,
                EventData = new PhaseEndEventData
                {
                    Phase = Phases.End,
                    NeedsDiscard = true,
                },
            });
            return result;
        }

        if (WinConditionChecker.CheckLaunchFailure(state, playerNum))
        {
            result.GameOver = new GameOverResult(
                state.OpponentOf(playerNum),
                WinReason.LaunchFailure.ToWireString());
            events.Add(MakeTurnEndEvent(game.GameID, playerNum, state));
            return result;
        }

        TurnManager.SwitchActivePlayer(state, clock);
        var gameOverResult = DrawPhaseProcessor.Process(state, game, cc, effects);

        events.Add(MakeTurnEndEvent(game.GameID, playerNum, state));
        events.Add(MakeTurnStartEvent(game.GameID, state));

        if (gameOverResult is not null)
        {
            result.GameOver = gameOverResult;
        }

        return result;
    }

    /// <summary>
    /// Returns true if the player needs to discard (hand > 6).
    /// </summary>
    static bool ProcessEndPhaseLogic(BattleGameState state, Game game, long playerNum, ICardCache cc, IEffectRegistry effects)
    {
        var field = state.GetField(playerNum);

        FirePassiveEffects(state, game, playerNum, field, cc, effects);
        CollectMaintenanceCost(state, playerNum, field, cc);
        GenerateInsight(state, playerNum, field, cc);
        ExpireTemporaryEffects(field);
        ResetPerTurnFlags(state, playerNum, field);

        return state.GetHand(playerNum).Count > BattleConstants.HandLimit;
    }

    static long CalculateMaintenanceCost(DeployedResource resource, CardDefinition card)
    {
        long baseCost;
        if (card.Elastic)
        {
            long intrinsicStat = StatCalculator.CalculateIntrinsicStat(resource, card);
            baseCost = Math.Max(0, intrinsicStat - card.FreeTier) * card.CostPerRequest / 100;
        }
        else
        {
            baseCost = card.MaintenanceCost * BattleConstants.GetRankMultiplier(resource.Rank);
        }

        return FieldHelpers.ApplyReduction(
            resource.TemporaryEffects, BuffTypes.MaintenanceReduction, baseCost);
    }

    static void CollectMaintenanceCost(BattleGameState state, long playerNum, Field field, ICardCache cc)
    {
        long totalMC = FieldHelpers.AllFaceUpResources(field)
            .Sum(r => CalculateMaintenanceCost(r, cc.MustGet(r.CardID)));

        state.SetBudget(playerNum, state.GetBudget(playerNum) - totalMC);
    }

    static void GenerateInsight(BattleGameState state, long playerNum, Field field, ICardCache cc)
    {
        long totalYield = 0;

        foreach (var res in field.Backend)
        {
            if (!res.FaceUp)
            {
                continue;
            }

            var card = cc.Get(res.CardID);
            if (card is null || !card.IsDataResource)
            {
                continue;
            }
            if (FieldHelpers.HasTemporaryEffect(res, BuffTypes.Dormant))
            {
                continue;
            }

            totalYield += StatCalculator.CalculateEffectiveInsight(res, field, cc);

            StatCalculator.ApplyElasticBonus(res, card);
        }

        state.SetInsightPool(playerNum, state.GetInsightPool(playerNum) + totalYield);
    }

    static void ExpireTemporaryEffects(Field field)
    {
        foreach (var resource in FieldHelpers.AllResources(field))
        {
            resource.TemporaryEffects.RemoveAll(e =>
                e.Duration is EffectDurations.ThisTurn
                    or EffectDurations.UntilNextTurnEnd
                    or EffectDurations.UntilNextOwnTurnEnd);
        }
    }

    static void ResetPerTurnFlags(BattleGameState state, long playerNum, Field field)
    {
        foreach (var resource in FieldHelpers.AllResources(field))
        {
            resource.HasAttacked = false;
            resource.EffectUsedThisTurn = false;
            resource.MonetizedAmount = 0;
        }

        foreach (var support in FieldHelpers.AllSupports(field))
        {
            support.EffectUsedThisTurn = false;
        }

        state.SetIncidentPlayedThisTurn(playerNum, false);
        state.SetRoutineUsedThisTurn(playerNum, false);
    }

    static void FirePassiveEffects(
        BattleGameState state, Game game, long playerNum, Field field,
        ICardCache cc, IEffectRegistry effects)
    {

        // リソース＋アタッチメント＋サポートを DeployOrder 昇順で収集
        var triggers = new List<(string CardId, long DeployOrder, DeployedResource? Source, DeployedSupport? SupSource)>();

        foreach (var resource in FieldHelpers.AllFaceUpResources(field))
        {
            if (HasEndPhaseHandler(effects, resource.CardID))
            {
                triggers.Add((resource.CardID, resource.DeployOrder, resource, null));
            }

            foreach (var att in field.Support.Where(a => a.TargetInstanceID == resource.InstanceID))
            {
                if (HasEndPhaseHandler(effects, att.CardID))
                {
                    triggers.Add((att.CardID, resource.DeployOrder, resource, null));
                }
            }
        }

        foreach (var support in FieldHelpers.AllSupports(field))
        {
            if (!support.FaceUp || support.DeployingTurnsLeft > 0) { continue; }
            if (HasEndPhaseHandler(effects, support.CardID))
            {
                triggers.Add((support.CardID, support.DeployOrder, null, support));
            }
        }

        triggers.Sort((a, b) => a.DeployOrder.CompareTo(b.DeployOrder));

        foreach (var (cardId, _, source, supSource) in triggers)
        {
            var handler = GetEndPhaseHandler(effects, cardId);
            if (handler is null) { continue; }

            handler(new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = playerNum,
                Source = source,
                SupSource = supSource,
                CardCache = cc,
                Effects = effects,
            });
        }
    }

    private static bool HasEndPhaseHandler(IEffectRegistry effects, string cardId)
    {
        return effects.Has(cardId, TriggerType.Passive)
            || effects.Has(cardId, TriggerType.OnEndPhase);
    }

    private static EffectHandler? GetEndPhaseHandler(IEffectRegistry effects, string cardId)
    {
        return effects.Get(cardId, TriggerType.OnEndPhase)
            ?? effects.Get(cardId, TriggerType.Passive);
    }

    private static GameEvent MakeTurnEndEvent(string gameID, long playerNum, BattleGameState state)
    {
        return new GameEvent
        {
            GameID = gameID,
            EventType = EventTypes.TurnEnd,
            PlayerNum = playerNum,
            EventData = new TurnEndEventData
            {
                Phase = Phases.End,
                NextTurn = state.CurrentTurn,
                ActivePlayer = state.ActivePlayer,
                CurrentPhase = state.CurrentPhase.ToWireString(),
            },
        };
    }

    /// <summary>ターン開始イベントを生成します。</summary>
    /// <param name="gameID">対象ゲームの ID。</param>
    /// <param name="state">現在のゲーム状態。</param>
    /// <returns>生成されたターン開始イベント。</returns>
    internal static GameEvent MakeTurnStartEvent(string gameID, BattleGameState state)
    {
        return new GameEvent
        {
            GameID = gameID,
            EventType = EventTypes.TurnStart,
            PlayerNum = null,
            EventData = new TurnStartInternalEventData
            {
                Turn = state.CurrentTurn,
                ActivePlayer = state.ActivePlayer,
            },
        };
    }
}
