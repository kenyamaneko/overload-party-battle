using Microsoft.AspNetCore.Mvc.Testing;

namespace OverloadParty.Battle.Tests.Server;

/// <summary>
/// 起動時の設定不備で失敗する経路の検証。プロセス環境変数を退避・破壊・復元するため、
/// 他の Server.Tests クラスと同一コレクションに置き並列実行を止める。
/// コンストラクタ引数の ServerTestFixture は、同一コレクションの fixture 初期化 (DATABASE_CONN 等の
/// 「正常な起動が可能な状態」の設定) が本クラスのテスト実行前に完了することを xUnit に保証させるためだけに受け取る。
/// </summary>
[Trait("対象", "起動時設定の検証")]
[Collection(ServerTestCollection.Name)]
public class StartupConfigValidationTests(ServerTestFixture fixture)
{
    private static void WithEnvironmentVariable(string name, string? value, Action act)
    {
        var original = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, value);
            act();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }

    private static Action BuildFactory()
    {
        return () =>
        {
            using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();
        };
    }

    [Fact(DisplayName = "データベース接続文字列が未設定だと、起動に失敗する")]
    public void MissingDatabaseConn_FailsStartup()
    {
        WithEnvironmentVariable("DATABASE_CONN", null, () =>
        {
            var act = BuildFactory();

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*DATABASE_CONN*");
        });
    }

    [Fact(DisplayName = "NPC の AI 設定ディレクトリが未設定だと、起動に失敗する")]
    public void MissingNpcAiConfigDir_FailsStartup()
    {
        WithEnvironmentVariable("NPC_AI_CONFIG_DIR", null, () =>
        {
            var act = BuildFactory();

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*NPC_AI_CONFIG_DIR*");
        });
    }

    [Fact(DisplayName = "NPC の AI 設定ディレクトリが実在しないと、起動に失敗する")]
    public void NonexistentNpcAiConfigDir_FailsStartup()
    {
        WithEnvironmentVariable("NPC_AI_CONFIG_DIR", "/tmp/TST-nonexistent-npc-ai-config-dir", () =>
        {
            var act = BuildFactory();

            act.Should().Throw<DirectoryNotFoundException>();
        });
    }

    [Fact(DisplayName = "ローカル開発モードでカードデータファイルが実在しないと、起動に失敗する")]
    public void NonexistentLocalCardsFile_FailsStartup()
    {
        WithEnvironmentVariable("CARDS_JSON_PATH", "/tmp/TST-nonexistent-cards.json", () =>
        {
            var act = BuildFactory();

            act.Should().Throw<FileNotFoundException>();
        });
    }
}
