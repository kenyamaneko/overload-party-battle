namespace OverloadParty.Battle.Npc;

public static class NpcConstants
{
    public const string PlayerIdPrefix = "npc-";
    public const string PlayerId = "npc-00000000-0000-0000-0000-000000000001";

    public static bool IsNpcPlayer(string playerId) => playerId.StartsWith(PlayerIdPrefix);
}
