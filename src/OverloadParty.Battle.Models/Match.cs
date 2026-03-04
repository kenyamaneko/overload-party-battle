namespace OverloadParty.Battle.Models;

/// <summary>
/// Match history record. Maps to the matches table.
/// </summary>
public class Match
{
    public long MatchID { get; set; }
    public string GameID { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
