namespace OverloadParty.Battle.Engine.Effects;

/// <summary>
/// 効果の発動条件を表す述語。bool を返し例外を投げない。
/// <para>
/// 不成立 (false) の扱いは呼び出し側 (active = throw / passive = 空の結果 /
/// 従属サブブロック = スキップ) が決める。
/// </para>
/// </summary>
public interface IEffectGuard
{
    /// <summary>現在の context でこの発動条件が成立するか。</summary>
    /// <param name="ctx">効果実行コンテキスト。</param>
    /// <returns>成立する場合 true。</returns>
    bool Check(EffectContext ctx);
}
