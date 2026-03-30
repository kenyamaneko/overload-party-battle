using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// Strategy defines the interface for NPC decision-making.
/// Available actions are pre-computed by the engine; the AI selects which to take.
/// </summary>
public interface INpcStrategy
{
    List<NpcAction> DecideMainPhaseActions(GameState state, Game game, long npcPlayerNum, List<AvailableAction> available);
    List<NpcAction> DecideBattlePhaseActions(GameState state, Game game, long npcPlayerNum, List<AvailableAction> available);
    List<string> DecideDiscard(GameState state, long npcPlayerNum);
    NpcAction? DecideSlotSelect(GameState state, long npcPlayerNum);
}
