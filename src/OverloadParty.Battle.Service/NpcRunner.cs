using Microsoft.Extensions.Logging;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Service;

/// <summary>
/// NpcAdvanceResult は 1 つの NPC アクション進行の結果を表現します
/// </summary>
public record NpcAdvanceResult(
    List<GameEvent> Events,
    GameOverResult? GameOver,
    bool NpcPending)
{
    /// <summary>
    /// NPC のアクションが不要であることを示す結果を返します。
    /// </summary>
    public static NpcAdvanceResult Done() => new([], null, false);
}

/// <summary>
/// Orchestrates NPC turns: resolves AI strategies, decides actions, and executes them via GameEngine.
/// Each call to AdvanceOneAction processes exactly one NPC action, matching PvP's one-action-per-call flow.
/// The gateway loops until NpcPending is false.
/// NPC は human と同じく情報秘匿済み ClientGameState から意思決定する。
/// </summary>
public class NpcRunner
{
    private readonly GameEngine _engine;
    private readonly IGameRepository _repo;
    private readonly ICardCache _cardCache;
    private readonly ILogger<NpcRunner> _logger;
    private readonly Dictionary<string, AiConfig> _aiConfigs;

    public NpcRunner(
        GameEngine engine,
        IGameRepository repo,
        ICardCache cardCache,
        Dictionary<string, AiConfig> aiConfigs,
        ILogger<NpcRunner> logger)
    {
        _engine = engine;
        _repo = repo;
        _cardCache = cardCache;
        _aiConfigs = aiConfigs;
        _logger = logger;

        foreach (var (model, config) in aiConfigs)
        {
            AiConfigValidator.Validate(config, cardCache);
        }
    }

    /// <summary>
    /// Processes exactly one NPC action and returns.
    /// Returns NpcPending=true if the active player is still an NPC after the action.
    /// The gateway calls this in a loop until NpcPending is false or GameOver is set.
    /// </summary>
    public async Task<NpcAdvanceResult> AdvanceOneAction(Game game, CancellationToken ct = default)
    {
        var gameID = game.GameID;
        var state = await _repo.GetGameState(gameID, ct)
            ?? throw new InvalidOperationException($"game state {gameID} lost");

        // 保留中の効果中プレイヤー選択は ActivePlayer ではなく chooser が解決するため、
        // ActivePlayer の NPC 判定より先に chooser が NPC かを確認する。
        if (state.PendingEffectChoice is { } pendingChoice)
        {
            var chooserAI = ResolveNpcAIForPlayer(game, pendingChoice.ChooserPlayerNum);
            if (chooserAI is null)
            {
                // chooser が human の間は NPC 側に進められる手番がない。
                return NpcAdvanceResult.Done();
            }
            return await ProcessOnePendingEffectChoice(
                game, state, pendingChoice.ChooserPlayerNum, chooserAI, ct);
        }

        var npcAI = ResolveNpcAIForPlayer(game, state.ActivePlayer);
        if (npcAI is null)
        {
            return NpcAdvanceResult.Done();
        }

        var npcPlayerNum = state.ActivePlayer;
        var clientState = BuildClientState(state, game, npcPlayerNum);

        // 保留中のスロット選択を優先処理
        if (clientState.MyView.PendingSlotSelect is not null)
        {
            return await ProcessOneSlotSelect(game, npcAI, clientState, npcPlayerNum, ct);
        }

        // 現在のフェーズのアクションを決定
        var actions = DecideActions(npcAI, clientState, game.GameID);

        if (actions.Count == 0)
        {
            return NpcAdvanceResult.Done();
        }

        // 成功するまで各アクションを試行（拒否されたものはスキップ）
        var rejections = new List<string>();
        foreach (var action in actions)
        {
            var actionType = EnumExtensions.ParseActionType(action.ActionType);
            try
            {
                game = await _repo.GetGame(gameID, ct)
                    ?? throw new InvalidOperationException($"game {gameID} lost during NPC action");

                var result = await _engine.ProcessAction(game, npcPlayerNum, actionType, action.Data, ct);
                var pending = await IsNpcPending(game, result, ct);
                return new NpcAdvanceResult(result.Events, result.GameOver, pending);
            }
            catch (GameRuleException ex)
            {
                // 個別の rejection は候補を順に試す正常系の一部なので Debug に留め、全件拒否のみ Warn を出す
                rejections.Add($"{action.ActionType}:{ex.Message}");
                _logger.LogDebug(ex, "NPC action rejected (game={GameID}, action={Action})", gameID, action.ActionType);
            }
        }

        _logger.LogWarning(
            "All NPC actions rejected (game={GameID}, phase={Phase}, tried={Tried}, rejections=[{Rejections}])",
            gameID, state.CurrentPhase, actions.Count, string.Join(" | ", rejections));
        return NpcAdvanceResult.Done();
    }

