namespace OverloadParty.Generated;

/// <summary>
/// Temporary local definition until OverloadParty.Generated NuGet package is updated.
/// Remove this file after the common package publishes the turn_start schema.
/// </summary>
public class TurnStartEventData
{
    public required long Turn { get; init; }
    public required long ActivePlayer { get; init; }

    public Dictionary<string, object> ToDictionary()
    {
        var d = new Dictionary<string, object>
        {
            ["turn"] = Turn,
            ["activePlayer"] = ActivePlayer,
        };
        return d;
    }
}
