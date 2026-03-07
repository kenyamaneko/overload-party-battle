namespace OverloadParty.Battle.Models;

/// <summary>
/// アクション / イベントタイプのワイヤーフォーマット文字列定数。
/// AvailableAction.Type, NpcAction.ActionType, GameEvent.EventType で共通使用。
/// </summary>
public static class WireActionTypes
{
    // ─── プレイヤーアクション ────────────────────────────────
    public const string PlayCard = "play_card";
    public const string Attack = "attack";
    public const string ScaleUp = "scale_up";
    public const string Monetize = "monetize";
    public const string ActivateEffect = "activate_effect";
    public const string Migrate = "migrate";
    public const string SetReactive = "set_reactive";
    public const string EndPhase = "end_phase";
    public const string Forfeit = "forfeit";
    public const string Reactive = "reactive";

    // ─── イベント専用（アクションとしては使わない） ──────────
    public const string AttachCard = "attach_card";
    public const string DiscardHand = "discard_hand";
    public const string PhaseChange = "phase_change";
    public const string PhaseEnd = "phase_end";
    public const string TurnEnd = "turn_end";
}
