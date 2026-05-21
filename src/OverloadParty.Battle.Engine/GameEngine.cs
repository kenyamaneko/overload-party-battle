using OverloadParty.Battle.Models;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Stateless game engine. All state lives in BattleGameState (persisted via IGameRepository).
/// Depends only on IGameRepository, ICardCache, and IEffectRegistry interfaces.
/// </summary>
public class GameEngine
{
    private readonly IGameRepository _repo;
    private readonly ICardCache _cardCache;
    private readonly IEffectRegistry _effects;

    /// <summary>Initializes a new instance of <see cref="GameEngine"/>.</summary>
    /// <param name="repo">The game persistence layer.</param>
    /// <param name="cardCache">Read-only card definitions.</param>
    /// <param name="effects">The effect registry (card effect handlers).</param>
    public GameEngine(IGameRepository repo, ICardCache cardCache, IEffectRegistry effects)
    {
        _repo = repo;
        _cardCache = cardCache;
        _effects = effects;
    }

    /// <summary>Gets the configured effect registry.</summary>
    public IEffectRegistry EffectRegistry => _effects;

    /// <summary>
    /// CreateNewGame はシャッフルしたデッキと初期手札で新しいゲームを作成します
    /// </summary>
    /// <param name="deck1">プレイヤー 1 のデッキスナップショット。</param>
    /// <param name="deck2">プレイヤー 2 のデッキスナップショット。</param>
    /// <param name="firstPlayer">先攻プレイヤー番号 (1 または 2)。</param>
    /// <param name="npc1Model">プレイヤー 1 が NPC のときのモデル識別子。</param>
    /// <param name="npc2Model">プレイヤー 2 が NPC のときのモデル識別子。</param>
    /// <param name="engineVersion">エンジンのバージョン。</param>
    /// <param name="cardDataVersion">カードデータのバージョン。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>作成されたゲームの ID。</returns>
    public async Task<string> CreateNewGame(
        DeckSnapshot deck1, DeckSnapshot deck2,
        long firstPlayer,
        string? npc1Model = null, string? npc2Model = null,
        string engineVersion = "", string cardDataVersion = "",
        CancellationToken ct = default)
    {
        var gameID = Guid.NewGuid().ToString("N");
        var (game, state) = GameInitializer.CreateNewGame(
            gameID, deck1, deck2, firstPlayer, _cardCache);

        game.Npc1Model = npc1Model;
        game.Npc2Model = npc2Model;
        game.EngineVersion = engineVersion;
        game.CardDataVersion = cardDataVersion;

        await _repo.CreateGame(game, state, ct);
        return gameID;
    }

    /// <summary>
    /// Runs the draw-phase auto-advance for the active player.
    /// Called at the start of each turn before player actions.
    /// </summary>
    /// <param name="game">対象ゲームのメタデータ。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>勝敗が確定した場合の結果、未確定なら null。</returns>
    public async Task<GameOverResult?> RunAutoAdvance(
        Game game, CancellationToken ct = default)
    {
        GameOverResult? gameOverResult = null;

        await _repo.UpdateGameState(game.GameID, state =>
        {
            gameOverResult = DrawPhaseProcessor.Process(state, game, _cardCache, _effects);

            return Task.CompletedTask;
        }, ct: ct);

        if (gameOverResult is not null)
        {
            await _repo.FinishGame(game.GameID, gameOverResult.WinnerNum, gameOverResult.Reason, ct);
        }

        return gameOverResult;
    }

    /// <summary>
    /// Forfeit はゲームを即座にフォーフェイト（棄権）で終了します
    /// </summary>
    /// <param name="game">The game metadata.</param>
    /// <param name="playerNum">The forfeiting player's number (1 or 2).</param>
    /// <param name="reason">The reason for the forfeit.</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>棄権処理の結果として相手の勝利を表すアクション結果。</returns>
    public async Task<ActionResult> Forfeit(
        Game game, long playerNum, WinReason reason,
        CancellationToken ct = default)
    {
        if (game.Status != GameStatus.Playing)
        {
            throw new GameRuleException("game is not in playing state");
        }

        var opponentNum = playerNum == 1 ? 2 : 1;
        await _repo.FinishGame(game.GameID, opponentNum, reason.ToWireString(), ct);
        return new ActionResult
        {
            GameOver = new GameOverResult(opponentNum, reason.ToWireString()),
        };
    }

