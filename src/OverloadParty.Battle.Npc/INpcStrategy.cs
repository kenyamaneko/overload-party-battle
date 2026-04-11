using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Strategy defines the interface for NPC decision-making.
/// Available actions are pre-computed by the engine; the AI selects which to take.
/// </summary>
public interface INpcStrategy
{
    List<NpcAction> DecideMainPhaseActions(BattleGameState state, Game game, long npcPlayerNum, List<AvailableAction> available);
    List<NpcAction> DecideBattlePhaseActions(BattleGameState state, Game game, long npcPlayerNum, List<AvailableAction> available);
    List<string> DecideDiscard(BattleGameState state, long npcPlayerNum, int discardCount);
    NpcAction? DecideSlotSelect(BattleGameState state, long npcPlayerNum);
}
