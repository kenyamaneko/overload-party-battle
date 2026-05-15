using System.Text.Json;
using System.Text.Json.Serialization;
using OverloadParty.Battle.Models.Json;

namespace OverloadParty.Battle.Data.Json;

/// <summary>
/// JSON serializer options for DB JSONB columns.
/// Uses snake_case naming and null suppression to match the Go version's wire format.
/// </summary>
public static class DbJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ZoneJsonConverterFactory() },
    };
}
