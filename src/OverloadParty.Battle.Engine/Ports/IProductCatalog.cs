using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Ports;

/// <summary>
/// 陣営からプロダクト定義を解決する読み取り専用カタログ。施策の使用回数判定と
/// Insight コストの参照に使う。Engine はこのインターフェースにのみ依存し、実装は
/// Data 層に置く。
/// </summary>
public interface IProductCatalog
{
    /// <summary>指定した陣営のプロダクトを返します。存在しなければ null を返します。</summary>
    /// <param name="faction">解決対象の陣営。</param>
    /// <returns>陣営に紐づくプロダクト。未登録なら null。</returns>
    Product? GetByFaction(string faction);
}
