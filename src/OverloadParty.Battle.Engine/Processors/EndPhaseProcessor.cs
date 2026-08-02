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

    /// <summary>
    /// 選択の解決後に、中断していたエンドフェーズの残りを進めます。
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="playerNum">フェーズを終えようとしているプレイヤー番号。</param>
    /// <param name="cc">カード定義キャッシュ。</param>
    /// <param name="effects">効果ハンドラのレジストリ。</param>
    /// <param name="clock">現在時刻の供給元。</param>
    /// <param name="firedInstanceIds">中断までに発動を終えた効果のインスタンス ID。</param>
    /// <returns>エンドフェーズの残りで生じたイベントを含むアクション結果。</returns>
    public static ActionResult ResumeEndPhase(
        BattleGameState state, Game game, long playerNum, ICardCache cc,
        IEffectRegistry effects, IClock clock, List<string> firedInstanceIds)
    {
        return ProcessEndPhaseTransition(state, game, playerNum, cc, effects, clock, [], firedInstanceIds);
    }

    private static ActionResult ProcessEndPhaseTransition(
        BattleGameState state, Game game, long playerNum, ICardCache cc,
        IEffectRegistry effects, IClock clock, List<GameEvent> events,
        List<string>? firedInstanceIds = null)
    {
        if (!ProcessEndPhaseLogic(
                state, game, playerNum, cc, effects, events, firedInstanceIds ?? [], out bool needsDiscard))
        {
            // 選択待ちに入ったので、維持コスト徴収から先は選択が解決されるまで進めない。
            return new ActionResult { Events = events };
        }

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
    /// エンドフェーズの効果発動と各種精算を行います。効果が選択待ちに入った場合は精算前に打ち切ります。
    /// </summary>
    /// <param name="needsDiscard">手札が上限を超え、破棄が必要なら true。</param>
    /// <returns>最後まで進めば true、選択待ちで中断したら false。</returns>
    static bool ProcessEndPhaseLogic(
        BattleGameState state, Game game, long playerNum, ICardCache cc, IEffectRegistry effects,
        List<GameEvent> events, List<string> firedInstanceIds, out bool needsDiscard)
    {
        needsDiscard = false;
        var field = state.GetField(playerNum);

        if (!FirePassiveEffects(state, game, playerNum, field, cc, effects, events, firedInstanceIds))
        {
            return false;
        }

        CollectMaintenanceCost(state, playerNum, field, cc);
        GenerateInsight(state, playerNum, field, cc);
        ExpireTemporaryEffects(field);
        ResetPerTurnFlags(state, playerNum, field);

        needsDiscard = state.GetHand(playerNum).Count > BattleConstants.HandLimit;
        return true;
    }

    static long CalculateMaintenanceCost(DeployedResource resource, CardDefinition card)
    {
        long baseCost = StatCalculator.CalculateBaseMaintenanceCost(resource, card);

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

    /// <returns>全ての効果を発動し終えれば true、選択待ちで中断したら false。</returns>
    static bool FirePassiveEffects(
        BattleGameState state, Game game, long playerNum, Field field,
        ICardCache cc, IEffectRegistry effects, List<GameEvent> events, List<string> firedInstanceIds)
    {

        // リソース＋アタッチメント＋サポートを DeployOrder 昇順で収集
        var triggers = new List<(string CardId, long DeployOrder, DeployedResource? Source, DeployedSupport? SupSource, string EffectOwnerId)>();

        foreach (var resource in FieldHelpers.AllFaceUpResources(field))
        {
            if (HasEndPhaseHandler(effects, resource.CardID))
            {
                triggers.Add((resource.CardID, resource.DeployOrder, resource, null, resource.InstanceID));
            }

            foreach (var att in field.Support.Where(a => a.TargetInstanceID == resource.InstanceID))
            {
                if (HasEndPhaseHandler(effects, att.CardID))
                {
                    triggers.Add((att.CardID, resource.DeployOrder, resource, null, att.InstanceID));
                }
            }
        }

        foreach (var support in FieldHelpers.AllSupports(field))
        {
            if (!support.FaceUp || support.DeployingTurnsLeft > 0) { continue; }
            if (HasEndPhaseHandler(effects, support.CardID))
            {
                triggers.Add((support.CardID, support.DeployOrder, null, support, support.InstanceID));
            }
        }

        triggers.Sort((a, b) => a.DeployOrder.CompareTo(b.DeployOrder));

        foreach (var (cardId, _, source, supSource, effectOwnerId) in triggers)
        {
            // 中断前に発動を終えた効果は、再開時に二重発動させない。
            if (firedInstanceIds.Contains(effectOwnerId)) { continue; }

            var trigger = effects.Has(cardId, TriggerType.OnEndPhase)
                ? TriggerType.OnEndPhase
                : TriggerType.Passive;
            var handler = GetEndPhaseHandler(effects, cardId);
            if (handler is null) { continue; }

            var result = handler(new EffectContext
            {
                State = state,
                Game = game,
                PlayerNum = playerNum,
                Source = source,
                SupSource = supSource,
                CardCache = cc,
                Effects = effects,
                Trigger = trigger,
                EffectCardId = cardId,
                EffectInstanceId = source?.InstanceID ?? supSource?.InstanceID,
            });

            if (result.HasGuardFailed) { continue; }

            events.AddRange(result.Events);

            // 選択待ちに入ったら、後続のエンドフェーズ効果は解決後に改めて発動させる。
            if (result.PendingChoice is not null)
            {
                // 中断した効果は選択の解決で最後まで実行されるので、再開時に発動済みとして飛ばす。
                result.PendingChoice.EndPhasePlayerNum = playerNum;
                result.PendingChoice.EndPhaseFiredInstanceIds = [.. firedInstanceIds, effectOwnerId];
                state.PendingEffectChoice = result.PendingChoice;
                return false;
            }

            firedInstanceIds.Add(effectOwnerId);
        }

        return true;
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
