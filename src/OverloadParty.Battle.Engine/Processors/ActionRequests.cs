namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// Request types for game actions. These are pure data objects with no JSON dependency.
/// The Server/Data layer is responsible for deserializing from JSON into these types.
/// </summary>
public class PlayCardRequest
{
    public string CardInstanceID { get; set; } = "";
    public string Zone { get; set; } = "";
    public int Index { get; set; }
    public string? TargetInstanceID { get; set; }
    public Dictionary<string, object>? ChoiceData { get; set; }
}

public class AttackRequest
{
    public string AttackerInstanceID { get; set; } = "";
    public string TargetInstanceID { get; set; } = "";
}

public class ScaleUpRequest
{
    public string InstanceID { get; set; } = "";
    public string TargetRank { get; set; } = "";
    public string? InstanceFamily { get; set; }
}

public class MonetizeRequest
{
    public List<MonetizeDistribution> Distributions { get; set; } = [];
}

public class MonetizeDistribution
{
    public string InstanceID { get; set; } = "";
    public long Amount { get; set; }
}

public class DiscardHandRequest
{
    public List<string> CardInstanceIDs { get; set; } = [];
}

public class ActivateEffectRequest
{
    public string InstanceID { get; set; } = "";
    public string? TargetInstanceID { get; set; }
    public Dictionary<string, object>? ChoiceData { get; set; }
}

public class MigrateRequest
{
    public string SourceInstanceID { get; set; } = "";
    public string TargetInstanceID { get; set; } = "";
}
