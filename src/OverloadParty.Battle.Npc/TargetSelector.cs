using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// TargetSelector は NPC のカテゴリベース意思決定向けターゲット選択ヘルパーを提供します
/// </summary>
public static class TargetSelector
{
    /// <summary>
    /// Returns the instanceID of the resource with the lowest effective AV.
    /// </summary>
    public static string? WeakestInZone(Field field, string? zone)
    {
        return FaceUpInZone(field, zone)
            .MinBy(r => r.EffectiveAV)
            ?.InstanceID;
    }

    /// <summary>
    /// Returns the instanceID of the resource with the highest TP or Yield.
    /// </summary>
    public static string? StrongestInZone(Field field, string? zone, ICardCache cc)
    {
        return FaceUpInZone(field, zone)
            .MaxBy(r => ResourceValue(r, cc))
            ?.InstanceID;
    }

    /// <summary>
    /// Returns the instanceID of the own resource with the most damage.
    /// </summary>
    public static string? MostDamagedOwn(Field field)
    {
        return FieldHelpers.AllFaceUpResources(field)
            .Where(r => r.Damage > 0)
            .MaxBy(r => r.Damage)
            ?.InstanceID;
    }

    public static int CountAllResources(Field field)
    {
        return FieldHelpers.AllFaceUpResources(field).Count;
    }

    public static int CountResourcesInZone(Field field, string? zone)
    {
        return FaceUpInZone(field, zone).Count();
    }

    public static bool HasDamagedResource(Field field)
    {
        return FieldHelpers.AllFaceUpResources(field).Any(r => r.Damage > 0);
    }

    public static bool HasFaceDownSupport(Field field)
    {
        return field.Support.Any(s => !s.FaceUp);
    }

    public static bool HasPlatform(Field field, ICardCache cc)
    {
        return field.Support
            .Any(sup => (cc.Get(sup.CardID)
                ?? throw new InvalidOperationException($"Card '{sup.CardID}' not found in card cache"))
                .CardType == CardTypes.Platform);
    }

    public static string? FirstPlatformId(Field field, ICardCache cc)
    {
        return field.Support
            .FirstOrDefault(sup => (cc.Get(sup.CardID)
                ?? throw new InvalidOperationException($"Card '{sup.CardID}' not found in card cache"))
                .CardType == CardTypes.Platform)
            ?.InstanceID;
    }

    public static long ResourceValue(DeployedResource r, ICardCache cc)
    {
        if (r.CurrentTP is > 0)
        {
            return r.CurrentTP.Value;
        }
        if (r.CurrentYield is > 0)
        {
            return r.CurrentYield.Value;
        }
        var card = cc.Get(r.CardID)
            ?? throw new InvalidOperationException($"Card '{r.CardID}' not found in card cache");
        return card.IsComputeType ? card.BaseThroughput : card.IsDataType ? card.BaseYield : 0;
    }

    // ─── TargetSpec resolution ──────────────────────────────────

    /// <summary>
    /// Resolves a single target from a TargetSpec. Uses the selector to filter
    /// resources, then orders by the order_by stat and picks the first result.
    /// </summary>
    public static string? Resolve(TargetSpec spec, Field selfField, Field oppField, ICardCache cc)
    {
        var field = ResolveField(spec.Selector, selfField, oppField);
        var resources = FilterResources(field, spec.Selector, cc);
        return OrderAndPick(resources, spec.OrderBy, cc);
    }

    /// <summary>
    /// Resolves a single target from a TargetSpec, constrained to the given valid target IDs.
    /// </summary>
    public static string? ResolveFromValid(
        TargetSpec spec, List<string> validTargets, Field selfField, Field oppField, ICardCache cc)
    {
        var field = ResolveField(spec.Selector, selfField, oppField);
        var validSet = new HashSet<string>(validTargets);
        var resources = FilterResources(field, spec.Selector, cc)
            .Where(r => validSet.Contains(r.InstanceID));
        return OrderAndPick(resources, spec.OrderBy, cc);
    }

