using System.Text.Json;

namespace OverloadParty.Battle.Models;

/// <summary>
/// A single effect block deserialized from the cards JSON "effects" array.
/// </summary>
public class EffectDef
{
    public string? Id { get; set; }
    public string Trigger { get; set; } = "";
    public string? UseLimit { get; set; }
    public string? After { get; set; }
    public List<JsonElement>? Guard { get; set; }
    public List<JsonElement>? Ops { get; set; }
    public Dictionary<string, List<JsonElement>>? Choice { get; set; }
    public string? Custom { get; set; }
    public Dictionary<string, JsonElement>? Meta { get; set; }
}
