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
    /// <param name="field">対象のフィールド。</param>
    /// <param name="zone">フロントエンド / バックエンドの絞り込み。null なら両方。</param>
    /// <returns>該当リソースの InstanceID。なければ null。</returns>
    public static string? WeakestInZone(Field field, string? zone)
    {
        return FaceUpInZone(field, zone)
            .MinBy(r => r.EffectiveAV)
            ?.InstanceID;
    }

    /// <summary>
    /// Returns the instanceID of the resource with the highest TP or Yield.
    /// </summary>
    /// <param name="field">対象のフィールド。</param>
    /// <param name="zone">フロントエンド / バックエンドの絞り込み。null なら両方。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>該当リソースの InstanceID。なければ null。</returns>
    public static string? StrongestInZone(Field field, string? zone, ICardCache cc)
    {
        return FaceUpInZone(field, zone)
            .MaxBy(r => ResourceValue(r, cc))
            ?.InstanceID;
    }

    /// <summary>
    /// Returns the instanceID of the own resource with the most damage.
    /// </summary>
    /// <param name="field">自分のフィールド。</param>
    /// <returns>該当リソースの InstanceID。なければ null。</returns>
    public static string? MostDamagedOwn(Field field)
    {
        return FieldHelpers.AllFaceUpResources(field)
            .Where(r => r.Damage > 0)
            .MaxBy(r => r.Damage)
            ?.InstanceID;
    }

    /// <summary>
    /// フィールド上の表向きリソース数を返します。
    /// </summary>
    /// <param name="field">対象のフィールド。</param>
    /// <returns>表向きリソースの総数。</returns>
    public static int CountAllResources(Field field)
    {
        return FieldHelpers.AllFaceUpResources(field).Count;
    }

    /// <summary>
    /// 指定ゾーンの表向きリソース数を返します。
    /// </summary>
    /// <param name="field">対象のフィールド。</param>
    /// <param name="zone">フロントエンド / バックエンドの絞り込み。null なら両方。</param>
    /// <returns>該当ゾーンの表向きリソース数。</returns>
    public static int CountResourcesInZone(Field field, string? zone)
    {
        return FaceUpInZone(field, zone).Count();
    }

    /// <summary>
    /// フィールド上にダメージを受けたリソースが存在するか判定します。
    /// </summary>
    /// <param name="field">対象のフィールド。</param>
    /// <returns>1 体でも被ダメージ中なら true。</returns>
    public static bool HasDamagedResource(Field field)
    {
        return FieldHelpers.AllFaceUpResources(field).Any(r => r.Damage > 0);
    }

    /// <summary>
    /// サポートゾーンに裏向きのカードが存在するか判定します。
    /// </summary>
    /// <param name="field">対象のフィールド。</param>
    /// <returns>裏向きのサポートカードが 1 枚でもあれば true。</returns>
    public static bool HasFaceDownSupport(Field field)
    {
        return field.Support.Any(s => !s.FaceUp);
    }

    /// <summary>
    /// サポートゾーンにプラットフォームが存在するか判定します。
    /// </summary>
    /// <param name="field">対象のフィールド。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>プラットフォームが 1 枚でもあれば true。</returns>
    public static bool HasPlatform(Field field, ICardCache cc)
    {
        return field.Support
            .Any(sup => (cc.Get(sup.CardID)
                ?? throw new InvalidOperationException($"Card '{sup.CardID}' not found in card cache"))
                .CardType == CardTypes.Platform);
    }

    /// <summary>
    /// サポートゾーン最初のプラットフォームの InstanceID を返します。
    /// </summary>
    /// <param name="field">対象のフィールド。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>該当プラットフォームの InstanceID。なければ null。</returns>
    public static string? FirstPlatformId(Field field, ICardCache cc)
    {
        return field.Support
            .FirstOrDefault(sup => (cc.Get(sup.CardID)
                ?? throw new InvalidOperationException($"Card '{sup.CardID}' not found in card cache"))
                .CardType == CardTypes.Platform)
            ?.InstanceID;
    }

    /// <summary>
    /// リソースの強さを表すスコア (スループット or イールド) を返します。
    /// </summary>
    /// <param name="r">対象リソース。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>スループット or イールド値。</returns>
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
    /// <param name="spec">ターゲット仕様。</param>
    /// <param name="selfField">自分のフィールド。</param>
    /// <param name="oppField">相手のフィールド。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>解決したターゲットの InstanceID。該当なしなら null。</returns>
    public static string? Resolve(TargetSpec spec, Field selfField, Field oppField, ICardCache cc)
    {
        var field = ResolveField(spec.Selector, selfField, oppField);
        var resources = FilterResources(field, spec.Selector, cc);
        return OrderAndPick(resources, spec.OrderBy, cc);
    }

    /// <summary>
    /// Resolves a single target from a TargetSpec, constrained to the given valid target IDs.
    /// </summary>
    /// <param name="spec">ターゲット仕様。</param>
    /// <param name="validTargets">エンジンが許可するターゲット ID 集合。</param>
    /// <param name="selfField">自分のフィールド。</param>
    /// <param name="oppField">相手のフィールド。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>解決したターゲットの InstanceID。該当なしなら null。</returns>
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
    /// <param name="actions">ソート対象のアクション列。</param>
    /// <param name="orderBy">_desc / _asc サフィックス付きの並び順指定。</param>
    /// <param name="field">ソース リソースを含むフィールド。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>ソート済みのアクション列。</returns>
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
    /// <param name="field">対象のフィールド。</param>
    /// <param name="sel">セレクタ定義。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>条件を満たすリソース列。</returns>
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
