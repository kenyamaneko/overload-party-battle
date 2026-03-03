using System.Text.Json;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Service;

namespace OverloadParty.Battle.Server.WebSocket;

/// <summary>
/// Manages WebSocket connections, game membership, and disconnect timeouts.
/// </summary>
public class WsManager
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, WsConnection> _connections = new(); // playerID → Connection
    private readonly Dictionary<string, List<string>> _gameMembers = new(); // gameID → []playerID
    private readonly Dictionary<string, string> _playerGames = new(); // playerID → gameID
    private readonly Dictionary<string, CancellationTokenSource> _disconnectTimers = new();

    private readonly GameService _gameService;
    private readonly ILogger<WsManager> _logger;
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(60);

    public WsManager(GameService gameService, ILogger<WsManager> logger)
    {
        _gameService = gameService;
        _logger = logger;

        // Wire up observers
        _gameService.SetActionObserver((gameID, actionType, data) =>
            BroadcastActionPerformed(gameID, actionType, data));
        _gameService.SetBattleEventObserver((gameID, evt) =>
            BroadcastBattleEvent(gameID, evt));
    }

    public void Register(WsConnection conn)
    {
        lock (_lock)
        {
            // Cancel disconnect timer if reconnecting
            if (_disconnectTimers.Remove(conn.PlayerID, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _logger.LogInformation("Player {PlayerID} reconnected", conn.PlayerID);
            }

            // Replace existing connection
            if (_connections.TryGetValue(conn.PlayerID, out var old))
            {
                _ = old.CloseAsync();
                old.Dispose();
            }
            _connections[conn.PlayerID] = conn;
        }
    }

    public void Unregister(WsConnection conn)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(conn.PlayerID, out var existing) || existing != conn)
                return;

            _connections.Remove(conn.PlayerID);

            // Start disconnect timer if player is in a game
            if (_playerGames.TryGetValue(conn.PlayerID, out var gameID))
            {
                var cts = new CancellationTokenSource();
                _disconnectTimers[conn.PlayerID] = cts;
                var playerID = conn.PlayerID;

                _ = Task.Delay(DisconnectTimeout, cts.Token).ContinueWith(_ =>
                    HandleDisconnectTimeout(playerID, gameID), TaskContinuationOptions.OnlyOnRanToCompletion);

                _logger.LogInformation("Player {PlayerID} disconnected, {Timeout}s timeout started",
                    conn.PlayerID, DisconnectTimeout.TotalSeconds);
            }
        }
    }

    private void HandleDisconnectTimeout(string playerID, string gameID)
    {
        lock (_lock) { _disconnectTimers.Remove(playerID); }

        _logger.LogWarning("Player {PlayerID} disconnect timeout expired for game {GameID}", playerID, gameID);
        // Forfeit is handled at the game engine level
    }

    public void JoinGame(string playerID, string gameID)
    {
        lock (_lock)
        {
            _playerGames[playerID] = gameID;
            if (!_gameMembers.TryGetValue(gameID, out var members))
            {
                members = [];
                _gameMembers[gameID] = members;
            }
            if (!members.Contains(playerID))
                members.Add(playerID);
        }
    }

    public void LeaveGame(string playerID)
    {
        lock (_lock)
        {
            if (!_playerGames.Remove(playerID, out var gameID)) return;
            if (_gameMembers.TryGetValue(gameID, out var members))
            {
                members.Remove(playerID);
                if (members.Count == 0)
                    _gameMembers.Remove(gameID);
            }
        }
    }

    // ─── Sending ────────────────────────────────────────────────

    public void SendToPlayer(string playerID, object message)
    {
        WsConnection? conn;
        lock (_lock) { _connections.TryGetValue(playerID, out conn); }
        conn?.SendMessage(message);
    }

    public void BroadcastToGame(string gameID, object message)
    {
        List<string> players;
        lock (_lock)
        {
            if (!_gameMembers.TryGetValue(gameID, out var members)) return;
            players = [.. members];
        }
        foreach (var pid in players)
            SendToPlayer(pid, message);
    }

    public void SendGameStateToPlayers(string gameID)
    {
        List<string> players;
        lock (_lock)
        {
            if (!_gameMembers.TryGetValue(gameID, out var members)) return;
            players = [.. members];
        }

        foreach (var pid in players)
        {
            var clientState = _gameService.GetGameStateForPlayer(gameID, pid).GetAwaiter().GetResult();
            if (clientState is null) continue;
            SendToPlayer(pid, new { type = WsMsgType.GameState, data = clientState });
        }
    }

    public void SendTurnControlsToPlayers(string gameID)
    {
        List<string> players;
        lock (_lock)
        {
            if (!_gameMembers.TryGetValue(gameID, out var members)) return;
            players = [.. members];
        }

        foreach (var pid in players)
        {
            // Only active player gets turn controls (others get null)
            // GameService needs a method for this
        }
    }

    // ─── Action/Event Broadcasting ──────────────────────────────

    private void BroadcastActionPerformed(string gameID, string actionType, Dictionary<string, object>? actionData)
    {
        List<string> players;
        lock (_lock)
        {
            if (!_gameMembers.TryGetValue(gameID, out var members)) return;
            players = [.. members];
        }

        foreach (var pid in players)
        {
            var clientState = _gameService.GetGameStateForPlayer(gameID, pid).GetAwaiter().GetResult();
            SendToPlayer(pid, new
            {
                type = WsMsgType.ActionPerformed,
                data = new
                {
                    action_type = actionType,
                    action_data = actionData,
                    state = clientState,
                },
            });
        }
    }

    private void BroadcastBattleEvent(string gameID, BattleEvent evt)
    {
        List<string> players;
        lock (_lock)
        {
            if (!_gameMembers.TryGetValue(gameID, out var members)) return;
            players = [.. members];
        }

        foreach (var pid in players)
        {
            var clientState = _gameService.GetGameStateForPlayer(gameID, pid).GetAwaiter().GetResult();
            var actionData = BuildBattleEventData(evt, pid, clientState);

            SendToPlayer(pid, new
            {
                type = WsMsgType.ActionPerformed,
                data = new
                {
                    action_type = evt.Type,
                    action_data = actionData,
                    state = clientState,
                },
            });
        }
    }

    private static object BuildBattleEventData(BattleEvent evt, string playerID, ClientGameState? state)
    {
        switch (evt.Type)
        {
            case "battle_start":
            {
                var p1 = evt.Player1Info;
                var p2 = evt.Player2Info;
                var (my, opp) = p1?.PlayerID == playerID ? (p1, p2) : (p2, p1);
                return new
                {
                    my_name = my?.Name ?? "Player",
                    my_level = my?.Level ?? 1,
                    opponent_name = opp?.Name ?? "Player",
                    opponent_level = opp?.Level ?? 1,
                    match_type = evt.MatchType ?? "pvp",
                };
            }
            case "turn_start":
                return new
                {
                    turn = evt.Turn,
                    is_my_turn = state?.IsMyTurn ?? false,
                };
            default:
                return new { };
        }
    }

    public void BroadcastGameOver(string gameID, long winnerNum, string winReason)
    {
        BroadcastToGame(gameID, new
        {
            type = WsMsgType.GameOver,
            data = new { game_id = gameID, winner_num = winnerNum, win_reason = winReason },
        });
    }

    // ─── Message handling ───────────────────────────────────────

    public async Task HandleMessage(WsConnection conn, WsMessage msg)
    {
        switch (msg.Type)
        {
            case WsMsgType.GameEnter:
                HandleGameEnter(conn, msg.Data);
                break;
            case WsMsgType.MatchmakingStart:
                await HandleMatchmakingStart(conn, msg.Data);
                break;
            case WsMsgType.MatchmakingCancel:
                HandleMatchmakingCancel(conn);
                break;
            case WsMsgType.NpcBattleStart:
                await HandleNpcBattleStart(conn, msg.Data);
                break;
            case WsMsgType.GameAction:
                await HandleGameAction(conn, msg.Data);
                break;
            case WsMsgType.UseStamp:
                HandleUseStamp(conn, msg.Data);
                break;
            case WsMsgType.Ping:
                conn.SendMessage(new { type = WsMsgType.Pong });
                break;
            default:
                _logger.LogWarning("Unhandled message type: {Type} from {PlayerID}", msg.Type, conn.PlayerID);
                break;
        }
    }

    private void HandleGameEnter(WsConnection conn, Dictionary<string, object>? data)
    {
        var gameID = GetString(data, "game_id");
        if (gameID is null) { SendError(conn, "invalid_data", "missing game_id"); return; }

        JoinGame(conn.PlayerID, gameID);
        conn.SendMessage(new { type = WsMsgType.GameEntered, data = new { game_id = gameID } });

        SendGameStateToPlayers(gameID);
        SendTurnControlsToPlayers(gameID);
    }

    private async Task HandleMatchmakingStart(WsConnection conn, Dictionary<string, object>? data)
    {
        var deckID = GetLong(data, "deck_id");
        if (deckID is null) { SendError(conn, "invalid_data", "missing deck_id"); return; }

        try
        {
            await _gameService.JoinQueue(conn.PlayerID, deckID.Value);
            conn.SendMessage(new { type = WsMsgType.MatchmakingStarted });
        }
        catch (Exception ex)
        {
            SendError(conn, "matchmaking_error", ex.Message, retryable: true);
        }
    }

    private void HandleMatchmakingCancel(WsConnection conn)
    {
        _gameService.LeaveQueue(conn.PlayerID);
        conn.SendMessage(new { type = WsMsgType.MatchmakingCancelled });
    }

    private async Task HandleNpcBattleStart(WsConnection conn, Dictionary<string, object>? data)
    {
        var deckID = GetLong(data, "deck_id");
        var faction = GetString(data, "npc_faction");
        if (deckID is null || faction is null) { SendError(conn, "invalid_data", "missing deck_id or npc_faction"); return; }

        try
        {
            var game = await _gameService.StartNPCBattle(conn.PlayerID, deckID.Value, faction);
            conn.SendMessage(new
            {
                type = WsMsgType.NpcBattleCreated,
                data = new { game_id = game.GameID, player1_id = game.Player1ID, player2_id = game.Player2ID },
            });
        }
        catch (Exception ex)
        {
            SendError(conn, "npc_battle_error", ex.Message, retryable: true);
        }
    }

    private async Task HandleGameAction(WsConnection conn, Dictionary<string, object>? data)
    {
        var gameID = GetString(data, "game_id");
        var actionTypeStr = GetString(data, "action_type");
        if (gameID is null || actionTypeStr is null) { SendError(conn, "invalid_data", "missing game_id or action_type"); return; }

        ActionType actionType;
        try { actionType = EnumExtensions.ParseActionType(actionTypeStr); }
        catch { SendError(conn, "invalid_data", $"unknown action type: {actionTypeStr}"); return; }

        // Extract action-specific data (everything except game_id and action_type)
        var actionData = data?.Where(kv => kv.Key is not "game_id" and not "action_type")
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        try
        {
            var result = await _gameService.ProcessAction(gameID, conn.PlayerID, actionType, actionData!);

            BroadcastActionPerformed(gameID, actionTypeStr, actionData);
            SendGameStateToPlayers(gameID);
            SendTurnControlsToPlayers(gameID);

            if (result.GameOver)
            {
                BroadcastGameOver(gameID, result.WinnerNum, result.WinReason ?? "");

                List<string> players;
                lock (_lock)
                {
                    if (_gameMembers.TryGetValue(gameID, out var members))
                        players = [.. members];
                    else
                        players = [];
                }
                foreach (var pid in players)
                    LeaveGame(pid);
            }
        }
        catch (Exception ex)
        {
            conn.SendMessage(new
            {
                type = WsMsgType.ActionRejected,
                data = new { game_id = gameID, action_type = actionTypeStr, reason = ex.Message },
            });
        }
    }

    private void HandleUseStamp(WsConnection conn, Dictionary<string, object>? data)
    {
        var gameID = GetString(data, "game_id");
        var stampNo = GetLong(data, "stamp_no");
        if (gameID is null || stampNo is null) return;

        BroadcastToGame(gameID, new
        {
            type = WsMsgType.StampUsed,
            data = new { game_id = gameID, player_id = conn.PlayerID, stamp_no = stampNo },
        });
    }

    // ─── Helpers ────────────────────────────────────────────────

    private static void SendError(WsConnection conn, string code, string message, bool retryable = false)
    {
        conn.SendMessage(new
        {
            type = WsMsgType.Error,
            data = new { error_code = code, message, retryable },
        });
    }

    private static string? GetString(Dictionary<string, object>? data, string key)
    {
        if (data is null) return null;
        if (!data.TryGetValue(key, out var val)) return null;
        return val switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => val.ToString(),
        };
    }

    private static long? GetLong(Dictionary<string, object>? data, string key)
    {
        if (data is null) return null;
        if (!data.TryGetValue(key, out var val)) return null;
        return val switch
        {
            long l => l,
            int i => i,
            JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt64(),
            string s when long.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }
}
