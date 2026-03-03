namespace OverloadParty.Battle.Engine;

/// <summary>
/// Thrown when a game rule is violated (invalid action, out-of-turn play, etc.).
/// These are expected errors, not bugs.
/// </summary>
public class GameRuleException : Exception
{
    public GameRuleException(string message) : base(message) { }
    public GameRuleException(string message, Exception inner) : base(message, inner) { }
}
