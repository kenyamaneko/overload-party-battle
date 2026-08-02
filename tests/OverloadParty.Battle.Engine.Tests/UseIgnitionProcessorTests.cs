using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class UseIgnitionProcessorTests
{
    /// <summary>Shared setup for UseIgnitionProcessor tests (card cache with a compute and platform card, and a game).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));
            _cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200"));
        }
    }

    [Trait("対象", "リソースの起動効果の使用")]
    public class ResourceEffect : Base
    {
        [Fact(DisplayName = "リソースの起動効果を使用するとハンドラが実行され使用済みフラグが立つ")]
        public void Process_ResourceEffect_ExecutesHandlerAndSetsFlag()
        {
            bool handlerCalled = false;
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, ctx =>
            {
                handlerCalled = true;
                return new EffectResult();
            });

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1", faceUp: true);
            state.Player1Field.Frontend[0] = resource;

            var req = new UseIgnitionRequest { InstanceID = "r_1" };
            UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            handlerCalled.Should().BeTrue();
            resource.EffectUsedThisTurn.Should().BeTrue();
        }

        [Fact(DisplayName = "リソースの起動効果の使用で カード ID とソース ID を載せた起動効果の使用イベントが生成される")]
        public void Process_ResourceEffect_GeneratesUseIgnitionEvent()
        {
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, _ => new EffectResult());

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1");

            var req = new UseIgnitionRequest { InstanceID = "r_1" };
            var result = UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            var evt = result.Events.Should().ContainSingle(e => e.EventType == ActionTypes.UseIgnition).Subject;
            var data = evt.EventData.Should().BeOfType<UseIgnitionEventData>().Subject;
            data.CardId.Should().Be("TST-0001");
            data.SourceId.Should().Be("r_1");
        }

        [Fact(DisplayName = "対象を指定したリソースの起動効果の使用でイベントに対象 ID が載る")]
        public void Process_ResourceEffect_EventCarriesTargetId()
        {
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, _ => new EffectResult());

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1");
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_1");

            var req = new UseIgnitionRequest { InstanceID = "r_1", TargetInstanceID = "opp_1" };
            var result = UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            var evt = result.Events.First(e => e.EventType == ActionTypes.UseIgnition);
            evt.EventData.Should().BeOfType<UseIgnitionEventData>()
                .Which.TargetId.Should().Be("opp_1");
        }

        [Fact(DisplayName = "起動効果が登録されていないリソースを起動すると例外になる")]
        public void Process_NoUseIgnition_Throws()
        {
            var reg = new EffectRegistry(); // nothing registered

            var state = TestFactory.MakeGameState(turn: 2);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1");

            var req = new UseIgnitionRequest { InstanceID = "r_1" };
            var act = () => UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            act.Should().Throw<GameRuleException>().WithMessage("*no ignition effect*");
        }

        [Fact(DisplayName = "休止状態のリソースの起動効果を使用すると例外になる")]
        public void Process_DormantResource_Throws()
        {
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, _ => new EffectResult());

            var state = TestFactory.MakeGameState(turn: 2);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1", faceUp: true);
            resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = BuffTypes.Dormant });
            state.Player1Field.Frontend[0] = resource;

            var req = new UseIgnitionRequest { InstanceID = "r_1" };
            var act = () => UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            act.Should().Throw<GameRuleException>().WithMessage("*dormant*");
        }

        [Fact(DisplayName = "このターン既に使用済みのリソースの起動効果を再使用すると例外になる")]
        public void Process_EffectAlreadyUsedThisTurn_Throws()
        {
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, _ => new EffectResult());

            var state = TestFactory.MakeGameState(turn: 2);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1");
            resource.EffectUsedThisTurn = true;
            state.Player1Field.Frontend[0] = resource;

            var req = new UseIgnitionRequest { InstanceID = "r_1" };
            var act = () => UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            act.Should().Throw<GameRuleException>().WithMessage("*already used*");
        }

        [Fact(DisplayName = "存在しないインスタンスを指定して起動効果を使用すると例外になる")]
        public void Process_ResourceNotFound_Throws()
        {
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, _ => new EffectResult());

            var state = TestFactory.MakeGameState(turn: 2);
            // Nothing on the field

            var req = new UseIgnitionRequest { InstanceID = "nonexistent" };
            var act = () => UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            act.Should().Throw<GameRuleException>().WithMessage("*not found*");
        }
    }

    [Trait("対象", "対象を伴うリソースの起動効果の使用")]
    public class ResourceEffectWithTarget : Base
    {
        [Fact(DisplayName = "自分のフィールドのリソースを対象に指定するとハンドラへ対象が渡る")]
        public void Process_WithTargetOnOwnField_PassesTargetToHandler()
        {
            DeployedResource? capturedTarget = null;
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, ctx =>
            {
                capturedTarget = ctx.Target;
                return new EffectResult();
            });

            var state = TestFactory.MakeGameState(turn: 2);
            var source = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1");
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_2");
            state.Player1Field.Frontend[0] = source;
            state.Player1Field.Frontend[1] = target;

            var req = new UseIgnitionRequest { InstanceID = "r_1", TargetInstanceID = "r_2" };
            UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            capturedTarget.Should().NotBeNull();
            capturedTarget!.InstanceID.Should().Be("r_2");
        }

        [Fact(DisplayName = "相手のフィールドのリソースを対象に指定するとハンドラへ対象が渡る")]
        public void Process_WithTargetOnOpponentField_PassesTargetToHandler()
        {
            DeployedResource? capturedTarget = null;
            var reg = new EffectRegistry();
            reg.Register("TST-0001", TriggerType.Ignition, ctx =>
            {
                capturedTarget = ctx.Target;
                return new EffectResult();
            });

            var state = TestFactory.MakeGameState(turn: 2);
            var source = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "r_1");
            var oppTarget = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_r");
            state.Player1Field.Frontend[0] = source;
            state.Player2Field.Frontend[0] = oppTarget;

            var req = new UseIgnitionRequest { InstanceID = "r_1", TargetInstanceID = "opp_r" };
            UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            capturedTarget!.InstanceID.Should().Be("opp_r");
        }
    }

    [Trait("対象", "サポートカードの起動効果の使用")]
    public class SupportEffect : Base
    {
        [Fact(DisplayName = "サポートカードの起動効果を使用するとハンドラが実行され使用済みフラグが立つ")]
        public void Process_SupportEffect_ExecutesHandlerAndSetsFlag()
        {
            bool handlerCalled = false;
            var reg = new EffectRegistry();
            reg.Register("TEST-0200", TriggerType.Ignition, ctx =>
            {
                handlerCalled = true;
                return new EffectResult();
            });

            var state = TestFactory.MakeGameState(turn: 2);
            var support = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TEST-0200",
                FaceUp = true,
            };
            state.Player1Field.Support[0] = support;

            var req = new UseIgnitionRequest { InstanceID = "sup_1" };
            UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            handlerCalled.Should().BeTrue();
            support.EffectUsedThisTurn.Should().BeTrue();
        }

        [Fact(DisplayName = "起動効果が登録されていないサポートカードを起動すると例外になる")]
        public void Process_SupportNoUseIgnition_Throws()
        {
            var reg = new EffectRegistry(); // 200 not registered

            var state = TestFactory.MakeGameState(turn: 2);
            state.Player1Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TEST-0200",
                FaceUp = true,
            };

            var req = new UseIgnitionRequest { InstanceID = "sup_1" };
            var act = () => UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            act.Should().Throw<GameRuleException>().WithMessage("*no ignition effect*");
        }

        [Fact(DisplayName = "サポートカードの起動効果の使用で カード ID とソース ID を載せた起動効果の使用イベントが生成される")]
        public void Process_SupportEffect_GeneratesUseIgnitionEvent()
        {
            var reg = new EffectRegistry();
            reg.Register("TEST-0200", TriggerType.Ignition, _ => new EffectResult());

            var state = TestFactory.MakeGameState(turn: 2);
            state.Player1Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TEST-0200",
                FaceUp = true,
            };

            var req = new UseIgnitionRequest { InstanceID = "sup_1" };
            var result = UseIgnitionProcessor.Process(state, _game, 1, req, _cc, reg);

            var evt = result.Events.Should().ContainSingle(e => e.EventType == ActionTypes.UseIgnition).Subject;
            var data = evt.EventData.Should().BeOfType<UseIgnitionEventData>().Subject;
            data.CardId.Should().Be("TEST-0200");
            data.SourceId.Should().Be("sup_1");
        }
    }

    [Trait("対象", "起動効果の発動条件")]
    public class IgnitionGuard
    {
        private const string ResourceCardId = "TST-0400";
        private const string SupportCardId = "TST-0401";
        private const long RequiredBudget = 500;
        private const long GainedBudget = 400;

        /// <summary>起動効果を持つリソースとサポートのカード定義を収めたキャッシュを作る。</summary>
        /// <returns>カード定義キャッシュ。</returns>
        private static TestCardCache MakeCardCache()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: ResourceCardId, deployTurns: 0));
            cc.Add(TestFactory.PlatformCard(cardId: SupportCardId));
            return cc;
        }

        /// <summary>バジェットの下限を発動条件とし、成立時にバジェットを得る起動効果を登録する。</summary>
        /// <param name="cardId">効果を登録するカード ID。</param>
        /// <returns>効果レジストリ。</returns>
        private static EffectRegistry MakeRegistry(string cardId)
        {
            var registry = new EffectRegistry();
            registry.Register(cardId, TriggerType.Ignition, EffectComposer.Compose(new BuiltBlock
            {
                Guards = [new MinBudgetGuard(RequiredBudget)],
                Ops = [new GainBudgetOp(PlayerRef.Myself, new StaticAmount(GainedBudget))],
            }));
            return registry;
        }

        /// <summary>起動効果を持つリソースをフロントエンドに置いた状態を作る。</summary>
        /// <param name="budget">効果所有者の初期バジェット。</param>
        /// <returns>テスト用ゲーム状態。</returns>
        private static BattleGameState MakeStateWithResource(long budget)
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, p1Budget: budget);
            state.Player1Field.Frontend[0] =
                TestFactory.MakeResource(cardId: ResourceCardId, instanceId: "r_1", faceUp: true);
            return state;
        }

        /// <summary>起動効果を持つサポートカードをサポートゾーンに置いた状態を作る。</summary>
        /// <param name="budget">効果所有者の初期バジェット。</param>
        /// <returns>テスト用ゲーム状態。</returns>
        private static BattleGameState MakeStateWithSupport(long budget)
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main, p1Budget: budget);
            state.Player1Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = SupportCardId,
                FaceUp = true,
            };
            return state;
        }

        [Fact(DisplayName = "バジェットが発動条件の下限に届かないリソースの起動効果を使用すると、ルール違反として拒否されバジェットが変わらない")]
        public void Process_ResourceGuardUnsatisfied_IsRejected()
        {
            var state = MakeStateWithResource(budget: 100);

            var act = () => UseIgnitionProcessor.Process(
                state, TestFactory.MakeGame(), 1,
                new UseIgnitionRequest { InstanceID = "r_1" }, MakeCardCache(), MakeRegistry(ResourceCardId));

            act.Should().Throw<GameRuleException>().WithMessage("*guard failed*");
            state.Player1Budget.Should().Be(100);
        }

        [Fact(DisplayName = "バジェットが発動条件の下限に届かないサポートカードの起動効果を使用すると、ルール違反として拒否されバジェットが変わらない")]
        public void Process_SupportGuardUnsatisfied_IsRejected()
        {
            var state = MakeStateWithSupport(budget: 100);

            var act = () => UseIgnitionProcessor.Process(
                state, TestFactory.MakeGame(), 1,
                new UseIgnitionRequest { InstanceID = "sup_1" }, MakeCardCache(), MakeRegistry(SupportCardId));

            act.Should().Throw<GameRuleException>().WithMessage("*guard failed*");
            state.Player1Budget.Should().Be(100);
        }

        [Fact(DisplayName = "バジェットが発動条件の下限を満たすリソースの起動効果を使用すると、効果が適用されバジェットが増える")]
        public void Process_ResourceGuardSatisfied_AppliesEffect()
        {
            var state = MakeStateWithResource(budget: 1000);

            UseIgnitionProcessor.Process(
                state, TestFactory.MakeGame(), 1,
                new UseIgnitionRequest { InstanceID = "r_1" }, MakeCardCache(), MakeRegistry(ResourceCardId));

            state.Player1Budget.Should().Be(1400);
        }
    }

    [Trait("対象", "不発だった起動効果の再使用")]
    public class MisfiredIgnitionReuse
    {
        private const string ResourceCardId = "TST-0410";
        private const string DeployedFromHandCardId = "TST-0411";
        private const string FillerCardId = "TST-0412";

        /// <summary>手札からのデプロイを要求する起動効果を登録したキャッシュとレジストリを作る。</summary>
        /// <returns>カード定義キャッシュと効果レジストリ。</returns>
        private static (TestCardCache Cc, EffectRegistry Registry) MakeEnv()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: ResourceCardId, deployTurns: 0));
            cc.Add(TestFactory.DataCard(cardId: DeployedFromHandCardId));
            cc.Add(TestFactory.DataCard(cardId: FillerCardId));

            var registry = new EffectRegistry();
            registry.Register(
                ResourceCardId, TriggerType.Ignition, EffectComposer.Compose(new RequestSlotFromHandOp()));
            return (cc, registry);
        }

        /// <summary>バックエンドを埋め、手札にデプロイ対象を 1 枚持たせた状態を作る。</summary>
        /// <returns>テスト用ゲーム状態。</returns>
        private static BattleGameState MakeStateWithFullBackend()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Frontend[0] =
                TestFactory.MakeResource(cardId: ResourceCardId, instanceId: "r_1", faceUp: true);
            for (int i = 0; i < state.Player1Field.Backend.Capacity; i++)
            {
                state.Player1Field.Backend[i] =
                    TestFactory.MakeResource(cardId: FillerCardId, instanceId: $"f_{i}", faceUp: true);
            }
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = DeployedFromHandCardId });
            return state;
        }

        /// <summary>手札のカードをデプロイさせる起動効果の使用リクエストを作る。</summary>
        /// <returns>起動効果の使用リクエスト。</returns>
        private static UseIgnitionRequest IgniteRequest() =>
            new()
            {
                InstanceID = "r_1",
                ChoiceData = new Dictionary<string, object> { ["cardId"] = DeployedFromHandCardId },
            };

        [Fact(DisplayName = "配置先が無く不発になった起動効果は、配置先が空けば同じターンにもう一度使用できる")]
        public void Process_MisfiredIgnition_CanBeUsedAgainInSameTurn()
        {
            var (cc, registry) = MakeEnv();
            var state = MakeStateWithFullBackend();

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, IgniteRequest(), cc, registry);

            state.PendingSlotSelects.Should().BeEmpty();

            state.Player1Field.Backend[2] = null;
            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1, IgniteRequest(), cc, registry);

            state.PendingSlotSelects.Should().ContainSingle()
                .Which.Resource.CardID.Should().Be(DeployedFromHandCardId);
            state.Player1Hand.Should().BeEmpty();
        }
    }

    [Trait("対象", "フィールドのカードによる選択付き起動効果")]
    public class ChoiceFromFieldCard
    {
        private const string ResourceCardId = "TST-0420";
        private const string SupportCardId = "TST-0421";
        private const string DeckCardId = "TST-0422";
        private const int PeekCount = 2;

        /// <summary>デッキ上端から 1 枚選ばせる起動効果をリソースとサポートの双方に登録する。</summary>
        /// <returns>カード定義キャッシュと効果レジストリ。</returns>
        private static (TestCardCache Cc, EffectRegistry Registry) MakeEnv()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: ResourceCardId, deployTurns: 0));
            cc.Add(TestFactory.PlatformCard(cardId: SupportCardId));
            cc.Add(TestFactory.DataCard(cardId: DeckCardId));

            var handler = MakeKeepOneHandler();
            var registry = new EffectRegistry();
            registry.Register(ResourceCardId, TriggerType.Ignition, handler);
            registry.Register(SupportCardId, TriggerType.Ignition, handler);
            return (cc, registry);
        }

        /// <summary>デッキ上端を提示して 1 枚選ばせる効果ハンドラを組み立てる。</summary>
        /// <returns>効果ハンドラ。</returns>
        private static EffectHandler MakeKeepOneHandler()
        {
            var meta = new Dictionary<string, JsonElement>
            {
                ["peek"] = JsonSerializer.SerializeToElement(PeekCount),
            };
            var action = new CustomEffectRegistry().Build(CustomEffects.KeepOneFromDeckTop, meta)
                ?? throw new InvalidOperationException("failed to build keep_one_from_deck_top");
            return EffectComposer.Compose(new CustomFnOp(action));
        }

        /// <summary>デッキ上端に 3 枚積んだ状態を作る。</summary>
        /// <returns>テスト用ゲーム状態。</returns>
        private static BattleGameState MakeStateWithDeck()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "d_1", CardID = DeckCardId });
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "d_2", CardID = DeckCardId });
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "d_3", CardID = DeckCardId });
            return state;
        }

        [Fact(DisplayName = "選択を伴う起動効果をフィールドのリソースから使用すると、そのリソースを効果の主体とした選択待ちに遷移する")]
        public void Process_ResourceChoiceEffect_SuspendsForChoice()
        {
            var (cc, registry) = MakeEnv();
            var state = MakeStateWithDeck();
            state.Player1Field.Frontend[0] =
                TestFactory.MakeResource(cardId: ResourceCardId, instanceId: "r_1", faceUp: true);

            UseIgnitionProcessor.Process(
                state, TestFactory.MakeGame(), 1, new UseIgnitionRequest { InstanceID = "r_1" }, cc, registry);

            var pending = state.PendingEffectChoice.Should().NotBeNull().And.Subject.As<PendingEffectChoice>();
            pending.Candidates.Should().Equal("d_1", "d_2");
            pending.EffectCardId.Should().Be(ResourceCardId);
            pending.EffectInstanceId.Should().Be("r_1");
            pending.Trigger.Should().Be(TriggerType.Ignition);
        }

        [Fact(DisplayName = "選択を伴う起動効果をサポートカードから使用すると、そのサポートカードを効果の主体とした選択待ちに遷移する")]
        public void Process_SupportChoiceEffect_SuspendsForChoice()
        {
            var (cc, registry) = MakeEnv();
            var state = MakeStateWithDeck();
            state.Player1Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = SupportCardId,
                FaceUp = true,
            };

            UseIgnitionProcessor.Process(
                state, TestFactory.MakeGame(), 1, new UseIgnitionRequest { InstanceID = "sup_1" }, cc, registry);

            var pending = state.PendingEffectChoice.Should().NotBeNull().And.Subject.As<PendingEffectChoice>();
            pending.Candidates.Should().Equal("d_1", "d_2");
            pending.EffectCardId.Should().Be(SupportCardId);
            pending.EffectInstanceId.Should().Be("sup_1");
            pending.Trigger.Should().Be(TriggerType.Ignition);
        }

        [Fact(DisplayName = "フィールドのリソースが積んだ選択を解決すると、選んだカードが手札へ移り残りはトラッシュへ移る")]
        public void ResolvePendingChoice_AppliesEffectToChosenCard()
        {
            var (cc, registry) = MakeEnv();
            var state = MakeStateWithDeck();
            state.Player1Field.Frontend[0] =
                TestFactory.MakeResource(cardId: ResourceCardId, instanceId: "r_1", faceUp: true);
            UseIgnitionProcessor.Process(
                state, TestFactory.MakeGame(), 1, new UseIgnitionRequest { InstanceID = "r_1" }, cc, registry);

            ResolvePendingChoiceProcessor.Process(
                state, TestFactory.MakeGame(), 1,
                new ResolvePendingChoiceRequest { ChosenId = "d_2" }, cc, registry, new FakeClock());

            state.PendingEffectChoice.Should().BeNull();
            state.Player1Hand.Select(c => c.InstanceID).Should().Equal("d_2");
            state.Player1Trash.Select(c => c.InstanceID).Should().Equal("d_1");
            state.Player1Repository.Select(c => c.InstanceID).Should().Equal("d_3");
        }
    }
}