    // ─── Private ────────────────────────────────────────────────

    private GD.ClientGameState BuildClientState(BattleGameState state, Game game, long playerNum)
    {
        if (_engine.EffectRegistry is null)
        {
            throw new InvalidOperationException("EffectRegistry is not configured");
        }
        return GameStateView.Build(state, game, playerNum, _cardCache, _engine.EffectRegistry, _engine.InitiativeCatalog);
    }

    private INpcStrategy ResolveAI(string npcModel)
    {
        if (_engine.EffectRegistry is null)
        {
            throw new InvalidOperationException("EffectRegistry is not configured");
        }

        if (!_aiConfigs.TryGetValue(npcModel, out var config))
        {
            throw new InvalidOperationException(
                $"No AI config found for model '{npcModel}'. Available: [{string.Join(", ", _aiConfigs.Keys)}]");
        }

        return new NpcAi(config, _cardCache, _engine.EffectRegistry);
    }

    private INpcStrategy? ResolveNpcAIForPlayer(Game game, long playerNum)
    {
        var npcModel = game.GetNpcModel(playerNum);
        return npcModel is not null ? ResolveAI(npcModel) : null;
    }

    private async Task<NpcAdvanceResult> ProcessOneSlotSelect(
        Game game, INpcStrategy npcAI, GD.ClientGameState clientState, long npcPlayerNum,
        CancellationToken ct)
    {
        var gameID = game.GameID;
        var slotAction = npcAI.DecideSlotSelect(clientState)
            ?? throw new InvalidOperationException($"NPC failed to decide slot selection (game={gameID})");

        var slotType = EnumExtensions.ParseActionType(slotAction.ActionType);

        game = await _repo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"game {gameID} lost during NPC slot select");

        var result = await _engine.ProcessAction(game, npcPlayerNum, slotType, slotAction.Data, ct);
        var pending = await IsNpcPending(game, result, ct);
        return new NpcAdvanceResult(result.Events, result.GameOver, pending);
    }

    private async Task<NpcAdvanceResult> ProcessOnePendingEffectChoice(
        Game game, BattleGameState state, long chooserPlayerNum, INpcStrategy chooserAI,
        CancellationToken ct)
    {
        var gameID = game.GameID;
        var clientState = BuildClientState(state, game, chooserPlayerNum);
        var choiceAction = chooserAI.DecidePendingEffectChoice(clientState)
            ?? throw new InvalidOperationException(
                $"NPC failed to decide pending effect choice (game={gameID})");

        var actionType = EnumExtensions.ParseActionType(choiceAction.ActionType);

        game = await _repo.GetGame(gameID, ct)
            ?? throw new InvalidOperationException($"game {gameID} lost during NPC pending choice");

        var result = await _engine.ProcessAction(game, chooserPlayerNum, actionType, choiceAction.Data, ct);
        var pending = await IsNpcPending(game, result, ct);
        return new NpcAdvanceResult(result.Events, result.GameOver, pending);
    }

    private static List<NpcAction> DecideActions(
        INpcStrategy npcAI, GD.ClientGameState clientState, string gameID)
    {
        return clientState.CurrentPhase switch
        {
            "main" => npcAI.DecideMainPhaseActions(clientState),
            "battle" => npcAI.DecideBattlePhaseActions(clientState),
            "end" => BuildDiscardActions(npcAI, clientState, gameID),
            var p => throw new InvalidOperationException(
                $"NPC encountered unexpected phase '{p}' (game={gameID})"),
        };
    }

    private static List<NpcAction> BuildDiscardActions(
        INpcStrategy npcAI, GD.ClientGameState clientState, string gameID)
    {
        var discardCount = clientState.MyView.Hand.Count - BattleConstants.HandLimit;
        if (discardCount <= 0)
        {
            throw new InvalidOperationException(
                $"NPC in end phase but no discard needed (game={gameID})");
        }

        var ids = npcAI.DecideDiscard(clientState, discardCount);
        if (ids.Count == 0)
        {
            throw new InvalidOperationException(
                $"NPC in end phase but AI returned no discard targets (game={gameID})");
        }
        return
        [
            new NpcAction
            {
                ActionType = ActionTypes.DiscardHand,
                Data = new DiscardHandRequest { CardInstanceIDs = ids },
            }
        ];
    }

    private async Task<bool> IsNpcPending(Game game, OverloadParty.Battle.Engine.ActionResult result, CancellationToken ct)
    {
        if (result.GameOver is not null)
        {
            return false;
        }

        if (result.ShouldSelectSlot)
        {
            return true;
        }

        var state = await _repo.GetGameState(game.GameID, ct);
        if (state is null)
        {
            return false;
        }

        return ResolveNpcAIForPlayer(game, state.ActivePlayer) is not null;
    }
}
