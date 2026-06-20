using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Npc;

/// <summary>
/// 情報秘匿済み wire ビュー (GD.Field / GD.OpponentField) 上のフィールド走査ヘルパー。
/// 空きスロット (null) を除外しながら列挙する。
/// </summary>
internal static class WireFieldHelpers
{
    /// <summary>自フィールドの全リソース (空きスロット除外)。</summary>
    public static IEnumerable<GD.DeployedResource> AllResources(GD.Field field) =>
        Concat(field.Frontend, field.Backend);

    /// <summary>相手フィールドの全リソース (空きスロット除外)。face-down 含む。</summary>
    public static IEnumerable<GD.DeployedResource> AllResources(GD.OpponentField field) =>
        Concat(field.Frontend, field.Backend);

    /// <summary>自フィールドの表向きリソースのみ。</summary>
    public static IEnumerable<GD.DeployedResource> AllFaceUpResources(GD.Field field) =>
        AllResources(field).Where(r => r.FaceUp);

    /// <summary>相手フィールドの表向きリソースのみ。</summary>
    public static IEnumerable<GD.DeployedResource> AllFaceUpResources(GD.OpponentField field) =>
        AllResources(field).Where(r => r.FaceUp);

    /// <summary>自フィールドのサポート (空きスロット除外)。</summary>
    public static IEnumerable<GD.DeployedSupport> AllSupports(GD.Field field) =>
        field.Support.Where(s => s is not null).Select(s => s!);

    /// <summary>相手フィールドのサポート (空きスロット除外、情報秘匿済み)。</summary>
    public static IEnumerable<GD.HiddenDeployedSupport> AllSupports(GD.OpponentField field) =>
        field.Support.Where(s => s is not null).Select(s => s!);

    /// <summary>自フロントエンドに表向きリソースが 1 体でもあるか。</summary>
    public static bool HasFrontendResources(GD.Field field) =>
        field.Frontend.Any(r => r is not null && r.FaceUp);

    /// <summary>リソースの実効 AV (= MaxAV - Damage)。</summary>
    public static long CalculateEffectiveAV(GD.DeployedResource r) => r.MaxAV - r.Damage;

    private static IEnumerable<GD.DeployedResource> Concat(
        List<GD.DeployedResource?> a, List<GD.DeployedResource?> b)
    {
        return a.Concat(b).Where(r => r is not null).Select(r => r!);
    }
}
