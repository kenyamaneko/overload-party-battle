namespace OverloadParty.Battle.Models;

/// <summary>
/// IEffectSource は効果定義群を EffectRegistry に登録できる供給元を表します。
/// カード効果と施策効果を同一インターフェースで対称に扱います。
/// </summary>
public interface IEffectSource
{
    /// <summary>EffectRegistry への登録キーを返します。</summary>
    string EffectSourceId { get; }

    /// <summary>登録対象の効果定義群を返します。</summary>
    IReadOnlyList<EffectDef> EffectDefs { get; }
}
