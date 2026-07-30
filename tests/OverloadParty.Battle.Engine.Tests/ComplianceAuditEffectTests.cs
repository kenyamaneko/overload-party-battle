using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

/// <summary>
/// カード #112 コンプライアンス監査の起動効果を検証する。
/// 効果: バジェット 200 を支払う。相手のバジェットが 400 減る。
/// 相手のフィールドに ISMS認証 (#96) も SOC2認証 (#97) もなければ、相手のバジェットがさらに 400 減る。
/// </summary>
public class ComplianceAuditEffectTests
{
    private const string CardId = "NT-0021";
    private const string IsmsPlatformNo = "NT-0005";
    private const string Soc2PlatformNo = "NT-0006";

    /// <summary>コンプライアンス監査の起動効果を使用する。</summary>
    /// <param name="state">対象のゲーム状態。</param>
    /// <param name="playerNum">起動するプレイヤー番号。</param>
    private static void Ignite(BattleGameState state, long playerNum)
    {
        var (effects, cc) = TestEffectSetup.Get();
        UseIgnitionProcessor.Process(
            state, TestFactory.MakeGame(), playerNum,
            new UseIgnitionRequest { InstanceID = "sup_audit" }, cc, effects);
    }

    /// <summary>表向きで発動可能なコンプライアンス監査サポートを配置する。</summary>
    /// <param name="state">ゲーム状態を変更する対象。</param>
    /// <param name="playerNum">サポートゾーンにカードを持つプレイヤー番号。</param>
    private static void AddComplianceAuditSupport(BattleGameState state, long playerNum)
    {
        var field = playerNum == 1 ? state.Player1Field : state.Player2Field;
        field.Support[0] = new DeployedSupport
        {
            InstanceID = "sup_audit",
            CardID = CardId,
            FaceUp = true,
            DeployingTurnsLeft = 0,
        };
    }

    /// <summary>指定フィールドのサポートゾーンにプラットフォームカードを置く。</summary>
    /// <param name="field">プラットフォームを持つフィールド。</param>
    /// <param name="cardId">配置するプラットフォームのカード ID。</param>
    /// <param name="slotIndex">サポートスロットの index。</param>
    /// <param name="faceUp">表向きかどうか。</param>
    /// <param name="deployingTurnsLeft">残りデプロイターン数 (0 なら稼働完了)。</param>
    private static void AddPlatformToField(
        Field field,
        string cardId,
        int slotIndex = 0,
        bool faceUp = true,
        long deployingTurnsLeft = 0)
    {
        field.Support[slotIndex] = new DeployedSupport
        {
            InstanceID = $"sup_platform_{slotIndex}",
            CardID = cardId,
            FaceUp = faceUp,
            DeployingTurnsLeft = deployingTurnsLeft,
        };
    }

    [Trait("対象", "コンプライアンス監査の起動コスト")]
    public class ActivationCost
    {
        [Fact(DisplayName = "起動すると自分のバジェットから 200 を支払う")]
        public void Ignite_PaysCost200_FromSelfBudget()
        {
            var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
            AddComplianceAuditSupport(state, playerNum: 1);

            Ignite(state, playerNum: 1);

            state.Player1Budget.Should().Be(800, "myself pays 200");
        }

        [Fact(DisplayName = "バジェットが 100 で 200 を支払えないとき発動条件を満たさずバジェットは変わらない")]
        public void Ignite_InsufficientBudget_GuardFails()
        {
            var state = TestFactory.MakeGameState(p1Budget: 100, p2Budget: 2000);
            AddComplianceAuditSupport(state, playerNum: 1);

            Ignite(state, playerNum: 1);

            state.Player1Budget.Should().Be(100, "budget should not change when guard fails");
            state.Player2Budget.Should().Be(2000, "opponent budget should not change when guard fails");
        }
    }

