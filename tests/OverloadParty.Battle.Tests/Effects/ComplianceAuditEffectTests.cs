using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

/// <summary>
/// Tests for card #112 Compliance Audit effect.
/// Effect: Pay 200 Budget. Opponent loses 400 Budget.
/// If opponent has no ISMS (#96) or SOC2 (#97) platform on field, opponent loses an additional 400 Budget.
/// </summary>
public class ComplianceAuditEffectTests
{
    private const long CardNo = 112;
    private const long IsmsPlatformNo = 96;
    private const long Soc2PlatformNo = 97;

    private readonly TestCardCache _cc;
    private readonly EffectRegistry _registry;
    private readonly Game _game;

    public ComplianceAuditEffectTests()
    {
        _cc = new TestCardCache();
        // Add card definitions needed for the effect
        _cc.Add(new CardDefinition { CardNo = CardNo, CardName = "コンプライアンス監査", CardType = "Incident" });
        _cc.Add(new CardDefinition { CardNo = IsmsPlatformNo, CardName = "ISMS認証", CardType = "Platform" });
        _cc.Add(new CardDefinition { CardNo = Soc2PlatformNo, CardName = "SOC2認証", CardType = "Platform" });

        _registry = new EffectRegistry();
        EffectInit.RegisterAllEffects(_registry);

        _game = TestFactory.MakeGame();
    }

    // ─── 基本発動コスト ──────────────────────────────────────────

    [Fact]
    public void Activate_PaysCost200_FromSelfBudget()
    {
        var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
        AddComplianceAuditSupport(state, playerNum: 1);

        ExecuteEffect(state, playerNum: 1);

        state.Player1Budget.Should().Be(800, "self pays 200");
    }

    [Fact]
    public void Activate_InsufficientBudget_Throws()
    {
        var state = TestFactory.MakeGameState(p1Budget: 100, p2Budget: 2000);
        AddComplianceAuditSupport(state, playerNum: 1);

        var act = () => ExecuteEffect(state, playerNum: 1);

        act.Should().Throw<GameRuleException>().WithMessage("*budget*");
    }

    // ─── 相手 Budget 減少 ─────────────────────────────────────────

    [Fact]
    public void Activate_WithoutCompliancePlatform_OpponentLoses800()
    {
        var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
        AddComplianceAuditSupport(state, playerNum: 1);
        // 相手フィールドに ISMS/SOC2 なし

        ExecuteEffect(state, playerNum: 1);

        state.Player2Budget.Should().Be(1200, "opponent loses 400 base + 400 extra = 800");
    }

    [Fact]
    public void Activate_WithIsmsPlatform_OpponentLoses400Only()
    {
        var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
        AddComplianceAuditSupport(state, playerNum: 1);
        AddPlatformToField(state.Player2Field, cardId: IsmsPlatformNo);

        ExecuteEffect(state, playerNum: 1);

        state.Player2Budget.Should().Be(1600, "opponent has ISMS so loses only base 400");
    }

    [Fact]
    public void Activate_WithSoc2Platform_OpponentLoses400Only()
    {
        var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
        AddComplianceAuditSupport(state, playerNum: 1);
        AddPlatformToField(state.Player2Field, cardId: Soc2PlatformNo);

        ExecuteEffect(state, playerNum: 1);

        state.Player2Budget.Should().Be(1600, "opponent has SOC2 so loses only base 400");
    }

    [Fact]
    public void Activate_WithBothPlatforms_OpponentLoses400Only()
    {
        var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
        AddComplianceAuditSupport(state, playerNum: 1);
        AddPlatformToField(state.Player2Field, cardId: IsmsPlatformNo);
        AddPlatformToField(state.Player2Field, cardId: Soc2PlatformNo, slotIndex: 1);

        ExecuteEffect(state, playerNum: 1);

        state.Player2Budget.Should().Be(1600, "opponent has both compliance platforms so loses only base 400");
    }

    // ─── フェイスダウン・未展開のプラットフォームは無効 ─────────────────

    [Fact]
    public void Activate_WithFaceDownIsmsPlatform_OpponentLoses800()
    {
        var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
        AddComplianceAuditSupport(state, playerNum: 1);
        // フェイスダウン（まだ展開中）のプラットフォームは無効
        AddPlatformToField(state.Player2Field, cardId: IsmsPlatformNo, faceUp: false);

        ExecuteEffect(state, playerNum: 1);

        state.Player2Budget.Should().Be(1200, "face-down ISMS does not count as active compliance platform");
    }

    [Fact]
    public void Activate_WithDeployingIsmsPlatform_OpponentLoses800()
    {
        var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
        AddComplianceAuditSupport(state, playerNum: 1);
        // DeployingTurnsLeft > 0 のプラットフォームは有効でない
        AddPlatformToField(state.Player2Field, cardId: IsmsPlatformNo, deployingTurnsLeft: 1);

        ExecuteEffect(state, playerNum: 1);

        state.Player2Budget.Should().Be(1200, "still-deploying ISMS does not count as active compliance platform");
    }

    // ─── Player 2 が発動する場合 ─────────────────────────────────

    [Fact]
    public void Activate_AsPlayer2_ReducesPlayer1Budget()
    {
        var state = TestFactory.MakeGameState(activePlayer: 2, p1Budget: 2000, p2Budget: 1000);
        AddComplianceAuditSupport(state, playerNum: 2);
        // Player1 に ISMS/SOC2 なし

        ExecuteEffect(state, playerNum: 2);

        state.Player2Budget.Should().Be(800, "player2 pays 200");
        state.Player1Budget.Should().Be(1200, "player1 loses 400 + 400 extra = 800");
    }

    // ─── Helper methods ───────────────────────────────────────────

    private EffectResult ExecuteEffect(GameState state, long playerNum)
    {
        var handler = _registry.Get(CardNo, TriggerType.Activate)
            ?? throw new InvalidOperationException("Card #112 handler not registered");

        var ctx = new EffectContext
        {
            State = state,
            Game = _game,
            PlayerNum = playerNum,
            CardCache = _cc,
        };

        return handler(ctx);
    }

    private static void AddComplianceAuditSupport(GameState state, long playerNum)
    {
        var field = playerNum == 1 ? state.Player1Field : state.Player2Field;
        field.Support[0] = new SupportInstance
        {
            InstanceID = "sup_audit",
            CardID = CardNo,
            FaceUp = true,
            DeployingTurnsLeft = 0,
        };
    }

    private static void AddPlatformToField(
        Field field,
        long cardId,
        int slotIndex = 0,
        bool faceUp = true,
        long deployingTurnsLeft = 0)
    {
        field.Support[slotIndex] = new SupportInstance
        {
            InstanceID = $"sup_platform_{slotIndex}",
            CardID = cardId,
            FaceUp = faceUp,
            DeployingTurnsLeft = deployingTurnsLeft,
        };
    }
}
