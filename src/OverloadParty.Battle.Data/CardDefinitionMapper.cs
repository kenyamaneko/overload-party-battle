using System.Text.Json;
using ApiCard = OverloadParty.ApiCard;
using CardTypes = OverloadParty.GameDesignConstants.CardTypes;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Data;

/// <summary>生成カードクライアント (<see cref="ApiCard"/>) の wire 型を battle ドメインモデルへ変換する。</summary>
public static class CardDefinitionMapper
{
    private static readonly JsonSerializerOptions EffectJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const string ThroughputKey = "throughput";
    private const string ThroughputMaxKey = "throughput_max";
    private const string YieldKey = "yield";
    private const string YieldMaxKey = "yield_max";
    private const string AvailabilityKey = "availability";
    private const string MaintenanceCostKey = "maintenance_cost";
    private const string SlaPenaltyKey = "sla_penalty";

    private static readonly HashSet<string> ComputeStatsContractKeys =
        [ThroughputKey, ThroughputMaxKey, AvailabilityKey, MaintenanceCostKey, SlaPenaltyKey];

    private static readonly HashSet<string> DataResourceStatsContractKeys =
        [YieldKey, YieldMaxKey, AvailabilityKey, MaintenanceCostKey, SlaPenaltyKey];

    public static CardDefinition ToCardDefinition(ApiCard.CardDefinition source)
    {
        var cardType = source.Card_type;
        return new CardDefinition
        {
            CardId = source.Card_id,
            CardName = source.Card_name,
            ResourceLabel = source.Resource_label,
            Faction = source.Faction,
            CardType = cardType,
            Subtype = source.Subtype,
            DeployTurns = source.Deploy_turns,
            Resizable = source.Resizable,
            Elastic = source.Elastic,
            ElasticIncrement = source.Elastic_increment,
            FreeTier = source.Free_tier,
            CostPerRequest = source.Cost_per_request,
            EffectText = source.Effect_text,
            Restriction = source.Restriction,
            IsActive = source.Is_active,
            CreatedAt = source.Created_at.UtcDateTime,
            UpdatedAt = source.Updated_at.UtcDateTime,
            ComputeStats = cardType == CardTypes.Compute ? ToComputeStats(source.Card_id, source.Stats) : null,
            DataResourceStats = cardType == CardTypes.DataResource ? ToDataResourceStats(source.Card_id, source.Stats) : null,
            Effects = ToEffectDefs(source.Card_id, source.Effects),
        };
    }

    public static Initiative ToInitiative(ApiCard.Initiative source)
    {
        return new Initiative
        {
            InitiativeId = source.Initiative_id,
            ProductId = source.Product_id,
            Kind = ToInitiativeKind(source.Kind),
            Name = source.Name,
            InsightCost = source.Insight_cost,
            EffectText = source.Effect_text,
            Effect = ToEffectDef(source.Initiative_id, source.Effect),
        };
    }

    private static ComputeStats ToComputeStats(string cardId, JsonElement stats)
    {
        var values = ReadStatsValues(cardId, stats, ComputeStatsContractKeys, [ThroughputKey, AvailabilityKey, MaintenanceCostKey, SlaPenaltyKey]);
        return new ComputeStats
        {
            Throughput = values[ThroughputKey],
            Availability = values[AvailabilityKey],
            MaintenanceCost = values[MaintenanceCostKey],
            SLAPenalty = values[SlaPenaltyKey],
        };
    }

    private static DataResourceStats ToDataResourceStats(string cardId, JsonElement stats)
    {
        var values = ReadStatsValues(cardId, stats, DataResourceStatsContractKeys, [YieldKey, AvailabilityKey, MaintenanceCostKey, SlaPenaltyKey]);
        return new DataResourceStats
        {
            Yield = values[YieldKey],
            Availability = values[AvailabilityKey],
            MaintenanceCost = values[MaintenanceCostKey],
            SLAPenalty = values[SlaPenaltyKey],
        };
    }

    private static Dictionary<string, long> ReadStatsValues(
        string cardId, JsonElement stats, HashSet<string> contractKeys, string[] requiredKeys)
    {
        if (stats.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"card {cardId}: stats must be a JSON object, was {stats.ValueKind}");
        }

        var values = new Dictionary<string, long>();
        foreach (var property in stats.EnumerateObject())
        {
            if (!contractKeys.Contains(property.Name))
            {
                throw new InvalidOperationException($"card {cardId}: stats has unrecognized key '{property.Name}'");
            }

            values[property.Name] = property.Value.GetInt64();
        }

        foreach (var requiredKey in requiredKeys)
        {
            if (!values.ContainsKey(requiredKey))
            {
                throw new InvalidOperationException($"card {cardId}: stats is missing required key '{requiredKey}'");
            }
        }

        return values;
    }

    private static List<EffectDef>? ToEffectDefs(string cardId, JsonElement effects)
    {
        if (effects.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        if (effects.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"card {cardId}: effects must be a JSON array, was {effects.ValueKind}");
        }

        return JsonSerializer.Deserialize<List<EffectDef>>(effects.GetRawText(), EffectJsonOptions);
    }

    private static EffectDef ToEffectDef(string initiativeId, JsonElement effect)
    {
        if (effect.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"initiative {initiativeId}: effect must be a JSON object, was {effect.ValueKind}");
        }

        return JsonSerializer.Deserialize<EffectDef>(effect.GetRawText(), EffectJsonOptions)
            ?? throw new InvalidOperationException($"initiative {initiativeId}: effect deserialized to null");
    }

    private static string ToInitiativeKind(ApiCard.InitiativeKind kind) => kind switch
    {
        ApiCard.InitiativeKind.Routine => InitiativeKinds.Routine,
        ApiCard.InitiativeKind.Special => InitiativeKinds.Special,
        _ => throw new InvalidOperationException($"unrecognized initiative kind '{kind}'"),
    };
}
