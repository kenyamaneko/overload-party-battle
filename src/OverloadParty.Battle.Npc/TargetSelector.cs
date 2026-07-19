using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// TargetSelector は NPC のカテゴリベース意思決定向けターゲット選択ヘルパーを提供します。
/// 入出力は wire 型 (GD.Field / GD.OpponentField / GD.DeployedResource) を使う。
/// </summary>
public static class TargetSelector
{
    /// <summary>
    /// Returns the instanceID of the resource with the lowest effective AV.
    /// </summary>
    /// <param name="field">対象の自フィールド。</param>
    /// <param name="zone">フロントエンド / バックエンドの絞り込み。null なら両方。</param>
    /// <returns>該当リソースの InstanceID。なければ null。</returns>
    public static string? WeakestInZone(GD.Field field, string? zone)
    {
        return FaceUpInZone(field.Frontend, field.Backend, zone)
            .MinBy(WireFieldHelpers.CalculateEffectiveAV)
            ?.InstanceID;
    }

    /// <summary>
    /// Returns the instanceID of the resource with the highest TP or Yield.
    /// </summary>
    /// <param name="field">対象の自フィールド。</param>
    /// <param name="zone">フロントエンド / バックエンドの絞り込み。null なら両方。</param>
    /// <returns>該当リソースの InstanceID。なければ null。</returns>
    public static string? StrongestInZone(GD.Field field, string? zone)
    {
        return FaceUpInZone(field.Frontend, field.Backend, zone)
            .MaxBy(CalculateResourceValue)
            ?.InstanceID;
    }

    /// <summary>
    /// Returns the instanceID of the own resource with the most damage.
    /// </summary>
    /// <param name="field">自フィールド。</param>
    /// <returns>該当リソースの InstanceID。なければ null。</returns>
    public static string? MostDamagedOwn(GD.Field field)
    {
        return WireFieldHelpers.AllFaceUpResources(field)
            .Where(r => r.Damage > 0)
            .MaxBy(r => r.Damage)
            ?.InstanceID;
    }

    /// <summary>
    /// 自フィールド上の表向きリソース数を返します。
    /// </summary>
    /// <param name="field">対象の自フィールド。</param>
    /// <returns>表向きリソースの総数。</returns>
    public static int CountAllResources(GD.Field field) =>
        WireFieldHelpers.AllFaceUpResources(field).Count();

    /// <summary>
    /// 相手フィールド上の表向きリソース数を返します。
    /// </summary>
    /// <param name="field">対象の相手フィールド。</param>
    /// <returns>表向きリソースの総数。</returns>
    public static int CountAllResources(GD.OpponentField field) =>
        WireFieldHelpers.AllFaceUpResources(field).Count();

    /// <summary>
    /// 指定ゾーンの表向きリソース数を返します (自フィールド)。
    /// </summary>
    public static int CountResourcesInZone(GD.Field field, string? zone) =>
        FaceUpInZone(field.Frontend, field.Backend, zone).Count();

    /// <summary>
    /// 指定ゾーンの表向きリソース数を返します (相手フィールド)。
    /// </summary>
    public static int CountResourcesInZone(GD.OpponentField field, string? zone) =>
        FaceUpInZone(field.Frontend, field.Backend, zone).Count();

    /// <summary>
    /// 自フィールドにダメージを受けたリソースが存在するか判定します。
    /// </summary>
    /// <param name="field">対象の自フィールド。</param>
    /// <returns>1 体でも被ダメージ中なら true。</returns>
    public static bool HasDamagedResource(GD.Field field) =>
        WireFieldHelpers.AllFaceUpResources(field).Any(r => r.Damage > 0);

    /// <summary>
    /// 相手のサポートゾーンに裏向きのカードが存在するか判定します。
    /// </summary>
    /// <param name="field">対象の相手フィールド。</param>
    /// <returns>裏向きのサポートカードが 1 枚でもあれば true。</returns>
    public static bool HasFaceDownSupport(GD.OpponentField field) =>
        WireFieldHelpers.AllSupports(field).Any(s => s.FaceDown);

