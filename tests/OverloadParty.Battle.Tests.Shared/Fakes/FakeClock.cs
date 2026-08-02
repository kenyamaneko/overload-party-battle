namespace OverloadParty.Battle.Tests.Fakes;

/// <summary>IClock の test double。テストから現在時刻を進められる。</summary>
public class FakeClock : IClock
{
    /// <summary>基準となる固定時刻。実時刻に依存しない値を使う。</summary>
    public static readonly DateTime Origin = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>現在の UTC 時刻。</summary>
    public DateTime UtcNow { get; private set; } = Origin;

    /// <summary>現在時刻を指定秒だけ進める。</summary>
    /// <param name="seconds">進める秒数。小数で 1 秒未満も指定できる。</param>
    public void Advance(double seconds) => UtcNow = UtcNow.AddSeconds(seconds);
}
