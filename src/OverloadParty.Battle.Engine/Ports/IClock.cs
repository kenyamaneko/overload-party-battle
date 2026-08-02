namespace OverloadParty.Battle.Engine.Ports;

/// <summary>
/// 現在時刻を供給する。タイムバンクの計測に用いる。
/// </summary>
public interface IClock
{
    /// <summary>現在の UTC 時刻を返す。</summary>
    DateTime UtcNow { get; }
}
