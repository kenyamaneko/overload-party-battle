using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// 効果が選択待ちで suspend / resume するフローを、ダミー reactive ハンドラで検証する。
/// 実カードの挙動は別レイヤー (cards_gen.json + EffectInitTests) で担保する。
/// </summary>
public class PendingEffectChoiceTests
{
    private const string HandDummyCardId = "TST-0001";
    private const string ReactiveDummyCardId = "TST-0002";

    private readonly TestCardCache _cc = new();
    private readonly TestEffectRegistry _registry = new();
    private readonly Game _game = TestFactory.MakeGame();

    public PendingEffectChoiceTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: HandDummyCardId));
        _cc.Add(TestFactory.ReactiveCard(cardId: ReactiveDummyCardId));
    }

    // ─── HandCard 選択: 候補有り → suspend ──

    [Fact]
    public void DeployFromHandOp_ViaReactive_WithCandidates_SuspendsForHandChoice()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = HandDummyCardId, ArtNo = 1 });
        var supSource = MakeReactiveSupSource();
        var destroyed = TestFactory.MakeResource(cardId: HandDummyCardId, instanceId: "dest_1");

        var handler = EffectComposer.Compose(new RequestSlotFromHandOp());
        var result = handler(MakeCtx(state, target: destroyed, supSource: supSource));

        result.PendingChoice.Should().NotBeNull();
        var pending = result.PendingChoice!;
        pending.ChooserPlayerNum.Should().Be(1);
        pending.OwnerPlayerNum.Should().Be(1);
        pending.EffectCardId.Should().Be(ReactiveDummyCardId);
        pending.EffectInstanceId.Should().Be("sup_1");
        pending.ChoiceKind.Should().Be(ChoiceKinds.HandCard);
        pending.Candidates.Should().Equal(HandDummyCardId);
        pending.Trigger.Should().Be(TriggerType.OnDestroy);
        pending.Target.Should().BeSameAs(destroyed);
    }

    // ─── HandCard 選択: 候補無し → guard fail ──

    [Fact]
    public void DeployFromHandOp_ViaReactive_WithoutCandidates_FailsGuard()
    {
        var state = TestFactory.MakeGameState();
        var supSource = MakeReactiveSupSource();
        var destroyed = TestFactory.MakeResource(cardId: HandDummyCardId, instanceId: "dest_1");

        var handler = EffectComposer.Compose(new RequestSlotFromHandOp());
        var result = handler(MakeCtx(state, target: destroyed, supSource: supSource));

        result.HasGuardFailed.Should().BeTrue();
        result.PendingChoice.Should().BeNull();
    }

    // ─── 直接呼び出し (SupSource 無し) は suspend せず例外 ──

    [Fact]
    public void DeployFromHandOp_DirectCall_WithoutChoice_Throws()
    {
        var state = TestFactory.MakeGameState();
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = HandDummyCardId, ArtNo = 1 });

        var handler = EffectComposer.Compose(new RequestSlotFromHandOp());
        // SupSource なし + ChoiceData なし は active 経路の不正リクエスト (A-1)、例外を伝搬する。
        var act = () => handler(MakeCtx(state));

        act.Should().Throw<GameRuleException>();
    }

    // ─── FieldTarget 選択: chooser が所有者と異なるケース ──

    [Fact]
    public void Suspend_WithFieldTargetKind_AllowsForeignChooser()
    {
        var state = TestFactory.MakeGameState(phase: Phase.Battle);
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(
            cardId: HandDummyCardId, instanceId: "p2_fe_a", faceUp: true);
        state.Player2Field.Frontend[1] = TestFactory.MakeResource(
            cardId: HandDummyCardId, instanceId: "p2_fe_b", faceUp: true);

        var supSource = MakeReactiveSupSource();
        // 攻撃側 (Player2) が再ダメージ先を選ぶ想定のダミー custom handler。
        var handler = EffectComposer.Compose(new InlineOp(octx =>
        {
            var candidates = octx.OpponentField.Frontend
                .ToArray()
                .Where(r => r is not null)
                .Select(r => r!.InstanceID)
                .ToList();
            octx.SuspendForChoice(
                "instanceId", ChoiceKinds.FieldTarget, candidates, octx.EventOwnerNum!.Value);
        }));

        var result = handler(MakeCtx(state, supSource: supSource, eventOwnerNum: 2));

        result.PendingChoice.Should().NotBeNull();
        var pending = result.PendingChoice!;
        pending.ChooserPlayerNum.Should().Be(2);
        pending.OwnerPlayerNum.Should().Be(1);
        pending.ChoiceKind.Should().Be(ChoiceKinds.FieldTarget);
        pending.Candidates.Should().BeEquivalentTo("p2_fe_a", "p2_fe_b");
    }

    // ─── Resolve: ChoiceData が wired されて handler が完走する ──

    [Fact]
    public void Resolve_AppliesChoiceData_HandlerCompletes()
    {
        var state = TestFactory.MakeGameState();
        string? capturedChoice = null;
        var handler = EffectComposer.Compose(new InlineOp(octx =>
        {
            capturedChoice = octx.ChoiceData?.GetValueOrDefault("cardId")?.ToString();
        }));
        _registry.Register(ReactiveDummyCardId, TriggerType.OnDestroy, handler);

        state.PendingEffectChoice = new PendingEffectChoice
        {
            ChooserPlayerNum = 1,
            OwnerPlayerNum = 1,
            EffectCardId = ReactiveDummyCardId,
            EffectInstanceId = "sup_1",
            Trigger = TriggerType.OnDestroy,
            ChoiceKey = "cardId",
            ChoiceKind = ChoiceKinds.HandCard,
            Candidates = [HandDummyCardId],
        };

        var req = new ResolvePendingChoiceRequest { ChosenId = HandDummyCardId };
        var result = ResolvePendingChoiceProcessor.Process(state, _game, 1, req, _cc, _registry);

        state.PendingEffectChoice.Should().BeNull();
        capturedChoice.Should().Be(HandDummyCardId);
    }

    // ─── Resolve: pending に保存した Source / Target を ctx に復元する ──

    [Fact]
    public void Resolve_RestoresSourceAndTarget()
    {
        var state = TestFactory.MakeGameState();
        var source = TestFactory.MakeResource(cardId: HandDummyCardId, instanceId: "src_1");
        var target = TestFactory.MakeResource(cardId: HandDummyCardId, instanceId: "tgt_1");

        DeployedResource? capturedSource = null;
        DeployedResource? capturedTarget = null;
        var handler = EffectComposer.Compose(new InlineOp(octx =>
        {
            capturedSource = octx.Source;
            capturedTarget = octx.Target;
        }));
        _registry.Register(ReactiveDummyCardId, TriggerType.OnDestroy, handler);

        state.PendingEffectChoice = new PendingEffectChoice
        {
            ChooserPlayerNum = 1,
            OwnerPlayerNum = 1,
            EffectCardId = ReactiveDummyCardId,
            EffectInstanceId = "sup_1",
            Trigger = TriggerType.OnDestroy,
            ChoiceKey = "cardId",
            ChoiceKind = ChoiceKinds.HandCard,
            Candidates = [HandDummyCardId],
            Source = source,
            Target = target,
        };

        var req = new ResolvePendingChoiceRequest { ChosenId = HandDummyCardId };
        ResolvePendingChoiceProcessor.Process(state, _game, 1, req, _cc, _registry);

        capturedSource.Should().BeSameAs(source);
        capturedTarget.Should().BeSameAs(target);
    }

    [Fact]
    public void Resolve_WithWrongChooser_Throws()
    {
        var state = TestFactory.MakeGameState();
        state.PendingEffectChoice = MakeHandCardPending();

        var req = new ResolvePendingChoiceRequest { ChosenId = HandDummyCardId };
        var act = () => ResolvePendingChoiceProcessor.Process(state, _game, 2, req, _cc, _registry);

        act.Should().Throw<GameRuleException>().WithMessage("*different player*");
    }

    [Fact]
    public void Resolve_WithUnknownChoiceId_Throws()
    {
        var state = TestFactory.MakeGameState();
        state.PendingEffectChoice = MakeHandCardPending();

        var req = new ResolvePendingChoiceRequest { ChosenId = "TST-9999" };
        var act = () => ResolvePendingChoiceProcessor.Process(state, _game, 1, req, _cc, _registry);

        act.Should().Throw<GameRuleException>().WithMessage("*not in candidates*");
    }

    // ─── helpers ────────────────────────────────────────────

    private static DeployedSupport MakeReactiveSupSource() => new()
    {
        InstanceID = "sup_1",
        CardID = ReactiveDummyCardId,
        FaceUp = false,
        DeployOrder = 1,
    };

    private PendingEffectChoice MakeHandCardPending() => new()
    {
        ChooserPlayerNum = 1,
        OwnerPlayerNum = 1,
        EffectCardId = ReactiveDummyCardId,
        EffectInstanceId = "sup_1",
        Trigger = TriggerType.OnDestroy,
        ChoiceKey = "cardId",
        ChoiceKind = ChoiceKinds.HandCard,
        Candidates = [HandDummyCardId],
    };

    private EffectContext MakeCtx(
        BattleGameState state,
        long playerNum = 1,
        DeployedResource? source = null,
        DeployedResource? target = null,
        DeployedSupport? supSource = null,
        long? eventOwnerNum = null) =>
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
            Trigger = TriggerType.OnDestroy,
        };

    /// <summary>テスト専用のインライン op (任意の Action を実行する)。</summary>
    private class InlineOp : IEffectOp
    {
        private readonly Action<OpContext> _action;
        public InlineOp(Action<OpContext> action) => _action = action;
        public void Execute(OpContext ctx) => _action(ctx);
    }
}
