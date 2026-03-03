namespace OverloadParty.Battle.Npc;

/// <summary>
/// Represents a single action the NPC wants to take.
/// </summary>
public class NpcAction
{
    public string ActionType { get; init; } = "";
    public Dictionary<string, object> Data { get; init; } = new();
}
