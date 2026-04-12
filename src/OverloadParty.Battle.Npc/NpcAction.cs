namespace OverloadParty.Battle.Npc;

/// <summary>
/// NpcAction は NPC が実行したい 1 つのアクションを表現します
/// </summary>
public class NpcAction
{
    public string ActionType { get; init; } = "";
    public object Data { get; init; } = new();
}
