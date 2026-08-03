using OverloadParty.Battle.Engine.Ports;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Engine;

/// <summary>
/// 施策を ID で解決するインメモリ実装。起動時に読み込んだ施策定義を保持する。
/// </summary>
public class InitiativeCatalog : IInitiativeCatalog
{
    private readonly Dictionary<string, Initiative> _byId;

    /// <summary>施策定義の一覧から ID インデックスを構築します。</summary>
    /// <param name="initiatives">保持する施策定義の一覧。</param>
    public InitiativeCatalog(IEnumerable<Initiative> initiatives)
    {
        _byId = initiatives.ToDictionary(i => i.InitiativeId);
    }

    /// <inheritdoc />
    public Initiative? GetById(string initiativeId) => _byId.GetValueOrDefault(initiativeId);
}
