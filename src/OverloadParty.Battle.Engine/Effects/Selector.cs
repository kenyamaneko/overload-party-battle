using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// Selects target resources for an effect operation.
/// </summary>
public interface ISelector
{
    /// <summary>
    /// Selects target resources from the current pipeline context.
    /// </summary>
    /// <param name="ctx">The operation context.</param>
    /// <returns>List of selected resource instances.</returns>
    List<ResourceInstance> Select(OpContext ctx);
}

/// <summary>
/// Select the source resource.
/// </summary>
public class SourceSelector : ISelector
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly SourceSelector Instance = new();

    /// <inheritdoc />
    public List<ResourceInstance> Select(OpContext ctx) =>
        ctx.Source is { } s ? [s] : [];
}

/// <summary>
/// Select the target resource.
/// </summary>
public class TargetSelector : ISelector
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly TargetSelector Instance = new();

    /// <inheritdoc />
    public List<ResourceInstance> Select(OpContext ctx) =>
        ctx.Target is { } t ? [t] : [];
}

/// <summary>
/// Select a resource by player choice (from ChoiceData.instanceId).
/// </summary>
public class ByChoiceSelector : ISelector
{
    /// <summary>Zone filter, or null for any zone.</summary>
    public string? Zone { get; init; }

    /// <summary>Faction filter, or null for any faction.</summary>
    public string? Faction { get; init; }

    /// <summary>Card type filter, or null for any type.</summary>
    public string? CardType { get; init; }

    /// <summary>Owner of the target: "self" or "opponent".</summary>
    public string Owner { get; init; } = "self";

    /// <inheritdoc />
    public List<ResourceInstance> Select(OpContext ctx)
    {
        var instanceId = GetChoiceInstanceId(ctx);
        if (instanceId is null)
        {
            return [];
        }

        var field = Owner == "opponent" ? ctx.OpponentField : ctx.MyField;
        var resource = FieldHelpers.FindResourceByID(field, instanceId);
        if (resource is null)
        {
            return [];
        }

        // Apply filters
        if (Zone is { } z)
        {
            var actualZone = FieldHelpers.FindResourceZone(field, instanceId)?.ToWireString();
            if (actualZone != z)
            {
                return [];
            }
        }

        if (Faction is { Length: > 0 } faction)
        {
            var card = ctx.CardCache.Get(resource.CardID);
            if (card?.Faction != faction)
            {
                return [];
            }
        }

        if (CardType is { Length: > 0 } cardType)
        {
            var card = ctx.CardCache.Get(resource.CardID);
            if (card?.CardType != cardType)
            {
                return [];
            }
        }

        return [resource];
    }

    private static string? GetChoiceInstanceId(OpContext ctx)
    {
        if (ctx.ChoiceData is null)
        {
            return null;
        }
        if (ctx.ChoiceData.TryGetValue("instanceId", out var val))
        {
            return val?.ToString();
        }
        return null;
    }
}

/// <summary>
/// Select all own resources (optionally filtered by zone and faction).
/// </summary>
public class AllOwnSelector : ISelector
{
    /// <summary>Zone filter, or null for all zones.</summary>
    public string? Zone { get; init; }

    /// <summary>Faction filter, or null for any faction.</summary>
    public string? Faction { get; init; }

    /// <inheritdoc />
    public List<ResourceInstance> Select(OpContext ctx)
    {
        var field = ctx.MyField;
        return FilterResources(field, Zone, Faction, ctx.CardCache);
    }

    internal static List<ResourceInstance> FilterResources(Field field, string? zone, string? faction, ICardCache cc)
    {
        IEnumerable<ResourceInstance> candidates = zone switch
        {
            GameConstants.ZoneFrontend => field.Frontend,
            GameConstants.ZoneBackend => field.Backend,
            _ => field.Frontend.Concat(field.Backend),
        };

        return candidates
            .Where(r => r.FaceUp)
            .Where(r => faction is not { Length: > 0 } || cc.Get(r.CardID)?.Faction == faction)
            .ToList();
    }
}

/// <summary>
/// Select all opponent resources (optionally filtered by zone and faction).
/// </summary>
public class AllOpponentSelector : ISelector
{
    /// <summary>Zone filter, or null for all zones.</summary>
    public string? Zone { get; init; }

    /// <summary>Faction filter, or null for any faction.</summary>
    public string? Faction { get; init; }

    /// <inheritdoc />
    public List<ResourceInstance> Select(OpContext ctx)
    {
        var field = ctx.OpponentField;
        return AllOwnSelector.FilterResources(field, Zone, Faction, ctx.CardCache);
    }
}
