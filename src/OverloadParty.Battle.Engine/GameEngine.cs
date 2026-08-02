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
    private readonly IInitiativeCatalog _initiatives;
    private readonly IClock _clock;

    /// <summary>Initializes a new instance of <see cref="GameEngine"/>.</summary>
    /// <param name="repo">The game persistence layer.</param>
    /// <param name="cardCache">Read-only card definitions.</param>
    /// <param name="effects">The effect registry (card effect handlers).</param>
    /// <param name="initiatives">ID から施策を解決するカタログ。</param>
    /// <param name="clock">タイムバンクの計測に使う時刻の供給元。</param>
    public GameEngine(
        IGameRepository repo, ICardCache cardCache, IEffectRegistry effects,
        IInitiativeCatalog initiatives, IClock clock)
    {
        _repo = repo;
        _cardCache = cardCache;
        _effects = effects;
        _initiatives = initiatives;
        _clock = clock;
    }

    /// <summary>Gets the configured effect registry.</summary>
    public IEffectRegistry EffectRegistry => _effects;

    /// <summary>Gets the configured initiative catalog.</summary>
    public IInitiativeCatalog InitiativeCatalog => _initiatives;

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
            gameID, deck1, deck2, firstPlayer, _cardCache, _clock);

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

            return Task.FromResult<IReadOnlyList<GameEvent>>([]);
        }, ct: ct);

        if (gameOverResult is not null)
        {
            await _repo.FinishGame(game.GameID, gameOverResult.WinnerNum, gameOverResult.Reason, ct);
        }

        return gameOverResult;
    }

    /// <summary>
    /// Forfeit はゲームを即座に強制決着で終了します
    /// </summary>
    /// <param name="game">The game metadata.</param>
    /// <param name="playerNum">The forfeiting player's number (1 or 2).</param>
    /// <param name="reason">The reason for the forfeit.</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>強制決着処理の結果として相手の勝利を表すアクション結果。</returns>
    public async Task<ActionResult> Forfeit(
        Game game, long playerNum, WinReason reason,
        CancellationToken ct = default)
    {
        EnsurePlaying(game);

        var opponentNum = playerNum == 1 ? 2 : 1;
        return await FinishGameWithResult(game.GameID, opponentNum, reason.ToWireString(), ct);
    }

    /// <summary>
    /// ForfeitBoth はゲームを両者強制決着 (勝者なし) で即座に終了します
    /// </summary>
    /// <param name="game">The game metadata.</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>引き分けを表すアクション結果。</returns>
    public async Task<ActionResult> ForfeitBoth(
        Game game, CancellationToken ct = default)
    {
        EnsurePlaying(game);

        return await FinishGameWithResult(game.GameID, 0, WinReason.Disconnect.ToWireString(), ct);
    }

    /// <summary>
    /// Persists the game outcome and builds the corresponding action result.
    /// </summary>
    /// <param name="gameID">対象ゲームの ID。</param>
    /// <param name="winnerNum">勝者のプレイヤー番号 (0 は引き分け)。</param>
    /// <param name="reason">勝敗が確定した理由の wire 文字列。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>勝敗確定情報を含むアクション結果。</returns>
    private async Task<ActionResult> FinishGameWithResult(
        string gameID, long winnerNum, string reason, CancellationToken ct)
    {
        await _repo.FinishGame(gameID, winnerNum, reason, ct);
        return new ActionResult
        {
            GameOver = new GameOverResult(winnerNum, reason),
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
        EnsurePlaying(game);

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

            DeductElapsedTime(state, _clock);

            var timeoutResult = WinConditionChecker.CheckTimeout(state);
            if (timeoutResult is not null)
            {
                actionResult = new ActionResult { GameOver = timeoutResult };
                return Task.FromResult<IReadOnlyList<GameEvent>>(actionResult.Events);
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
                    state, game, playerNum, (SelectSlotRequest)actionData, _cardCache, _effects);
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
                        state, game, playerNum, _cardCache, _effects, _clock),
                    ActionType.DiscardHand => DiscardProcessor.Process(
                        state, game, playerNum, (DiscardHandRequest)actionData, _cardCache, _effects, _clock),
                    ActionType.UseIgnition => UseIgnitionProcessor.Process(
                        state, game, playerNum, (UseIgnitionRequest)actionData, _cardCache, _effects),
                    ActionType.UseInitiative => UseInitiativeProcessor.Process(
                        state, game, playerNum, (UseInitiativeRequest)actionData, _cardCache, _effects, _initiatives),
                    _ => throw new GameRuleException($"unknown action type: {actionType}")
                };
            }

            if (actionType != ActionType.SelectSlot && state.PendingSlotSelects.Count > 0)
            {
                actionResult.ShouldSelectSlot = true;
            }

            actionResult.GameOver ??= WinConditionChecker.Check(state, game);

            return Task.FromResult<IReadOnlyList<GameEvent>>(actionResult.Events);
        }, pending, ct);

        if (actionResult.GameOver is { } over)
        {
            await _repo.FinishGame(game.GameID, over.WinnerNum, over.Reason, ct);
        }

        return actionResult;
    }

    /// <summary>
    /// Throws if the game is not in the Playing state.
    /// </summary>
    /// <param name="game">対象ゲームのメタデータ。</param>
    private static void EnsurePlaying(Game game)
    {
        if (game.Status != GameStatus.Playing)
        {
            throw new GameRuleException("game is not in playing state");
        }
    }

    /// <summary>
    /// ターン開始時刻からの経過秒数をターンプレイヤーのタイムバンクから差し引く。
    /// </summary>
    /// <param name="state">対象のゲーム状態。</param>
    /// <param name="clock">現在時刻の供給元。</param>
    internal static void DeductElapsedTime(BattleGameState state, IClock clock)
    {
        // 1 秒未満の端数を切り捨てたまま基準時刻を進めると、短い間隔の操作を繰り返す限り
        // タイムバンクが減らなくなるため、差し引いた分だけ基準時刻を進めて端数を持ち越す。
        var elapsed = (long)(clock.UtcNow - state.TurnStartedAt).TotalSeconds;
        if (elapsed <= 0)
        {
            return;
        }

        var remaining = state.GetTimeBank(state.ActivePlayer) - elapsed;
        state.SetTimeBank(state.ActivePlayer, remaining);
        state.TurnStartedAt = state.TurnStartedAt.AddSeconds(elapsed);
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
            state, myField, oppField, hand, budget, insightPool, _cardCache, _effects, _initiatives);
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
