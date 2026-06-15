using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine.Ports;

/// <summary>
/// 施策定義を ID で解決する読み取り専用カタログ。Engine はこのインターフェースにのみ依存する。
/// </summary>
public interface IInitiativeCatalog
{
    /// <summary>指定 ID の施策を返します。存在しなければ null を返します。</summary>
    /// <param name="initiativeId">解決対象の施策 ID。</param>
    /// <returns>施策定義。未登録なら null。</returns>
    Initiative? GetById(string initiativeId);
}
