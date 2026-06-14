using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Ports;

/// <summary>
/// プロダクト定義を ID で解決する読み取り専用カタログ。施策の使用回数判定と
/// Insight コストの参照に使う。Engine はこのインターフェースにのみ依存する。
/// </summary>
public interface IProductCatalog
{
    /// <summary>指定 ID のプロダクトを返します。存在しなければ null を返します。</summary>
    /// <param name="productId">解決対象のプロダクト ID。</param>
    /// <returns>プロダクト定義。未登録なら null。</returns>
    Product? GetById(string productId);
}
