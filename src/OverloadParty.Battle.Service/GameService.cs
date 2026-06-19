using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Service;

/// <summary>
/// A game event paired with the post-action state snapshot for the requesting player.
/// </summary>
public class ActionEventWithState
{
    public required GameEvent Event { get; init; }
    /// <summary>
    /// Info-hidden state for the requesting player after this event was applied.
    /// Null for the player's own action events (gateway fetches opponent state separately).
    /// </summary>
    public ClientGameState? State { get; init; }
}

/// <summary>
/// Result of a player action.
/// </summary>
public class GameActionResult
{
    public GameOverResult? GameOver { get; init; }
    public ClientGameState? State { get; init; }
    public List<ActionEventWithState> Events { get; init; } = [];
    /// <summary>
    /// true when the next actor is an NPC. Gateway calls AdvanceNpcTurn
    /// repeatedly until this becomes false so each NPC action is delivered
    /// individually (system events remain attached to the correct snapshot).
    /// </summary>
    public bool NpcPending { get; init; }
}

/// <summary>
/// GameService は PvP と NPC の両方のゲーム操作を統合するファサードです
/// </summary>
public class GameService
{
    private readonly GameEngine _engine;
    private readonly IGameRepository _gameRepo;
    private readonly ICardCache _cardCache;
    private readonly NpcRunner _npcRunner;
    private readonly Dictionary<string, AiConfig> _aiConfigs;

    private static readonly string EngineVersion =
        typeof(GameEngine).Assembly.GetName().Version?.ToString() ?? "unknown";
    private static readonly string CardDataVersion =
        typeof(InitialValues).Assembly.GetName().Version?.ToString() ?? "unknown";

    public GameService(
        GameEngine engine,
        IGameRepository gameRepo,
        ICardCache cardCache,
        NpcRunner npcRunner,
        Dictionary<string, AiConfig> aiConfigs)
    {
        _engine = engine;
        _gameRepo = gameRepo;
        _cardCache = cardCache;
        _npcRunner = npcRunner;
        _aiConfigs = aiConfigs;
    }

    // ─── Game creation ──────────────────────────────────────────

    /// <summary>
    /// Creates a new PvP game from matchmaking parameters (called by Gateway).
    /// 対戦当時の player display 情報 (name / level) を battle が永続化する。account
    /// に同期依存せず、引数として渡された snapshot をそのまま信頼して保存する。
    /// </summary>
    /// <param name="player1Cards">プレイヤー 1 のデッキ snapshot。</param>
    /// <param name="player1Routine">プレイヤー 1 がセットしたルーチン施策の ID。</param>
    /// <param name="player1Special">プレイヤー 1 がセットしたスペシャル施策の ID。</param>
    /// <param name="player2Cards">プレイヤー 2 のデッキ snapshot。</param>
    /// <param name="player2Routine">プレイヤー 2 がセットしたルーチン施策の ID。</param>
    /// <param name="player2Special">プレイヤー 2 がセットしたスペシャル施策の ID。</param>
    /// <param name="playerSummaries">両プレイヤーの表示用 summary。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>初期化済みの Game。</returns>
    public async Task<Game> CreateGameFromMatch(
        List<DeckSnapshotCard> player1Cards, string player1Routine, string player1Special,
        List<DeckSnapshotCard> player2Cards, string player2Routine, string player2Special,
        IReadOnlyList<PlayerSummarySnapshot> playerSummaries,
        CancellationToken ct = default)
    {
        var deck1 = new DeckSnapshot { Cards = player1Cards, RoutineId = player1Routine, SpecialId = player1Special };
        var deck2 = new DeckSnapshot { Cards = player2Cards, RoutineId = player2Routine, SpecialId = player2Special };

        long firstPlayer = Random.Shared.Next(2) == 0 ? 1 : 2;

        var gameID = await _engine.CreateNewGame(
            deck1, deck2, firstPlayer,
            engineVersion: EngineVersion, cardDataVersion: CardDataVersion, ct: ct);

        await _gameRepo.SavePlayerSummaries(gameID, playerSummaries, ct);

        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"created game {gameID} not found");

