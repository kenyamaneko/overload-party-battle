namespace OverloadParty.Battle.Server.WebSocket;

/// <summary>
/// WebSocket message type constants (wire format).
/// Must match client expectations from overload-party-common.
/// </summary>
public static class WsMsgType
{
    // Server → Client
    public const string GameState = "game_state";
    public const string GameOver = "game_over";
    public const string Error = "error";
    public const string GameEntered = "game_entered";
    public const string MatchmakingStarted = "matchmaking_started";
    public const string MatchmakingCancelled = "matchmaking_cancelled";
    public const string ActionRejected = "action_rejected";
    public const string StampUsed = "stamp_used";
    public const string Pong = "pong";
    public const string MatchFound = "match_found";
    public const string GameStateRestore = "game_state_restore";
    public const string ActionPerformed = "action_performed";
    public const string TurnControls = "turn_controls";
    public const string NpcBattleCreated = "npc_battle_created";

    // Client → Server
    public const string GameEnter = "game_enter";
    public const string MatchmakingStart = "matchmaking_start";
    public const string MatchmakingCancel = "matchmaking_cancel";
    public const string GameAction = "game_action";
    public const string UseStamp = "use_stamp";
    public const string Ping = "ping";
    public const string NpcBattleStart = "npc_battle_start";
}

/// <summary>
/// Generic WebSocket message envelope.
/// </summary>
public class WsMessage
{
    public string Type { get; set; } = "";
    public Dictionary<string, object>? Data { get; set; }
}
