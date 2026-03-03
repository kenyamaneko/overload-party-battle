using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// Result of processing a game action or auto-advance.
/// </summary>
public class ActionResult
{
    public List<GameEvent> Events { get; set; } = [];
    public bool StateUpdated { get; set; }
    public bool GameOver { get; set; }
    public long WinnerNum { get; set; }
    public string? WinReason { get; set; }
    public bool NeedsDiscard { get; set; }
}
