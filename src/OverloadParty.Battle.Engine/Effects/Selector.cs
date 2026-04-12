using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// ISelector はエフェクト操作のターゲットリソースを選択します
/// </summary>
public interface ISelector
{
    /// <summary>
    /// Selects target resources from the current pipeline context.
    /// </summary>
    /// <param name="ctx">The operation context.</param>
    /// <returns>List of selected resource instances.</returns>
    List<DeployedResource> Select(OpContext ctx);
}

/// <summary>
/// SourceSelector はソースリソースを選択します
/// </summary>
public class SourceSelector : ISelector
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly SourceSelector Instance = new();

    /// <inheritdoc />
    public List<DeployedResource> Select(OpContext ctx) =>
        ctx.Source is { } s ? [s] : [];
}

/// <summary>
/// TargetSelector はターゲットリソースを選択します
/// </summary>
public class TargetSelector : ISelector
{
    /// <summary>Shared singleton instance.</summary>
    public static readonly TargetSelector Instance = new();

    /// <inheritdoc />
    public List<DeployedResource> Select(OpContext ctx) =>
        ctx.Target is { } t ? [t] : [];
}

/// <summary>
/// ByChoiceSelector はプレイヤーの選択（ChoiceData.instanceId）でリソースを選択します
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
    public List<DeployedResource> Select(OpContext ctx)
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

        // フィルターを適用
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
            var card = ctx.CardCache.MustGet(resource.CardID);
            if (card.Faction != faction)
            {
                return [];
            }
        }

        if (CardType is { Length: > 0 } cardType)
        {
            var card = ctx.CardCache.MustGet(resource.CardID);
            if (card.CardType != cardType)
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
/// AllOwnSelector は自分の全リソースを選択します（ゾーン・ファクションでフィルタ可能）
/// </summary>
public class AllOwnSelector : ISelector
{
    /// <summary>Zone filter, or null for all zones.</summary>
    public string? Zone { get; init; }

    /// <summary>Faction filter, or null for any faction.</summary>
    public string? Faction { get; init; }

    /// <summary>Card type filter list, or null for any type.</summary>
    public List<string>? CardTypes { get; init; }

    /// <inheritdoc />
    public List<DeployedResource> Select(OpContext ctx)
    {
        var field = ctx.MyField;
        return FilterResources(field, Zone, Faction, ctx.CardCache, CardTypes);
    }

    internal static List<DeployedResource> FilterResources(Field field, string? zone, string? faction, ICardCache cc, List<string>? cardTypes = null)
    {
        IEnumerable<DeployedResource> candidates = zone switch
        {
            Zones.Frontend => field.Frontend,
            Zones.Backend => field.Backend,
            _ => field.Frontend.Concat(field.Backend),
        };

        return candidates
            .Where(r => r.FaceUp)
            .Where(r => faction is not { Length: > 0 } || cc.MustGet(r.CardID).Faction == faction)
            .Where(r => cardTypes is not { Count: > 0 } || cardTypes.Contains(cc.MustGet(r.CardID).CardType))
            .ToList();
    }
}

/// <summary>
/// AllOpponentSelector は相手の全リソースを選択します（ゾーン・ファクションでフィルタ可能）
/// </summary>
public class AllOpponentSelector : ISelector
{
    /// <summary>Zone filter, or null for all zones.</summary>
    public string? Zone { get; init; }

    /// <summary>Faction filter, or null for any faction.</summary>
    public string? Faction { get; init; }

    /// <summary>Card type filter list, or null for any type.</summary>
    public List<string>? CardTypes { get; init; }

    /// <inheritdoc />
    public List<DeployedResource> Select(OpContext ctx)
    {
        var field = ctx.OpponentField;
        return AllOwnSelector.FilterResources(field, Zone, Faction, ctx.CardCache, CardTypes);
    }
}

/// <summary>
/// UnionSelector は 2 つのセレクタの結果を結合します
/// </summary>
public class UnionSelector(ISelector a, ISelector b) : ISelector
{
    /// <inheritdoc />
    public List<DeployedResource> Select(OpContext ctx) =>
        a.Select(ctx).Concat(b.Select(ctx)).ToList();
}

/// <summary>
/// ExcludeSourceSelector は別のセレクタをラップしソースリソースを結果から除外します
/// </summary>
public class ExcludeSourceSelector(ISelector inner) : ISelector
{
    /// <inheritdoc />
    public List<DeployedResource> Select(OpContext ctx)
    {
        var results = inner.Select(ctx);
        if (ctx.Source is null) return results;
        return results.Where(r => r.InstanceID != ctx.Source.InstanceID).ToList();
    }
}
