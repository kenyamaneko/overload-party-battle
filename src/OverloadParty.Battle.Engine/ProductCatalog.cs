using OverloadParty.Battle.Engine.Ports;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// 陣営からプロダクトを解決するインメモリ実装。起動時に card サービスから取得した
/// プロダクト定義を保持する。
/// </summary>
public class ProductCatalog : IProductCatalog
{
    private readonly Dictionary<string, Product> _byFaction;

    /// <summary>プロダクト定義の一覧から陣営インデックスを構築します。</summary>
    /// <param name="products">保持するプロダクト定義の一覧。</param>
    public ProductCatalog(IEnumerable<Product> products)
    {
        _byFaction = products.ToDictionary(p => p.Faction);
    }

    /// <inheritdoc />
    public Product? GetByFaction(string faction) => _byFaction.GetValueOrDefault(faction);
}
