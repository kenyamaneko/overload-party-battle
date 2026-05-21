using System.Text.Json.Serialization;

namespace OverloadParty.Battle.Engine.Processors;

/// <summary>
/// Request to play a card from hand onto the field.
/// Accepts both flat (Zone/Index) and nested (Position: {zone, index}) formats.
/// </summary>
public class PlayCardRequest
{
    /// <summary>The instance ID of the card in hand to play.</summary>
    [JsonPropertyName("cardInstanceId")]
    public string CardInstanceID { get; set; } = "";

    /// <summary>The target zone (frontend, backend, or support) to place the card.</summary>
    [JsonPropertyName("zone")]
    public string Zone { get; set; } = "";

    /// <summary>The slot index within the target zone.</summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>
    /// Nested position format sent by the client and NPC AI.
    /// When set, populates Zone and Index from the nested object.
    /// </summary>
    [JsonPropertyName("position")]
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
    [JsonPropertyName("targetInstanceId")]
    public string? TargetInstanceID { get; set; }

    /// <summary>Optional choice data for cards with deploy effects that require player input.</summary>
    [JsonPropertyName("choiceData")]
    public Dictionary<string, object>? ChoiceData { get; set; }
}

/// <summary>
/// Nested position object used in play_card requests from client and NPC AI.
/// </summary>
public class PlayCardPosition
{
    [JsonPropertyName("zone")]
    public string Zone { get; set; } = "";

    [JsonPropertyName("index")]
    public int Index { get; set; }
}

/// <summary>
/// Request to attack an opponent's resource with a frontend compute resource.
/// </summary>
public class AttackRequest
{
    /// <summary>The instance ID of the attacking resource.</summary>
    [JsonPropertyName("attackerInstanceId")]
    public string AttackerInstanceID { get; set; } = "";

    /// <summary>The instance ID of the target resource on the opponent's field.</summary>
    [JsonPropertyName("targetInstanceId")]
    public string TargetInstanceID { get; set; } = "";
}

/// <summary>
/// Request to change a resource's rank or instance family.
/// </summary>
public class ScaleUpRequest
{
    /// <summary>The instance ID of the resource to scale.</summary>
    [JsonPropertyName("instanceId")]
    public string InstanceID { get; set; } = "";

    /// <summary>
    /// Alias for InstanceID sent by the client and NPC AI as "componentInstanceId".
    /// </summary>
    [JsonPropertyName("componentInstanceId")]
    public string? ComponentInstanceID
    {
        get => null;
        set
        {
            if (value is not null)
            {
                InstanceID = value;
            }
        }
    }

    /// <summary>The target rank to scale to (e.g., small, medium, large).</summary>
    [JsonPropertyName("targetRank")]
    public string TargetRank { get; set; } = "";

    /// <summary>The optional instance family to assign when scaling to medium or large.</summary>
    [JsonPropertyName("instanceFamily")]
    public string? InstanceFamily { get; set; }
}

/// <summary>
/// Request to distribute insight yield from backend compute resources into budget.
/// </summary>
public class MonetizeRequest
{
    /// <summary>The list of distributions specifying which resources contribute and how much.</summary>
    [JsonPropertyName("distributions")]
    public List<MonetizeDistribution> Distributions { get; set; } = [];
}

/// <summary>
/// A single monetize distribution entry specifying a resource and the amount to distribute.
/// </summary>
public class MonetizeDistribution
{
    /// <summary>The instance ID of the backend compute resource.</summary>
    [JsonPropertyName("instanceId")]
    public string InstanceID { get; set; } = "";

    /// <summary>
    /// Alias for InstanceID sent by the client and NPC AI as "componentInstanceId".
    /// </summary>
    [JsonPropertyName("componentInstanceId")]
    public string? ComponentInstanceID
    {
        get => null;
        set
        {
            if (value is not null)
            {
                InstanceID = value;
            }
        }
    }

    /// <summary>The amount of insight to distribute from this resource.</summary>
    [JsonPropertyName("amount")]
    public long Amount { get; set; }
}

/// <summary>
/// Request to discard cards from hand when exceeding the hand limit.
/// </summary>
public class DiscardHandRequest
{
    /// <summary>The instance IDs of the cards to discard.</summary>
    [JsonPropertyName("cardInstanceIds")]
    public List<string> CardInstanceIDs { get; set; } = [];
}

/// <summary>
/// Request to activate a resource's or support card's effect.
/// </summary>
public class UseEffectRequest
{
    /// <summary>The instance ID of the resource or support card whose effect to activate.</summary>
    [JsonPropertyName("instanceId")]
    public string InstanceID { get; set; } = "";

    /// <summary>The optional instance ID of the target for the effect.</summary>
    [JsonPropertyName("targetInstanceId")]
    public string? TargetInstanceID { get; set; }

    /// <summary>Optional choice data for effects that require player input.</summary>
    [JsonPropertyName("choiceData")]
    public Dictionary<string, object>? ChoiceData { get; set; }
}

/// <summary>
/// Request to select a deployment slot for a pending effect deploy.
/// </summary>
public class SelectSlotRequest
{
    /// <summary>The target zone (frontend or backend).</summary>
    [JsonPropertyName("zone")]
    public string Zone { get; set; } = "";

    /// <summary>The slot index within the target zone.</summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }
}

/// <summary>
/// Request to forfeit a game, with an optional reason indicating why.
/// </summary>
public class ForfeitRequest
{
    /// <summary>
    /// The reason for the forfeit (e.g. "turn_timeout", "disconnect", "surrender").
    /// Defaults to TurnTimeout if not specified.
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// 保留中の効果選択を解決するためのリクエスト。選択値を 1 つ受け取る。
/// </summary>
public class ResolvePendingChoiceRequest
{
    /// <summary>選択された ID (PendingEffectChoice.Candidates のいずれか)。</summary>
    [JsonPropertyName("chosen_id")]
    public string ChosenId { get; set; } = "";
}
