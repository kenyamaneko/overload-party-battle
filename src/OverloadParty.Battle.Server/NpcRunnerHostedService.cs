using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Server;

/// <summary>
/// Resolves <see cref="NpcRunner"/> from DI at startup so that its constructor
/// (which runs <c>AiConfigValidator</c> against every loaded AI config) fires before
/// the HTTP server starts accepting requests. Any validation failure — e.g. an unknown
/// <c>card_id</c> referenced by an NPC deck — is propagated out of <see cref="StartAsync"/>
/// and crashes startup, matching our fail-fast policy.
///
/// Must be registered AFTER <see cref="ICardCache"/> is populated (the validator reads
/// cards from the cache), which Program.cs guarantees by loading cards before
/// <c>app.Run()</c>.
/// </summary>
public sealed class NpcRunnerHostedService : IHostedService
{
    private readonly IServiceProvider _services;

    public NpcRunnerHostedService(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>HTTP サーバ受付前に <see cref="NpcRunner"/> を DI から解決して起動時バリデーションを発火させる。</summary>
    /// <param name="cancellationToken">ホスト起動のキャンセル通知。</param>
    /// <returns>起動完了を表すタスク。</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = _services.GetRequiredService<NpcRunner>();
        return Task.CompletedTask;
    }

    /// <summary>ホスト停止時のフックで、本サービスでは追加処理を持たない。</summary>
    /// <param name="cancellationToken">ホスト停止のキャンセル通知。</param>
    /// <returns>停止完了を表すタスク。</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
