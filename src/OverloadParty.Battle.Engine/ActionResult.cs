using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Represents a game-over outcome with the winner and reason.
/// WinnerNum=0 means draw.
/// </summary>
public record GameOverResult(long WinnerNum, string Reason);

/// <summary>
/// Result of processing a game action or auto-advance.
/// </summary>
public class ActionResult
{
    public List<GameEvent> Events { get; set; } = [];
    public bool StateUpdated { get; set; }
    public GameOverResult? GameOver { get; set; }
    public bool NeedsDiscard { get; set; }
}
