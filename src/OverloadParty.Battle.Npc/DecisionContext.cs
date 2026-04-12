using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// DecisionContext は NPC 意思決定に必要な全状態を保持するコンテキストです
/// </summary>
public class DecisionContext(Field field, Field oppField, List<UndeployedCard> hand, long budget, ICardCache cardCache)
{
    public Field Field { get; } = field;
    public Field OppField { get; } = oppField;
    public List<UndeployedCard> Hand { get; } = hand;
    public long Budget { get; } = budget;
    public ICardCache CardCache { get; } = cardCache;
    public long CurrentTurn { get; init; }
}