        await _engine.RunAutoAdvance(game, ct);
        return game;
    }

    /// <summary>
    /// Creates a new NPC game with fully initialized state.
    /// 対戦当時の player summary (人間 player と NPC) を player_summary に永続化する。
    /// NPC summary は caller (gateway) が npc_model の display_name から組み立てて渡す。
    /// </summary>
    /// <param name="playerCards">人間プレイヤーのデッキ snapshot。</param>
    /// <param name="playerRoutine">人間プレイヤーがセットしたルーチン施策の ID。</param>
    /// <param name="playerSpecial">人間プレイヤーがセットしたスペシャル施策の ID。</param>
    /// <param name="npcModel">対戦相手となる NPC モデル ID。</param>
    /// <param name="playerSummaries">両プレイヤー (人間と NPC) の表示用 summary。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>初期化済みの Game。</returns>
    public async Task<Game> StartNPCBattle(
        List<DeckSnapshotCard> playerCards, string playerRoutine, string playerSpecial, string npcModel,
        IReadOnlyList<PlayerSummarySnapshot> playerSummaries,
        CancellationToken ct = default)
    {
        if (!playerCards.Any())
        {
            throw new InvalidOperationException("deck is empty");
        }

        if (!_aiConfigs.TryGetValue(npcModel, out var npcConfig))
        {
            throw new GameRuleException(
                $"No AI config found for '{npcModel}'. Available: [{string.Join(", ", _aiConfigs.Keys)}]");
        }

        var npcCards = npcConfig.Deck
            .SelectMany(e => Enumerable.Repeat(new DeckSnapshotCard { CardId = e.CardId }, e.Copies))
            .ToList();

        var deck1 = new DeckSnapshot { Cards = playerCards, RoutineId = playerRoutine, SpecialId = playerSpecial };
        var deck2 = new DeckSnapshot { DeckID = $"npc-{npcModel}", Cards = npcCards, RoutineId = npcConfig.RoutineId, SpecialId = npcConfig.SpecialId };

        long firstPlayer = Random.Shared.Next(2) == 0 ? 1 : 2;

        var gameID = await _engine.CreateNewGame(
            deck1, deck2, firstPlayer,
            npc2Model: npcModel,
            engineVersion: EngineVersion, cardDataVersion: CardDataVersion, ct: ct);

        await _gameRepo.SavePlayerSummaries(gameID, playerSummaries, ct);

        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"created game {gameID} not found");

        await _engine.RunAutoAdvance(game, ct);
        return game;
    }

    // ─── Actions ────────────────────────────────────────────────

    /// <summary>
    /// Processes a single player action. If the resulting active player is an NPC,
    /// returns NpcPending=true so the gateway can loop AdvanceNpcTurn.
    /// </summary>
    /// <param name="gameID">対象 Game の ID。</param>
    /// <param name="playerNum">アクションを実行するプレイヤー番号。</param>
    /// <param name="actionType">アクション種別。</param>
    /// <param name="actionData">アクション種別ごとのリクエスト本体。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>アクション適用後の状態・イベント・NPC 継続フラグ。</returns>
    public async Task<GameActionResult> ProcessAction(
        string gameID, long playerNum, ActionType actionType, object actionData,
        CancellationToken ct = default)
    {
        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new GameRuleException($"game {gameID} not found");

        OverloadParty.Battle.Engine.ActionResult result;
        if (actionType == ActionType.Forfeit)
        {
            var req = actionData as ForfeitRequest;
            var reason = ParseForfeitReason(req?.Reason);
            result = await _engine.Forfeit(game, playerNum, reason, ct);
        }
        else
        {
            result = await _engine.ProcessAction(game, playerNum, actionType, actionData, ct);
        }

        var allEvents = result.Events
            .Select(e =>
            {
                e.EventData = MapEventData(e, playerNum);
                return new ActionEventWithState { Event = e };
            })
            .ToList();

        var clientState = await GetStateForPlayer(gameID, playerNum, ct);

        if (result.GameOver is not null)
        {
            return new GameActionResult
            {
                GameOver = result.GameOver,
                State = clientState,
                Events = allEvents,
                NpcPending = false,
            };
        }

        var npcPending = await IsActivePlayerNpc(gameID, game, ct);
        return new GameActionResult
        {
            State = clientState,
            Events = allEvents,
            NpcPending = npcPending,
        };
    }

    // ─── State queries ──────────────────────────────────────────

    /// <summary>指定プレイヤー視点の情報秘匿済みゲーム状態を返す。</summary>
    /// <param name="gameID">対象 Game の ID。</param>
    /// <param name="playerNum">視点となるプレイヤー番号。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>視点プレイヤー向けの ClientGameState。</returns>
    public Task<ClientGameState> GetGameStateForPlayer(
        string gameID, long playerNum, CancellationToken ct = default)
        => GetStateForPlayer(gameID, playerNum, ct);

    /// <summary>指定プレイヤーがターンプレイヤーである場合の操作可能アクションを返す。</summary>
    /// <param name="gameID">対象 Game の ID。</param>
    /// <param name="playerNum">問い合わせ元のプレイヤー番号。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>ターンプレイヤーでない場合は null、それ以外は操作 UI 情報。</returns>
    public async Task<TurnControlsMessage?> GetTurnControlsForPlayer(
        string gameID, long playerNum, CancellationToken ct = default)
    {
        var state = await _gameRepo.GetGameState(gameID, ct)
            ?? throw new InvalidOperationException($"game state {gameID} not found");

        if (state.ActivePlayer != playerNum)
        {
            return null;
        }

        var hand = state.GetHand(playerNum);
        return AvailableActions.ComputeTurnControls(state, hand);
    }

    /// <summary>
    /// Processes exactly one NPC action and returns. The gateway loops on NpcPending
    /// so every NPC action is delivered with its own post-action state snapshot.
    /// </summary>
    /// <param name="gameID">対象 Game の ID。</param>
    /// <param name="ct">キャンセル用トークン。</param>
    /// <returns>NPC アクション適用後の状態・イベント・NPC 継続フラグ。</returns>
    public async Task<GameActionResult> AdvanceNpcTurn(
        string gameID, CancellationToken ct = default)
    {
        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new GameRuleException($"game {gameID} not found");

        if (game.Npc1Model is null && game.Npc2Model is null)
        {
            return new GameActionResult();
        }

        var humanPlayerNum = ResolveHumanPlayerNum(game);
        var npcResult = await _npcRunner.AdvanceOneAction(game, ct);

        if (npcResult.Events.Count == 0)
        {
            return new GameActionResult();
        }

        var clientState = await GetStateForPlayer(gameID, humanPlayerNum, ct);

        var events = npcResult.Events
            .Select(evt =>
            {
                evt.EventData = MapEventData(evt, humanPlayerNum);
                return new ActionEventWithState { Event = evt, State = clientState };
            })
            .ToList();

        return new GameActionResult
        {
            GameOver = npcResult.GameOver,
            State = clientState,
            Events = events,
            NpcPending = npcResult.NpcPending,
        };
    }

    // ─── Private helpers ────────────────────────────────────────

    private async Task<bool> IsActivePlayerNpc(string gameID, Game game, CancellationToken ct)
    {
        if (game.Npc1Model is null && game.Npc2Model is null)
        {
            return false;
        }

        var state = await _gameRepo.GetGameState(gameID, ct)
            ?? throw new InvalidOperationException($"game state {gameID} not found after ProcessAction");

        return game.GetNpcModel(state.ActivePlayer) is not null;
    }

    /// <summary>NPC でない側のプレイヤー番号を返す。</summary>
    private static long ResolveHumanPlayerNum(Game game)
    {
        if (game.Npc1Model is null)
        {
            return 1;
        }

        if (game.Npc2Model is null)
        {
            return 2;
        }

        throw new InvalidOperationException("both players are NPC");
    }

    private static WinReason ParseForfeitReason(string? reason) => reason switch
    {
        WinReasons.TurnTimeout => WinReason.TurnTimeout,
        WinReasons.Disconnect => WinReason.Disconnect,
        WinReasons.Surrender => WinReason.Surrender,
        null => throw new GameRuleException("forfeit reason is required"),
        _ => throw new GameRuleException($"unknown forfeit reason: {reason}"),
    };

    private async Task<ClientGameState> GetStateForPlayer(
        string gameID, long playerNum, CancellationToken ct)
    {
        var game = await _gameRepo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"game {gameID} not found");

        var state = await _gameRepo.GetGameState(gameID, ct)
            ?? throw new InvalidOperationException($"game state {gameID} not found");

        var clientState = GameStateView.Build(state, game, playerNum, _cardCache, _engine.EffectRegistry, _engine.InitiativeCatalog);
        var summaries = await _gameRepo.GetPlayerSummaries(gameID, ct);
        clientState.Player1Summary = BuildClientPlayerSummary(summaries, playerNum: 1);
        clientState.Player2Summary = BuildClientPlayerSummary(summaries, playerNum: 2);
        return clientState;
    }

    private static OverloadParty.GameState.PlayerSummary BuildClientPlayerSummary(
        List<PlayerSummarySnapshot> summaries, long playerNum)
    {
        var s = summaries.First(x => x.PlayerNum == playerNum);
        return new OverloadParty.GameState.PlayerSummary
        {
            Name = s.Name,
            Level = s.Level,
        };
    }

    /// <summary>
    /// Maps view-dependent event data fields for a specific player.
    /// TurnStart: converts internal ActivePlayer to viewer-relative IsMyTurn (internal → wire type).
    /// PlayCard: redacts CardId when the actor is the opponent and the card lands face-down.
    /// </summary>
    private IEventData? MapEventData(GameEvent evt, long viewerPlayerNum)
    {
        return evt.EventData switch
        {
            null => null,
            TurnStartInternalEventData ts => new TurnStartEventData
            {
                Turn = ts.Turn,
                IsMyTurn = ts.ActivePlayer == viewerPlayerNum,
            },
            PlayCardEventData pc when ShouldRedactPlayCard(evt, viewerPlayerNum, pc.CardId)
                => new PlayCardEventData
                {
                    CardId = "",
                    Zone = pc.Zone,
                    Index = pc.Index,
                    Cancelled = pc.Cancelled,
                },
            _ => evt.EventData,
        };
    }

    private bool ShouldRedactPlayCard(GameEvent evt, long viewerPlayerNum, string cardId)
    {
        if (evt.PlayerNum == viewerPlayerNum) { return false; }
        var cardDef = _cardCache.MustGet(cardId);
        return cardDef.CardType == CardTypes.Reactive || cardDef.DeployTurns > 0;
    }
}
