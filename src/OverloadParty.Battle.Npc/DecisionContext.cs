using OverloadParty.Battle.Engine;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// DecisionContext は NPC 意思決定に必要な情報秘匿済み状態をまとめて保持します。
/// PvP と同じ ClientGameState ビューから組み立てる。
/// </summary>
public class DecisionContext(
    GD.Field field,
    GD.OpponentField oppField,
    List<GD.UndeployedCard> hand,
    long budget,
    ICardCache cardCache)
{
    public GD.Field Field { get; } = field;
    public GD.OpponentField OppField { get; } = oppField;
    public List<GD.UndeployedCard> Hand { get; } = hand;
    public long Budget { get; } = budget;
    public ICardCache CardCache { get; } = cardCache;
    public long CurrentTurn { get; init; }
    public long InsightPool { get; init; }
}