    /// <summary>
    /// Orders AvailableActions by the given order_by stat applied to their source resource.
    /// </summary>
    public static List<AvailableAction> OrderActions(
        List<AvailableAction> actions, string orderBy, Field field, ICardCache cc)
    {
        var resMap = FieldHelpers.AllResources(field).ToDictionary(r => r.InstanceID);
        var (stat, desc) = ParseOrderBy(orderBy);
        long Selector(AvailableAction a) =>
            resMap.TryGetValue(a.SourceInstanceID!, out var r)
                ? GetStatValue(r, stat, cc) : 0;
        return (desc
            ? actions.OrderByDescending(Selector)
            : actions.OrderBy(Selector)).ToList();
    }

    /// <summary>
    /// Filters resources on a field using a SelectorDef. Shared by target resolution
    /// and GuardChecker condition evaluation.
    /// </summary>
    public static IEnumerable<DeployedResource> FilterResources(
        Field field, SelectorDef sel, ICardCache cc)
    {
        IEnumerable<DeployedResource> resources = sel.FaceDown == true
            ? FieldHelpers.AllResources(field).Where(r => !r.FaceUp)
            : FieldHelpers.AllFaceUpResources(field);

        if (sel.Zone is not null)
        {
            resources = sel.Zone switch
            {
                Zones.Frontend => field.Frontend.Where(r => sel.FaceDown == true || r.FaceUp),
                Zones.Backend => field.Backend.Where(r => sel.FaceDown == true || r.FaceUp),
                _ => Enumerable.Empty<DeployedResource>(),
            };
        }

        if (sel.Faction is not null)
        {
            resources = resources.Where(r =>
                (cc.Get(r.CardID)
                    ?? throw new InvalidOperationException(
                        $"Card '{r.CardID}' not found in card cache"))
                .Faction == sel.Faction);
        }

        if (sel.CardType is not null)
        {
            resources = resources.Where(r => OverloadParty.Battle.Engine.Effects.EffectHelpers.MatchesCardType(
                cc.Get(r.CardID)
                    ?? throw new InvalidOperationException(
                        $"Card '{r.CardID}' not found in card cache"),
                sel.CardType));
        }

        if (sel.CardId is not null)
        {
            var cardIds = new HashSet<string>(sel.CardId);
            resources = resources.Where(r => cardIds.Contains(r.CardID));
        }

        return resources;
    }

    // ─── Private helpers ────────────────────────────────────────

    private static Field ResolveField(SelectorDef sel, Field selfField, Field oppField)
    {
        return sel.Owner switch
        {
            "myself" => selfField,
            "opponent" => oppField,
            var o => throw new InvalidOperationException($"Unknown selector owner: '{o}'"),
        };
    }

    private static string? OrderAndPick(
        IEnumerable<DeployedResource> resources, string? orderBy, ICardCache cc)
    {
        if (orderBy is null)
        {
            return resources.FirstOrDefault()?.InstanceID;
        }
        var (stat, desc) = ParseOrderBy(orderBy);
        var ordered = desc
            ? resources.OrderByDescending(r => GetStatValue(r, stat, cc))
            : resources.OrderBy(r => GetStatValue(r, stat, cc));
        return ordered.FirstOrDefault()?.InstanceID;
    }

    private static (string Stat, bool Desc) ParseOrderBy(string orderBy)
    {
        if (orderBy.EndsWith("_desc"))
        {
            return (orderBy[..^5], true);
        }
        if (orderBy.EndsWith("_asc"))
        {
            return (orderBy[..^4], false);
        }
        throw new InvalidOperationException(
            $"order_by must end with _desc or _asc: '{orderBy}'");
    }

    private static long GetStatValue(DeployedResource r, string stat, ICardCache cc)
    {
        return stat switch
        {
            "tp" => ResourceValue(r, cc),
            "av" => r.EffectiveAV,
            "damage" => r.Damage,
            _ => throw new InvalidOperationException($"Unknown order_by stat: '{stat}'"),
        };
    }

    private static IEnumerable<DeployedResource> FaceUpInZone(Field field, string? zone)
    {
        var sources = Enumerable.Empty<DeployedResource>();
        if (zone is null or "" or Zones.Frontend)
        {
            sources = sources.Concat(field.Frontend);
        }
        if (zone is null or "" or Zones.Backend)
        {
            sources = sources.Concat(field.Backend);
        }
        return sources.Where(r => r.FaceUp);
    }
}