    [Trait("対象", "相手のバジェット減少量")]
    public class OpponentBudgetLoss
    {
        [Fact(DisplayName = "相手に ISMS も SOC2 もないとき相手のバジェットが 800 減る")]
        public void Ignite_WithoutCompliancePlatform_OpponentLoses800()
        {
            var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
            AddComplianceAuditSupport(state, playerNum: 1);
            // 相手フィールドに ISMS/SOC2 なし

            Ignite(state, playerNum: 1);

            state.Player2Budget.Should().Be(1200, "opponent loses 400 base + 400 extra = 800");
        }

        [Fact(DisplayName = "相手に ISMS があるとき相手のバジェットは 400 だけ減る")]
        public void Ignite_WithIsmsPlatform_OpponentLoses400Only()
        {
            var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
            AddComplianceAuditSupport(state, playerNum: 1);
            AddPlatformToField(state.Player2Field, cardId: IsmsPlatformNo);

            Ignite(state, playerNum: 1);

            state.Player2Budget.Should().Be(1600, "opponent has ISMS so loses only base 400");
        }

        [Fact(DisplayName = "相手に SOC2 があるとき相手のバジェットは 400 だけ減る")]
        public void Ignite_WithSoc2Platform_OpponentLoses400Only()
        {
            var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
            AddComplianceAuditSupport(state, playerNum: 1);
            AddPlatformToField(state.Player2Field, cardId: Soc2PlatformNo);

            Ignite(state, playerNum: 1);

            state.Player2Budget.Should().Be(1600, "opponent has SOC2 so loses only base 400");
        }

        [Fact(DisplayName = "相手に ISMS と SOC2 の両方があるとき相手のバジェットは 400 だけ減る")]
        public void Ignite_WithBothPlatforms_OpponentLoses400Only()
        {
            var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
            AddComplianceAuditSupport(state, playerNum: 1);
            AddPlatformToField(state.Player2Field, cardId: IsmsPlatformNo);
            AddPlatformToField(state.Player2Field, cardId: Soc2PlatformNo, slotIndex: 1);

            Ignite(state, playerNum: 1);

            state.Player2Budget.Should().Be(1600, "opponent has both compliance platforms so loses only base 400");
        }
    }

    [Trait("対象", "稼働していないプラットフォームの扱い")]
    public class InactivePlatform
    {
        [Fact(DisplayName = "相手の ISMS が裏向きのとき稼働中とみなされず相手のバジェットが 800 減る")]
        public void Ignite_WithFaceDownIsmsPlatform_OpponentLoses800()
        {
            var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
            AddComplianceAuditSupport(state, playerNum: 1);
            // 裏向き（まだ展開中）のプラットフォームは無効
            AddPlatformToField(state.Player2Field, cardId: IsmsPlatformNo, faceUp: false);

            Ignite(state, playerNum: 1);

            state.Player2Budget.Should().Be(1200, "face-down ISMS does not count as active compliance platform");
        }

        [Fact(DisplayName = "相手の ISMS がデプロイ中のとき稼働中とみなされず相手のバジェットが 800 減る")]
        public void Ignite_WithDeployingIsmsPlatform_OpponentLoses800()
        {
            var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
            AddComplianceAuditSupport(state, playerNum: 1);
            // DeployingTurnsLeft > 0 のプラットフォームは有効でない
            AddPlatformToField(state.Player2Field, cardId: IsmsPlatformNo, deployingTurnsLeft: 1);

            Ignite(state, playerNum: 1);

            state.Player2Budget.Should().Be(1200, "still-deploying ISMS does not count as active compliance platform");
        }
    }

    [Trait("対象", "プレイヤー2による起動")]
    public class ActivatedByPlayer2
    {
        [Fact(DisplayName = "プレイヤー2が起動すると自分が 200 を支払いプレイヤー1のバジェットが 800 減る")]
        public void Ignite_AsPlayer2_ReducesPlayer1Budget()
        {
            var state = TestFactory.MakeGameState(activePlayer: 2, p1Budget: 2000, p2Budget: 1000);
            AddComplianceAuditSupport(state, playerNum: 2);
            // Player1 に ISMS/SOC2 なし

            Ignite(state, playerNum: 2);

            state.Player2Budget.Should().Be(800, "player2 pays 200");
            state.Player1Budget.Should().Be(1200, "player1 loses 400 + 400 extra = 800");
        }
    }
}