    /// <summary>
    /// 相手のサポートゾーンに公開可能 (表向きまたは覗き見済み) なプラットフォームが存在するか判定します。
    /// 情報秘匿のため、裏向き未覗き見のサポートは判定対象外。
    /// </summary>
    /// <param name="field">対象の相手フィールド。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>公開可能なプラットフォームが 1 枚でもあれば true。</returns>
    public static bool HasPlatform(GD.OpponentField field, ICardCache cc) =>
        WireFieldHelpers.AllSupports(field)
            .Where(s => s.CardID is not null)
            .Any(s => (cc.Get(s.CardID!)
                ?? throw new InvalidOperationException($"Card '{s.CardID}' not found in card cache"))
                .CardType == CardTypes.Platform);

    /// <summary>
    /// 相手のサポートゾーン最初の公開済みプラットフォームの InstanceID を返します。
    /// </summary>
    /// <param name="field">対象の相手フィールド。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>該当プラットフォームの InstanceID。なければ null。</returns>
    public static string? FindFirstPlatformId(GD.OpponentField field, ICardCache cc) =>
        WireFieldHelpers.AllSupports(field)
            .Where(s => s.CardID is not null)
            .FirstOrDefault(s => (cc.Get(s.CardID!)
                ?? throw new InvalidOperationException($"Card '{s.CardID}' not found in card cache"))
                .CardType == CardTypes.Platform)
            ?.InstanceID;

    /// <summary>
    /// リソースの強さを表すスコア (スループット or イールド) を返します。
    /// </summary>
    /// <param name="r">対象リソース。</param>
    /// <returns>スループット or イールド値。</returns>
    public static long CalculateResourceValue(GD.DeployedResource r)
    {
        if (r.CurrentTP is > 0)
        {
            return r.CurrentTP.Value;
        }
        if (r.CurrentYield is > 0)
        {
            return r.CurrentYield.Value;
        }
        return 0;
    }

    // ─── TargetSpec resolution ──────────────────────────────────

    /// <summary>
    /// Resolves a single target from a TargetSpec.
    /// </summary>
    /// <param name="spec">ターゲット仕様。</param>
    /// <param name="myField">自フィールド。</param>
    /// <param name="oppField">相手フィールド。</param>
    /// <param name="cc">カード定義の参照元。</param>
    /// <returns>解決したターゲットの InstanceID。該当なしなら null。</returns>
    public static string? Resolve(TargetSpec spec, GD.Field myField, GD.OpponentField oppField, ICardCache cc)
    {
        var resources = FilterResources(spec.Selector, myField, oppField, cc);
        return OrderAndPick(resources, spec.OrderBy);
    }

    /// <summary>
    /// Resolves a single target from a TargetSpec, constrained to the given valid target IDs.
    /// </summary>
    public static string? ResolveFromValid(
        TargetSpec spec, List<string> validTargets,
        GD.Field myField, GD.OpponentField oppField, ICardCache cc)
    {
        var validSet = new HashSet<string>(validTargets);
        var resources = FilterResources(spec.Selector, myField, oppField, cc)
            .Where(r => validSet.Contains(r.InstanceID));
        return OrderAndPick(resources, spec.OrderBy);
    }

    /// <summary>
    /// Orders AvailableActions by the given order_by stat applied to their source resource on the given field.
    /// </summary>
    /// <typeparam name="T">並べ替える AvailableAction variant 型。</typeparam>
    /// <param name="actions">並べ替え対象のアクション列。</param>
    /// <param name="sourceInstanceId">アクションから source リソースの InstanceID を取り出す関数。</param>
    /// <param name="orderBy">並べ替えに使う stat 指定。</param>
    /// <param name="field">stat を引く自フィールド。</param>
    /// <returns>並べ替え済みのアクション列。</returns>
    public static List<T> OrderActions<T>(
        List<T> actions, Func<T, string?> sourceInstanceId, string orderBy, GD.Field field)
        where T : GD.AvailableAction
    {
        var resMap = WireFieldHelpers.AllResources(field).ToDictionary(r => r.InstanceID);
        var (stat, desc) = ParseOrderBy(orderBy);
        long Selector(T a) =>
            resMap.TryGetValue(sourceInstanceId(a)!, out var r)
                ? GetStatValue(r, stat) : 0;
        return (desc
            ? actions.OrderByDescending(Selector)
            : actions.OrderBy(Selector)).ToList();
    }

