namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// Request to play a card from hand onto the field.
/// Accepts both flat (Zone/Index) and nested (Position: {zone, index}) formats.
/// </summary>
public class PlayCardRequest
{
    /// <summary>The instance ID of the card in hand to play.</summary>
    public string CardInstanceID { get; set; } = "";

    /// <summary>The target zone (frontend, backend, or support) to place the card.</summary>
    public string Zone { get; set; } = "";

    /// <summary>The slot index within the target zone.</summary>
    public int Index { get; set; }

    /// <summary>
    /// Nested position format sent by the client and NPC AI.
    /// When set, populates Zone and Index from the nested object.
    /// </summary>
    public PlayCardPosition? Position
    {
        get => null;
        set
        {
            if (value is not null)
            {
                Zone = value.Zone;
                Index = value.Index;
            }
        }
    }

    /// <summary>The instance ID of the target resource, required for attachment cards.</summary>
    public string? TargetInstanceID { get; set; }

    /// <summary>Optional choice data for cards with deploy effects that require player input.</summary>
    public Dictionary<string, object>? ChoiceData { get; set; }
}

/// <summary>
/// Nested position object used in play_card requests from client and NPC AI.
/// </summary>
public class PlayCardPosition
{
    public string Zone { get; set; } = "";
    public int Index { get; set; }
}

/// <summary>
/// Request to attack an opponent's resource with a frontend compute resource.
/// </summary>
public class AttackRequest
{
    /// <summary>The instance ID of the attacking resource.</summary>
    public string AttackerInstanceID { get; set; } = "";

    /// <summary>The instance ID of the target resource on the opponent's field.</summary>
    public string TargetInstanceID { get; set; } = "";
}

/// <summary>
/// Request to change a resource's rank or instance family.
/// </summary>
public class ScaleUpRequest
{
    /// <summary>The instance ID of the resource to scale.</summary>
    public string InstanceID { get; set; } = "";

    /// <summary>
    /// Alias for InstanceID sent by the client and NPC AI as "componentInstanceId".
    /// </summary>
    public string? ComponentInstanceID
    {
        get => null;
        set
        {
            if (value is not null) InstanceID = value;
        }
    }

    /// <summary>The target rank to scale to (e.g., small, medium, large).</summary>
    public string TargetRank { get; set; } = "";

    /// <summary>The optional instance family to assign when scaling to medium or large.</summary>
    public string? InstanceFamily { get; set; }
}

/// <summary>
/// Request to distribute insight yield from backend compute resources into budget.
/// </summary>
public class MonetizeRequest
{
    /// <summary>The list of distributions specifying which resources contribute and how much.</summary>
    public List<MonetizeDistribution> Distributions { get; set; } = [];
}

/// <summary>
/// A single monetize distribution entry specifying a resource and the amount to distribute.
/// </summary>
public class MonetizeDistribution
{
    /// <summary>The instance ID of the backend compute resource.</summary>
    public string InstanceID { get; set; } = "";

    /// <summary>
    /// Alias for InstanceID sent by the client and NPC AI as "componentInstanceId".
    /// </summary>
    public string? ComponentInstanceID
    {
        get => null;
        set
        {
            if (value is not null) InstanceID = value;
        }
    }

    /// <summary>The amount of insight to distribute from this resource.</summary>
    public long Amount { get; set; }
}

/// <summary>
/// Request to discard cards from hand when exceeding the hand limit.
/// </summary>
public class DiscardHandRequest
{
    /// <summary>The instance IDs of the cards to discard.</summary>
    public List<string> CardInstanceIDs { get; set; } = [];
}

/// <summary>
/// Request to activate a resource's or support card's effect.
/// </summary>
public class UseEffectRequest
{
    /// <summary>The instance ID of the resource or support card whose effect to activate.</summary>
    public string InstanceID { get; set; } = "";

    /// <summary>The optional instance ID of the target for the effect.</summary>
    public string? TargetInstanceID { get; set; }

    /// <summary>Optional choice data for effects that require player input.</summary>
    public Dictionary<string, object>? ChoiceData { get; set; }
}

/// <summary>
/// Request to migrate a resource from one instance to another.
/// </summary>
public class MigrateRequest
{
    /// <summary>The instance ID of the source resource being migrated away from.</summary>
    public string SourceInstanceID { get; set; } = "";

    /// <summary>The instance ID of the target resource being migrated to.</summary>
    public string TargetInstanceID { get; set; } = "";
}
