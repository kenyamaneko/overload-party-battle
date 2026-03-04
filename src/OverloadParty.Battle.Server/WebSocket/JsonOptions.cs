using System.Text.Json;
using OverloadParty.Battle.Data.Json;

namespace OverloadParty.Battle.Server.WebSocket;

/// <summary>
/// Shared JSON serializer options for WebSocket messages.
/// Uses snake_case to match Go version's wire format.
/// </summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new Data.Json.ZoneJsonConverterFactory() },
    };
}