    /// <summary>
    /// セレクタ条件に一致するリソースを返します。
    /// </summary>
    public static IEnumerable<GD.DeployedResource> FilterResources(
        SelectorDef sel, GD.Field myField, GD.OpponentField oppField, ICardCache cc)
    {
        var (frontend, backend) = sel.Owner switch
        {
            "myself" => (myField.Frontend, myField.Backend),
            "opponent" => (oppField.Frontend, oppField.Backend),
            var o => throw new InvalidOperationException($"Unknown selector owner: '{o}'"),
        };
        return FilterOnZones(frontend, backend, sel, cc);
    }

    // ─── Private helpers ────────────────────────────────────────

    private static IEnumerable<GD.DeployedResource> FilterOnZones(
        List<GD.DeployedResource?> frontend, List<GD.DeployedResource?> backend,
        SelectorDef sel, ICardCache cc)
    {
        IEnumerable<GD.DeployedResource> resources = sel.FaceDown == true
            ? frontend.Concat(backend).Where(r => r is not null).Select(r => r!).Where(r => !r.FaceUp)
            : frontend.Concat(backend).Where(r => r is not null).Select(r => r!).Where(r => r.FaceUp);

        if (sel.Zone is not null)
        {
            resources = sel.Zone switch
            {
                Zones.Frontend => frontend
                    .Where(r => r is not null && (sel.FaceDown == true || r.FaceUp))
                    .Select(r => r!),
                Zones.Backend => backend
                    .Where(r => r is not null && (sel.FaceDown == true || r.FaceUp))
                    .Select(r => r!),
                _ => Enumerable.Empty<GD.DeployedResource>(),
            };
        }

        if (sel.Faction is not null)
        {
            resources = resources.Where(r =>
                ResolveCard(cc, r.CardID).Faction == sel.Faction);
        }

        if (sel.CardType is not null)
        {
            resources = resources.Where(r =>
                OverloadParty.Battle.Engine.Effects.EffectHelpers.MatchesCardType(
                    ResolveCard(cc, r.CardID), sel.CardType));
        }

        if (sel.CardId is not null)
        {
            var cardIds = new HashSet<string>(sel.CardId);
            resources = resources.Where(r => cardIds.Contains(r.CardID));
        }

        return resources;
    }

    private static CardDefinition ResolveCard(ICardCache cc, string cardId) =>
        cc.Get(cardId)
            ?? throw new InvalidOperationException($"Card '{cardId}' not found in card cache");

    private static string? OrderAndPick(
        IEnumerable<GD.DeployedResource> resources, string? orderBy)
    {
        if (orderBy is null)
        {
            return resources.FirstOrDefault()?.InstanceID;
        }
        var (stat, desc) = ParseOrderBy(orderBy);
        var ordered = desc
            ? resources.OrderByDescending(r => GetStatValue(r, stat))
            : resources.OrderBy(r => GetStatValue(r, stat));
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

    private static long GetStatValue(GD.DeployedResource r, string stat)
    {
        return stat switch
        {
            "tp" => CalculateResourceValue(r),
            "av" => WireFieldHelpers.CalculateEffectiveAV(r),
            "damage" => r.Damage,
            _ => throw new InvalidOperationException($"Unknown order_by stat: '{stat}'"),
        };
    }

    private static IEnumerable<GD.DeployedResource> FaceUpInZone(
        List<GD.DeployedResource?> frontend, List<GD.DeployedResource?> backend, string? zone)
    {
        IEnumerable<GD.DeployedResource?> sources = Enumerable.Empty<GD.DeployedResource?>();
        if (zone is null or "" or Zones.Frontend)
        {
            sources = sources.Concat(frontend);
        }
        if (zone is null or "" or Zones.Backend)
        {
            sources = sources.Concat(backend);
        }
        return sources.Where(r => r is not null && r.FaceUp).Select(r => r!);
    }
}
