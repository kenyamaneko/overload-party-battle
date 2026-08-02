using OverloadParty.Battle.Engine.Ports;

namespace OverloadParty.Battle.Data;

/// <summary>
/// システム時刻を供給する <see cref="IClock"/> の実装。
/// </summary>
public class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTime UtcNow => DateTime.UtcNow;
}
