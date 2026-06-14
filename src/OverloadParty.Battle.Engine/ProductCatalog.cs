using OverloadParty.Battle.Engine.Ports;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// プロダクトを ID で解決するインメモリ実装。起動時に card サービスから取得した
/// プロダクト定義を保持する。
/// </summary>
public class ProductCatalog : IProductCatalog
{
    private readonly Dictionary<string, Product> _byId;

    /// <summary>プロダクト定義の一覧から ID インデックスを構築します。</summary>
    /// <param name="products">保持するプロダクト定義の一覧。</param>
    public ProductCatalog(IEnumerable<Product> products)
    {
        _byId = products.ToDictionary(p => p.ProductId);
    }

    /// <inheritdoc />
    public Product? GetById(string productId) => _byId.GetValueOrDefault(productId);
}
