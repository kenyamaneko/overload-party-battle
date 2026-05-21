using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// reactive 効果が選択待ちで suspend / resume するフローを 4 種類のカードで確認するテスト。
/// </summary>
public class PendingReactiveChoiceTests
{
    private readonly EffectRegistry _registry;
    private readonly ICardCache _cc;
    private readonly Game _game = TestFactory.MakeGame();

    public PendingReactiveChoiceTests()
    {
        (_registry, _cc) = TestEffectSetup.Get();
    }

    // ─── NT-0023: 同名コンピュート系を手札からデプロイ (chooser = owner) ──

    [Fact]
    public void NT0023_OnDestroy_WithMatchingHandCard_SuspendsForHandChoice()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TK-0001", ArtNo = 1 });
        var supSource = new DeployedSupport
        {
            InstanceID = "react_1",
            CardID = "NT-0023",
            FaceUp = false,
            DeployOrder = 1,
        };
        var destroyed = TestFactory.MakeResource(cardId: "TK-0001", instanceId: "dest_1");

        var handler = _registry.Get("NT-0023", TriggerType.OnDestroy)!;
        var result = handler(MakeCtx(
            state,
            playerNum: 1,
            trigger: TriggerType.OnDestroy,
            target: destroyed,
            supSource: supSource,
            eventOwnerNum: 1));