    /// <summary>
    /// Processes a player action (play card, attack, etc.) and persists the resulting events.
    /// </summary>
    /// <param name="game">The game metadata.</param>
    /// <param name="playerNum">The acting player's number (1 or 2).</param>
    /// <param name="actionType">The type of action to process.</param>
    /// <param name="actionData">The action-specific request data.</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>アクション処理の結果イベントと勝敗確定情報を含むアクション結果。</returns>
    public async Task<ActionResult> ProcessAction(
        Game game, long playerNum, ActionType actionType, object actionData,
        CancellationToken ct = default)
    {
        if (game.Status != GameStatus.Playing)
        {
            throw new GameRuleException("game is not in playing state");
        }

        ActionResult actionResult = null!;

        var pending = new PendingAction(playerNum, actionType.ToWireString(), actionData);

        await _repo.UpdateGameState(game.GameID, state =>
        {
            // reactive 選択待ちの間は ActivePlayer 以外の chooser が解決アクションを送るため、
            // ResolvePendingChoice は「自分のターン」チェックから除外して chooser 一致で判定する。
            bool isChooserResolving = actionType == ActionType.ResolvePendingChoice
                && state.PendingEffectChoice?.ChooserPlayerNum == playerNum;
            if (!isChooserResolving && state.ActivePlayer != playerNum)
            {
                throw new GameRuleException("not your turn");
            }

            DeductElapsedTime(state);

            var timeoutResult = WinConditionChecker.CheckTimeout(state);
            if (timeoutResult is not null)
            {
                actionResult = new ActionResult { GameOver = timeoutResult };
                return Task.CompletedTask;
            }

            if (state.PendingSlotSelects.Count > 0
                && state.PendingSlotSelects[0].PlayerNum == playerNum
                && actionType != ActionType.SelectSlot)
            {
                throw new GameRuleException("slot selection required");
            }

            if (state.PendingEffectChoice is { } pendingChoice
                && pendingChoice.ChooserPlayerNum == playerNum
                && actionType != ActionType.ResolvePendingChoice)
            {
                throw new GameRuleException("reactive choice required");
            }

            if (actionType == ActionType.SelectSlot)
            {
                actionResult = SelectSlotProcessor.Process(
                    state, game, playerNum, (SelectSlotRequest)actionData, _cardCache);
            }
            else if (actionType == ActionType.ResolvePendingChoice)
            {
                actionResult = ResolvePendingChoiceProcessor.Process(
                    state, game, playerNum, (ResolvePendingChoiceRequest)actionData, _cardCache, _effects);
            }
            else
            {
                if (!TurnManager.IsActionAllowedInPhase(state.CurrentPhase, actionType))
                {
                    throw new GameRuleException($"action {actionType.ToWireString()} not allowed in phase {state.CurrentPhase.ToWireString()}");
                }

                actionResult = actionType switch
                {
                    ActionType.PlayCard => PlayCardProcessor.Process(
                        state, game, playerNum, (PlayCardRequest)actionData, _cardCache, _effects),
                    ActionType.Attack => AttackProcessor.Process(
                        state, game, playerNum, (AttackRequest)actionData, _cardCache, _effects),
                    ActionType.ScaleUp => ScaleUpProcessor.Process(
                        state, game, playerNum, (ScaleUpRequest)actionData, _cardCache, _effects),
                    ActionType.Monetize => MonetizeProcessor.Process(
                        state, game, playerNum, (MonetizeRequest)actionData, _cardCache),
                    ActionType.EndPhase => EndPhaseProcessor.Process(
                        state, game, playerNum, _cardCache, _effects),
                    ActionType.DiscardHand => DiscardProcessor.Process(
                        state, game, playerNum, (DiscardHandRequest)actionData, _cardCache, _effects),
                    ActionType.UseEffect => UseEffectProcessor.Process(
                        state, game, playerNum, (UseEffectRequest)actionData, _cardCache, _effects),
                    _ => throw new GameRuleException($"unknown action type: {actionType}")
                };
            }

            if (actionType != ActionType.SelectSlot && state.PendingSlotSelects.Count > 0)
            {
                actionResult.NeedsSlotSelect = true;
            }

            actionResult.GameOver ??= WinConditionChecker.Check(state, game);

            return Task.CompletedTask;
        }, pending, ct);

        // イベントを永続化
        var eventCount = await _repo.GetEventCount(game.GameID, ct);
        foreach (var evt in actionResult.Events)
        {
            eventCount++;
            evt.SequenceNumber = eventCount;
            evt.CreatedAt = DateTime.UtcNow;
            await _repo.AppendEvent(evt, ct);
        }

        if (actionResult.GameOver is { } over)
        {
            await _repo.FinishGame(game.GameID, over.WinnerNum, over.Reason, ct);
        }

        return actionResult;
    }

    /// <summary>
    /// Deducts elapsed time since TurnStartedAt from the active player's TimeBank
    /// and resets TurnStartedAt to now.
    /// </summary>
    /// <param name="state">対象のゲーム状態。</param>
    internal static void DeductElapsedTime(BattleGameState state)
    {
        var now = DateTime.UtcNow;
        var elapsed = (long)(now - state.TurnStartedAt).TotalSeconds;
        if (elapsed > 0)
        {
            var remaining = state.GetTimeBank(state.ActivePlayer) - elapsed;
            state.SetTimeBank(state.ActivePlayer, remaining);
        }
        state.TurnStartedAt = now;
    }

    /// <summary>
    /// Computes all valid actions available to a player in the current game state.
    /// </summary>
    /// <param name="gameID">対象ゲームの ID。</param>
    /// <param name="playerNum">アクションを算出するプレイヤー番号。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>そのプレイヤーが現在実行可能なアクション一覧。</returns>
    public async Task<List<AvailableAction>> ComputeAvailableActions(
        string gameID, long playerNum, CancellationToken ct = default)
    {
        var game = await _repo.GetGame(gameID, ct)
            ?? throw new GameRuleException($"game {gameID} not found");
        var state = await _repo.GetGameState(gameID, ct)
            ?? throw new GameRuleException($"game state {gameID} not found");

        var myField = state.GetField(playerNum);
        var oppField = state.GetField(state.OpponentOf(playerNum));
        var hand = state.GetHand(playerNum);
        long budget = state.GetBudget(playerNum);
        long insightPool = state.GetInsightPool(playerNum);

        return AvailableActions.GetAllAvailableActions(
            state, myField, oppField, hand, budget, insightPool, _cardCache, _effects);
    }

    /// <summary>
    /// Computes turn control information (whether the player can end the phase, discard count).
    /// </summary>
    /// <param name="state">現在のゲーム状態。</param>
    /// <param name="hand">対象プレイヤーの手札。</param>
    /// <returns>UI 向けのターン制御情報。</returns>
    public TurnControlsMessage ComputeTurnControls(BattleGameState state, List<UndeployedCard> hand)
    {
        return AvailableActions.ComputeTurnControls(state, hand);
    }
}
