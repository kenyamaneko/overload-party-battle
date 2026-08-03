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
    private static void WithEnvironmentVariable(string name, string? value, Action act) =>
        WithEnvironmentVariables(new Dictionary<string, string?> { [name] = value }, act);

    private static void WithEnvironmentVariables(IReadOnlyDictionary<string, string?> variables, Action act)
    {
        var originals = variables.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (name, value) in variables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
            act();
        }
        finally
        {
            foreach (var (name, original) in originals)
            {
                Environment.SetEnvironmentVariable(name, original);
            }
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

    [Trait("対象", "データベース接続の設定検証")]
    [Collection(ServerTestCollection.Name)]
    public class DatabaseConnection(ServerTestFixture fixture)
    {
        [Fact(DisplayName = "IAM 認証を有効にする設定のとき、接続ユーザーが未指定なら起動に失敗する")]
        public void IamAuthWithoutConnectionUser_FailsStartup()
        {
            WithEnvironmentVariables(
                new Dictionary<string, string?>
                {
                    ["DATABASE_IAM_AUTH_ENABLED"] = "true",
                    ["DATABASE_CONN"] = "Host=/cloudsql/TST-project:TST-region:TST-instance;Database=battle;Search Path=battle;SSL Mode=Disable",
                },
                () =>
                {
                    var act = BuildFactory();

                    act.Should().Throw<InvalidOperationException>()
                        .WithMessage("*Username*");
                });
        }

        [Fact(DisplayName = "IAM 認証の有効・無効の指定が未設定のとき、起動に失敗する")]
        public void MissingIamAuthFlag_FailsStartup()
        {
            WithEnvironmentVariable("DATABASE_IAM_AUTH_ENABLED", null, () =>
            {
                var act = BuildFactory();

                act.Should().Throw<InvalidOperationException>()
                    .WithMessage("*DATABASE_IAM_AUTH_ENABLED is not set*");
            });
        }

        [Fact(DisplayName = "IAM 認証の有効・無効の指定が true でも false でもない値のとき、起動に失敗する")]
        public void InvalidIamAuthFlag_FailsStartup()
        {
            WithEnvironmentVariable("DATABASE_IAM_AUTH_ENABLED", "enabled", () =>
            {
                var act = BuildFactory();

                act.Should().Throw<InvalidOperationException>()
                    .WithMessage("*got \"enabled\"*");
            });
        }
    }
}