        result.PendingChoice.Should().NotBeNull();
        var pending = result.PendingChoice!;
        pending.ChooserPlayerNum.Should().Be(1);
        pending.OwnerPlayerNum.Should().Be(1);
        pending.ReactiveCardId.Should().Be("NT-0023");
        pending.ReactiveInstanceId.Should().Be("react_1");
        pending.ChoiceKind.Should().Be(ChoiceKinds.HandCard);
        pending.Candidates.Should().Equal("TK-0001");
        pending.Trigger.Should().Be(TriggerType.OnDestroy);
        pending.TargetInstanceId.Should().Be("dest_1");
    }

    [Fact]
    public void NT0023_OnDestroy_WithoutMatchingHandCard_FailsGuard()
    {
        var state = TestFactory.MakeGameState();
        var supSource = new DeployedSupport
        {
            InstanceID = "react_1",
            CardID = "NT-0023",
            FaceUp = false,
            DeployOrder = 1,
        };
        var destroyed = TestFactory.MakeResource(cardId: "TK-0001", instanceId: "dest_1");

        var handler = _registry.Get("NT-0023", TriggerType.OnDestroy)!;
        var result = handler(MakeCtx(
            state,
            playerNum: 1,
            trigger: TriggerType.OnDestroy,
            target: destroyed,
            supSource: supSource,
            eventOwnerNum: 1));

        result.GuardFailed.Should().BeTrue();
        result.PendingChoice.Should().BeNull();
    }

    // ─── NT-0024: 攻撃宣言キャンセル + 攻撃側が再ダメージ対象を選択 ──

    [Fact]
    public void NT0024_OnAttackDeclared_ChooserIsAttacker_FieldTargetCandidates()
    {
        var state = TestFactory.MakeGameState(phase: Phase.Battle);
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "TK-0001", instanceId: "p2_fe_a", faceUp: true);
        state.Player2Field.Frontend[1] = TestFactory.MakeResource(
            cardId: "TK-0001", instanceId: "p2_fe_b", faceUp: true);
        var attacker = TestFactory.MakeResource(cardId: "TK-0001", instanceId: "atk_1", faceUp: true);
        state.Player2Field.Frontend[0] = attacker;
        var supSource = new DeployedSupport
        {
            InstanceID = "react_1",
            CardID = "NT-0024",
            FaceUp = false,
            DeployOrder = 1,
        };

        var handler = _registry.Get("NT-0024", TriggerType.OnAttackDeclared)!;
        // attacker = Player2, defender = Player1 (reactive owner)
        var result = handler(MakeCtx(
            state,
            playerNum: 1,
            trigger: TriggerType.OnAttackDeclared,
            source: attacker,
            target: state.Player2Field.Frontend[1],
            supSource: supSource,
            eventOwnerNum: 2,
            eventDamage: 600));

        result.PendingChoice.Should().NotBeNull();
        var pending = result.PendingChoice!;
        // chooser は攻撃側 (= EventOwnerNum)、所有者は reactive 側プレイヤー。
        pending.ChooserPlayerNum.Should().Be(2);
        pending.OwnerPlayerNum.Should().Be(1);
        pending.ChoiceKind.Should().Be(ChoiceKinds.FieldTarget);
        // OpponentField (= reactive owner から見て Player2 側) のフロントエンドが候補。
        pending.Candidates.Should().BeEquivalentTo("atk_1", "p2_fe_b");
    }

    // ─── TK-0024: 天気使い コンピュート系を手札からデプロイ ──

    [Fact]
    public void TK0024_OnDestroy_FiltersTenkiComputeInHand()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TK-0001", ArtNo = 1 });
        // 他陣営は候補から除外されることを示す。
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_2", CardID = "TN-0001", ArtNo = 1 });
        var supSource = new DeployedSupport
        {
            InstanceID = "react_1",
            CardID = "TK-0024",
            FaceUp = false,
            DeployOrder = 1,
        };
        // ガード成立用に、破壊された天気使いコンピュートを target に置く。
        var destroyed = TestFactory.MakeResource(cardId: "TK-0001", instanceId: "dest_1");

        var handler = _registry.Get("TK-0024", TriggerType.OnDestroy)!;
        var result = handler(MakeCtx(
            state,
            playerNum: 1,
            trigger: TriggerType.OnDestroy,
            target: destroyed,
            supSource: supSource,
            eventOwnerNum: 1));

        result.PendingChoice.Should().NotBeNull();
        result.PendingChoice!.Candidates.Should().Equal("TK-0001");
    }

    // ─── ResolvePendingChoice: 再実行で選択値が反映される ──

    [Fact]
    public void Resolve_AppliesChoiceData_QueuesSlotSelect()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TK-0001", ArtNo = 1 });
        // 再実行時 Target を snapshot から復元できるため field 上に居なくても OK。
        var destroyed = TestFactory.MakeResource(cardId: "TK-0001", instanceId: "dest_1");
        state.PendingReactiveChoice = new PendingReactiveChoice
        {
            ChooserPlayerNum = 1,
            OwnerPlayerNum = 1,
            ReactiveCardId = "NT-0023",
            ReactiveInstanceId = "react_1",
            Trigger = TriggerType.OnDestroy,
            ChoiceKey = "cardId",
            ChoiceKind = ChoiceKinds.HandCard,
            Candidates = ["TK-0001"],
            EventOwnerNum = 1,
            TargetInstanceId = "dest_1",
            TargetSnapshot = destroyed,
        };

        var req = new ResolvePendingChoiceRequest { ChosenId = "TK-0001" };
        var result = ResolvePendingChoiceProcessor.Process(state, _game, 1, req, _cc, _registry);

        state.PendingReactiveChoice.Should().BeNull();
        result.StateUpdated.Should().BeTrue();
        // NT-0023 は SlotRequestHelpers.DeployFromHand を経由して slot 待ちを enqueue する。
        state.PendingSlotSelects.Should().ContainSingle(p => p.PlayerNum == 1);
        // 手札の選択カードは消費される。
        state.Player1Hand.Should().NotContain(c => c.CardID == "TK-0001");
    }

    [Fact]
    public void Resolve_WithWrongChooser_Throws()
    {
        var state = TestFactory.MakeGameState();
        state.PendingReactiveChoice = new PendingReactiveChoice
        {
            ChooserPlayerNum = 1,
            OwnerPlayerNum = 1,
            ReactiveCardId = "NT-0023",
            ReactiveInstanceId = "react_1",
            Trigger = TriggerType.OnDestroy,
            ChoiceKey = "cardId",
            ChoiceKind = ChoiceKinds.HandCard,
            Candidates = ["TK-0001"],
        };

        var req = new ResolvePendingChoiceRequest { ChosenId = "TK-0001" };
        var act = () => ResolvePendingChoiceProcessor.Process(state, _game, 2, req, _cc, _registry);

        act.Should().Throw<GameRuleException>().WithMessage("*different player*");
    }

    [Fact]
    public void Resolve_WithUnknownChoiceId_Throws()
    {
        var state = TestFactory.MakeGameState();
        state.PendingReactiveChoice = new PendingReactiveChoice
        {
            ChooserPlayerNum = 1,
            OwnerPlayerNum = 1,
            ReactiveCardId = "NT-0023",
            ReactiveInstanceId = "react_1",
            Trigger = TriggerType.OnDestroy,
            ChoiceKey = "cardId",
            ChoiceKind = ChoiceKinds.HandCard,
            Candidates = ["TK-0001"],
        };

        var req = new ResolvePendingChoiceRequest { ChosenId = "TN-0001" };
        var act = () => ResolvePendingChoiceProcessor.Process(state, _game, 1, req, _cc, _registry);

        act.Should().Throw<GameRuleException>().WithMessage("*not in candidates*");
    }

    private EffectContext MakeCtx(
        BattleGameState state,
        long playerNum,
        TriggerType trigger,
        DeployedResource? source = null,
        DeployedResource? target = null,
        DeployedSupport? supSource = null,
        long? eventOwnerNum = null,
        long? eventDamage = null) =>
        new()
        {
            State = state,
            Game = _game,
            PlayerNum = playerNum,
            Source = source,
            Target = target,
            SupSource = supSource,
            CardCache = _cc,
            Effects = _registry,
            EventOwnerNum = eventOwnerNum,
            EventDamage = eventDamage,
            Trigger = trigger,
        };
}
