using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class AttackProcessorTests
{
    /// <summary>AttackProcessor.Process テストの共有 setup (カードキャッシュ・ゲーム・リクエスト生成)。</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            // Compute card: TP=600, AV=1400, slaPenalty=400
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, slaPenalty: 400));
            // High-TP attacker: TP=1500
            _cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", tp: 1500, av: 1400, slaPenalty: 400, name: "StrongCompute"));
            // ObjectStorage card (data type, cannot attack)
            _cc.Add(TestFactory.DataCard(cardId: "TST-0002", subtype: "ObjectStorage", name: "TestObjStorage"));
            // Elastic compute card
            _cc.Add(TestFactory.ElasticContainerCard(cardId: "TST-0003"));
            // Platform card for reactive test
            _cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200", name: "ReactivePlatform"));
        }

        protected static AttackRequest MakeReq(string attackerId, string targetId) =>
            new() { AttackerInstanceID = attackerId, TargetInstanceID = targetId };
    }

    [Trait("対象", "攻撃のダメージ適用")]
    public class BasicAttack : Base
    {
        [Fact(DisplayName = "フロントエンドの Compute系リソースで攻撃すると、対象に 600 ダメージを与え攻撃済みになる")]
        public void DealsDamageToDefender()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            var result = AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            defender.Damage.Should().Be(600);
            attacker.HasAttacked.Should().BeTrue();
        }
    }

    [Trait("対象", "破壊時の SLA ペナルティ")]
    public class DestroysDefender : Base
    {
        [Fact(DisplayName = "可用性を超えるダメージで対象を破壊すると、盤面から除去され所有者のバジェットが SLA ペナルティ 400 だけ減る")]
        public void AppliesSlaPenalty()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "atk_1", faceUp: true, maxTP: 1500, currentTP: 1500);
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            long budgetBefore = state.Player2Budget;

            var result = AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            // Defender should be removed from field
            state.Player2Field.Frontend[0].Should().BeNull();

            // SLA penalty event data
            var attackEvent = result.Events.First(e => e.EventType == ActionTypes.Attack);
            attackEvent.EventData.Should().BeOfType<AttackEventData>()
                .Which.SlaPenalty.Should().NotBeNull();

            // Budget decreased by SLA penalty
            state.Player2Budget.Should().Be(budgetBefore - 400);
        }
    }

    [Trait("対象", "フロントエンド以外からの攻撃拒否")]
    public class AttackerNotOnFrontend : Base
    {
        [Fact(DisplayName = "バックエンドのリソースで攻撃しようとすると、GameRuleException になる")]
        public void Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Backend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            var act = () => AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*frontend*");
        }
    }

    [Trait("対象", "Compute系以外の攻撃拒否")]
    public class AttackerNotComputeType : Base
    {
        [Fact(DisplayName = "Compute系以外のリソースで攻撃しようとすると、GameRuleException になる")]
        public void Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0002", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            var act = () => AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*compute*");
        }
    }

    [Trait("対象", "攻撃済みリソースの再攻撃拒否")]
    public class AttackerAlreadyAttacked : Base
    {
        [Fact(DisplayName = "既に攻撃済みのリソースで再度攻撃しようとすると、GameRuleException になる")]
        public void Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            attacker.HasAttacked = true;
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            var act = () => AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*already attacked*");
        }
    }

    [Trait("対象", "休止リソースの攻撃拒否")]
    public class AttackerDormant : Base
    {
        [Fact(DisplayName = "休止状態のリソースで攻撃しようとすると、GameRuleException になる")]
        public void Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            attacker.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = BuffTypes.Dormant,
                Value = 1,
                Duration = "this_turn",
                SourceID = "test",
            });
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            var act = () => AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*dormant*");
        }
    }

    [Trait("対象", "攻撃不可リソースの攻撃拒否")]
    public class AttackerCannotAttack : Base
    {
        [Fact(DisplayName = "攻撃不可状態のリソースで攻撃しようとすると、GameRuleException になる")]
        public void Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            attacker.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = BuffTypes.CannotAttack,
                Value = 1,
                Duration = "this_turn",
                SourceID = "test",
            });
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            var act = () => AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*cannot_attack*");
        }
    }

    [Trait("対象", "裏向きリソースの攻撃拒否")]
    public class FaceDown : Base
    {
        [Theory(DisplayName = "攻撃者か対象のどちらかが裏向きのとき、GameRuleException になる")]
        [InlineData(false, true)]   // attacker face-down
        [InlineData(true, false)]  // defender face-down
        public void Throws(bool atkFaceUp, bool defFaceUp)
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: atkFaceUp, deployLeft: atkFaceUp ? 0 : 1);
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: defFaceUp, deployLeft: defFaceUp ? 0 : 1);
            state.Player2Field.Frontend[0] = defender;

            var act = () => AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>();
        }
    }

    [Trait("対象", "フロントエンド存在時のバックエンド攻撃拒否")]
    public class BackendTargetWithFrontend : Base
    {
        [Fact(DisplayName = "相手にフロントエンドが残っているときバックエンドを攻撃しようとすると、GameRuleException になる")]
        public void Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[0] = attacker;

            // P2 has both frontend and backend resources
            var frontRes = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "front_1", faceUp: true);
            state.Player2Field.Frontend[0] = frontRes;

            var backRes = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "back_1", faceUp: true);
            state.Player2Field.Backend[0] = backRes;

            var act = () => AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "back_1"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*backend*frontend*");
        }
    }

    [Trait("対象", "フロントエンド不在時のバックエンド貫通攻撃")]
    public class BackendTargetNoFrontend : Base
    {
        [Fact(DisplayName = "相手のフロントエンドが空のときはバックエンドを直接攻撃でき、600 ダメージを与える")]
        public void Succeeds()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[0] = attacker;

            // P2 has only backend, no frontend
            var backRes = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "back_1", faceUp: true);
            state.Player2Field.Backend[0] = backRes;

            var result = AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "back_1"), _cc, new EffectRegistry());

            backRes.Damage.Should().Be(600);
            result.Events.Should().Contain(e => e.EventType == ActionTypes.Attack);
        }
    }

    [Trait("対象", "被攻撃生存時のエラスティックボーナス")]
    public class ElasticDefender : Base
    {
        [Fact(DisplayName = "エラスティックなフロントエンドのリソースが被攻撃で生存すると、エラスティックボーナスが増える")]
        public void GainsBonus()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            // Weak attacker so defender survives
            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[0] = attacker;

            // Elastic container as defender (AV=1200, will survive 600 damage)
            var defender = TestFactory.MakeResource(
                cardId: "TST-0003", instanceId: "def_1", faceUp: true,
                maxAV: 1200, currentAV: 1200, maxTP: 500, currentTP: 500);
            state.Player2Field.Frontend[0] = defender;

            long bonusBefore = defender.ElasticBonus;

            AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            defender.ElasticBonus.Should().BeGreaterThan(bonusBefore);
        }

        // RULEBOOK §6: 被攻撃生存の Elastic 増分はフロントエンドの Compute / AI/ML 限定。
        // 貫通で攻撃されたバックエンドの Elastic Database は Yield ボーナスを得ない。
        [Fact(DisplayName = "貫通で攻撃されたバックエンドのエラスティックな Data系リソースは、生存してもエラスティックボーナスを得ない")]
        public void BackendElasticDataResource_NoBonusOnSurvive()
        {
            _cc.Add(TestFactory.DataCard(
                cardId: "TST-0006", subtype: "Database", elastic: true, elasticIncrement: 100, freeTier: 400));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[0] = attacker;

            // 相手フロントエンドは空 → バックエンドを直接攻撃可能 (貫通)
            var defender = TestFactory.MakeResource(
                cardId: "TST-0006", instanceId: "def_1", faceUp: true,
                maxAV: 1400, currentAV: 1400, maxTP: null, currentTP: null, maxYield: 400, currentYield: 400);
            state.Player2Field.Backend[0] = defender;

            AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            defender.ElasticBonus.Should().Be(0);
        }
    }

    [Trait("対象", "攻撃イベントの内容")]
    public class AttackEventContent : Base
    {
        [Fact(DisplayName = "攻撃すると、攻撃イベントに攻撃者 ID・対象 ID・ダメージ 600・破壊フラグ false が載る")]
        public void ContainsCorrectData()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            var result = AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            var attackEvent = result.Events.First(e => e.EventType == ActionTypes.Attack);
            var data = attackEvent.EventData.Should().BeOfType<AttackEventData>().Subject;
            data.AttackerId.Should().Be("atk_1");
            data.TargetId.Should().Be("def_1");
            data.Damage.Should().Be(600L);
            data.Destroyed.Should().Be(false);
        }
    }

    [Trait("対象", "存在しない攻撃者の指定拒否")]
    public class AttackerNotFound : Base
    {
        [Fact(DisplayName = "存在しないインスタンス ID を攻撃者に指定すると、GameRuleException になる")]
        public void Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            var act = () => AttackProcessor.Process(
                state, _game, 1, MakeReq("nonexistent", "def_1"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*not found*");
        }
    }

    [Trait("対象", "存在しない対象の指定拒否")]
    public class DefenderNotFound : Base
    {
        [Fact(DisplayName = "存在しないインスタンス ID を対象に指定すると、GameRuleException になる")]
        public void Throws()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[0] = attacker;

            var act = () => AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "nonexistent"), _cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*not found*");
        }
    }

    [Trait("対象", "非エラスティック防御者の被攻撃生存")]
    public class NonElasticDefender : Base
    {
        [Fact(DisplayName = "非エラスティックなリソースが被攻撃で生存しても、エラスティックボーナスは 0 のままになる")]
        public void NoElasticBonus()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            // Weak attacker so defender survives
            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[0] = attacker;

            // Non-elastic defender (AV=1400, survives 600 damage)
            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            defender.ElasticBonus.Should().Be(0);
        }
    }

    [Trait("対象", "攻撃時の誘発効果 (OnAttack)")]
    public class OnAttackEffect : Base
    {
        [Fact(DisplayName = "攻撃者の OnAttack 誘発効果が発動し、on_attack_triggered イベントが結果に加わる")]
        public void FiresAndAddsEvents()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            var effects = new TestEffectRegistry();
            bool handlerCalled = false;
            effects.Register("TST-0001", TriggerType.OnAttack, ctx =>
            {
                handlerCalled = true;
                return new EffectResult
                {
                    Events = [new GameEvent { EventType = "on_attack_triggered", GameID = ctx.Game.GameID }]
                };
            });

            var result = AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, effects);

            handlerCalled.Should().BeTrue();
            result.Events.Should().Contain(e => e.EventType == "on_attack_triggered");
        }
    }

    [Trait("対象", "リアクティブによる攻撃キャンセル")]
    public class ReactiveCancelsAttack : Base
    {
        [Fact(DisplayName = "相手の裏向きリアクティブが攻撃宣言に反応して攻撃をキャンセルすると、対象へのダメージが 0 になりリアクティブがトラッシュへ送られる")]
        public void DamageIsZero()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            _cc.Add(TestFactory.ReactiveCard(cardId: "TEST-0400"));

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            // Opponent has a face-down Reactive watching for an attack declaration
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TEST-0400",
                FaceUp = false,
                DeployOrder = 1
            };

            var effects = new TestEffectRegistry();
            effects.Register("TEST-0400", TriggerType.OnAttackDeclared, ctx => new EffectResult
            {
                ShouldCancelAction = true,
                Events = [new GameEvent { EventType = "reactive_fired", GameID = ctx.Game.GameID }]
            });

            var result = AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, effects);

            // Damage should not be applied
            defender.Damage.Should().Be(0);
            // Attacker still marked as attacked
            attacker.HasAttacked.Should().BeTrue();
            // Attack event should show cancelled=true
            var attackEvent = result.Events.First(e => e.EventType == ActionTypes.Attack);
            var data = attackEvent.EventData.Should().BeOfType<AttackEventData>().Subject;
            data.Cancelled.Should().Be(true);
            data.Damage.Should().Be(0L);
            // Reactive should be removed from support zone and sent to trash
            state.Player2Field.Support[0].Should().BeNull();
            state.Player2Trash.Should().Contain(c => c.CardID == "TEST-0400");
        }
    }

    [Trait("対象", "対象破壊時の誘発効果 (OnDestroy)")]
    public class OnDestroyEffect : Base
    {
        [Fact(DisplayName = "攻撃で対象が破壊されると、対象の OnDestroy 誘発効果が発動し on_destroy_triggered イベントが加わる")]
        public void FiresWhenDefenderDestroyed()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "atk_1", faceUp: true, maxTP: 1500, currentTP: 1500);
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            var effects = new TestEffectRegistry();
            bool destroyHandlerCalled = false;
            effects.Register("TST-0001", TriggerType.OnDestroy, ctx =>
            {
                destroyHandlerCalled = true;
                return new EffectResult
                {
                    Events = [new GameEvent { EventType = "on_destroy_triggered", GameID = ctx.Game.GameID }]
                };
            });

            var result = AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, effects);

            destroyHandlerCalled.Should().BeTrue();
            result.Events.Should().Contain(e => e.EventType == "on_destroy_triggered");
        }
    }

    [Trait("対象", "味方リソースの破壊時誘発効果 (OnDestroy)")]
    public class AlliedOnDestroy : Base
    {
        [Fact(DisplayName = "対象が破壊されると、同じ盤面の味方リソースの OnDestroy 誘発効果が発動し allied_on_destroy イベントが加わる")]
        public void FiresForOtherResources()
        {
            // Card 3 = an allied resource with OnDestroy
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0004", tp: 400, av: 1000, name: "AllyCompute"));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "atk_1", faceUp: true, maxTP: 1500, currentTP: 1500);
            state.Player1Field.Frontend[0] = attacker;

            // Defender will be destroyed
            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            // Allied resource on opponent's field
            var ally = TestFactory.MakeResource(cardId: "TST-0004", instanceId: "ally_1", faceUp: true, maxAV: 1000, currentAV: 1000, maxTP: 400, currentTP: 400);
            ally.DeployOrder = 1;
            state.Player2Field.Frontend[1] = ally;

            var effects = new TestEffectRegistry();
            bool alliedHandlerCalled = false;
            effects.Register("TST-0004", TriggerType.OnDestroy, ctx =>
            {
                alliedHandlerCalled = true;
                return new EffectResult
                {
                    Events = [new GameEvent { EventType = "allied_on_destroy", GameID = ctx.Game.GameID }]
                };
            });

            var result = AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, effects);

            alliedHandlerCalled.Should().BeTrue();
            result.Events.Should().Contain(e => e.EventType == "allied_on_destroy");
        }
    }

    [Trait("対象", "エラスティック防御者の破壊")]
    public class ElasticDefenderDestroyed : Base
    {
        [Fact(DisplayName = "エラスティックな防御者が破壊されると、盤面から除去される")]
        public void NoElasticBonus()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            // Strong attacker that will destroy the elastic defender
            var attacker = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "atk_1", faceUp: true, maxTP: 1500, currentTP: 1500);
            state.Player1Field.Frontend[0] = attacker;

            // Elastic container with AV=1200, will be destroyed by 1500 damage
            var defender = TestFactory.MakeResource(
                cardId: "TST-0003", instanceId: "def_1", faceUp: true,
                maxAV: 1200, currentAV: 1200, maxTP: 500, currentTP: 500);
            state.Player2Field.Frontend[0] = defender;

            AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            // Defender should be removed from field (destroyed)
            state.Player2Field.Frontend[0].Should().BeNull();
        }
    }

    [Trait("対象", "生存時の SLA ペナルティ")]
    public class NotDestroyed : Base
    {
        [Fact(DisplayName = "対象が破壊されずに生存すると、攻撃イベントの SLA ペナルティが 0 になる")]
        public void SlaPenaltyZero()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            var result = AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            var attackEvent = result.Events.First(e => e.EventType == ActionTypes.Attack);
            attackEvent.EventData.Should().BeOfType<AttackEventData>()
                .Which.SlaPenalty.Should().Be(0L);
        }
    }

    [Trait("対象", "リアクティブ未登録時の通常攻撃")]
    public class NullEffects : Base
    {
        [Fact(DisplayName = "リアクティブが登録されていないと、裏向きサポートがあっても攻撃はキャンセルされず 600 ダメージを与える")]
        public void NoReactiveTriggered()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[0] = defender;

            // Opponent has support but effects are null
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TEST-0200",
                FaceUp = false,
                DeployOrder = 1
            };

            var result = AttackProcessor.Process(
                state, _game, 1, MakeReq("atk_1", "def_1"), _cc, new EffectRegistry());

            // Attack should proceed normally
            defender.Damage.Should().Be(600);
        }
    }

    [Trait("対象", "プレイヤー 2 の攻撃")]
    public class Player2Attacks : Base
    {
        [Fact(DisplayName = "プレイヤー 2 が攻撃すると、プレイヤー 1 の対象に 600 ダメージを与え攻撃イベントの手番が 2 になる")]
        public void DealsDamageToPlayer1()
        {
            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle, activePlayer: 2);

            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_2", faceUp: true);
            state.Player2Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player1Field.Frontend[0] = defender;

            var result = AttackProcessor.Process(
                state, _game, 2, MakeReq("atk_2", "def_1"), _cc, new EffectRegistry());

            defender.Damage.Should().Be(600);
            attacker.HasAttacked.Should().BeTrue();
            result.Events.First(e => e.EventType == ActionTypes.Attack)
                .PlayerNum.Should().Be(2);
        }
    }

    /// <summary>標準的なコンピュート系リソース 1 種を持つカードキャッシュを作る。</summary>
    /// <returns>TST-0001 (TP=600 / AV=1400 / SLA=400) を登録したキャッシュ。</returns>
    private static TestCardCache StandardCc()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, slaPenalty: 400));
        return cc;
    }

    /// <summary>攻撃リクエストを作る。</summary>
    /// <param name="attacker">攻撃側インスタンス ID。</param>
    /// <param name="target">対象インスタンス ID。</param>
    /// <returns>攻撃リクエスト。</returns>
    private static AttackRequest Atk(string attacker, string target) =>
        new() { AttackerInstanceID = attacker, TargetInstanceID = target };

    [Trait("対象", "攻撃者の最終攻撃ターン記録")]
    public class AttackerState
    {
        [Fact(DisplayName = "ターン 5 に攻撃すると、攻撃者の最終攻撃ターンが 5 に記録される")]
        public void RecordsLastAttackTurn()
        {
            var cc = StandardCc();
            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);

            var attacker = state.Player1Field.Frontend[0]!;
            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk_1", "def_1"), cc, new EffectRegistry());

            attacker.LastAttackTurn.Should().Be(5);
        }
    }

    [Trait("対象", "被ダメージ軽減バフ")]
    public class DamageReduction
    {
        [Theory(DisplayName = "被ダメージ軽減バフの値だけ 600 のダメージが減り、0 未満にはならず 0 で下げ止まる")]
        [InlineData(200, 400)]
        [InlineData(600, 0)]
        [InlineData(800, 0)]
        public void AttackDamageReductionBuffClampsDamage(long reduction, long expectedDamage)
        {
            var cc = StandardCc();
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            defender.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = BuffTypes.AttackDamageReduction,
                Value = reduction,
            });
            state.Player2Field.Frontend[0] = defender;

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk_1", "def_1"), cc, new EffectRegistry());

            defender.Damage.Should().Be(expectedDamage);
        }
    }

    [Trait("対象", "被攻撃時の誘発効果 (OnHit)")]
    public class OnHitEffect
    {
        [Fact(DisplayName = "攻撃を受けた対象の OnHit 誘発効果が発動し、on_hit_triggered イベントが加わる")]
        public void FiresOnDefenderWhenAttacked()
        {
            var cc = StandardCc();
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);

            bool hitHandlerCalled = false;
            var effects = new TestEffectRegistry();
            effects.Register("TST-0001", TriggerType.OnHit, ctx =>
            {
                hitHandlerCalled = true;
                return new EffectResult
                {
                    Events = [new GameEvent { EventType = "on_hit_triggered", GameID = ctx.Game.GameID }]
                };
            });

            var result = AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk_1", "def_1"), cc, effects);

            hitHandlerCalled.Should().BeTrue("攻撃を受けた防御者の OnHit 誘発効果が発動する");
            result.Events.Should().Contain(e => e.EventType == "on_hit_triggered");
        }
    }

    [Trait("対象", "盤面変化時の誘発効果 (OnFieldChange)")]
    public class OnFieldChangeAfterDestroy
    {
        [Fact(DisplayName = "攻撃で対象が破壊され盤面が変化すると、OnFieldChange 誘発効果が 1 回発動する")]
        public void FiresAfterDefenderDestroyed()
        {
            var cc = StandardCc();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", tp: 1500, av: 1400, slaPenalty: 400, name: "StrongCompute"));
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0005", instanceId: "atk_1", faceUp: true, maxTP: 1500, currentTP: 1500);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);

            int fieldChangeFires = 0;
            var effects = new TestEffectRegistry();
            effects.Register("TST-0005", TriggerType.OnFieldChange, _ => { fieldChangeFires++; return new EffectResult(); });

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk_1", "def_1"), cc, effects);

            fieldChangeFires.Should().Be(1, "リソース破壊で盤面が変化し OnFieldChange が発動する");
        }
    }

    [Trait("対象", "target_shield で保護された対象の攻撃拒否")]
    public class TargetShieldProtection
    {
        [Fact(DisplayName = "target_shield で保護された対象を攻撃しようとすると、GameRuleException になる")]
        public void Process_ShieldedDefender_Throws()
        {
            var cc = StandardCc();
            var shield = TestFactory.AttachmentCard(cardId: "TST-0301");
            shield.Effects = [new EffectDef { Trigger = TriggerTypes.Passive, Custom = CustomEffects.TargetShield }];
            cc.Add(shield);

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "wall_1", faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TST-0301",
                TargetInstanceID = "def_1",
                FaceUp = true,
            };

            var act = () => AttackProcessor.Process(
                state, TestFactory.MakeGame(), 1, Atk("atk_1", "def_1"), cc, new EffectRegistry());

            act.Should().Throw<GameRuleException>().WithMessage("*target_shield*");
        }
    }

    [Trait("対象", "装備先破壊時のアタッチメント破壊")]
    public class AttachmentDestroyedWithHost
    {
        [Fact(DisplayName = "装備先のリソースが破壊されると、そのアタッチメントも破壊されトラッシュへ送られる")]
        public void DestroyingHost_AlsoDestroysItsAttachment()
        {
            var cc = StandardCc();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", tp: 1500, av: 1400, slaPenalty: 400, name: "StrongCompute"));
            cc.Add(TestFactory.AttachmentCard(cardId: "TST-0301"));

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0005", instanceId: "atk_1", faceUp: true, maxTP: 1500, currentTP: 1500);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "att_1",
                CardID = "TST-0301",
                TargetInstanceID = "def_1",
                FaceUp = true,
            };

            AttackProcessor.Process(state, TestFactory.MakeGame(), 1, Atk("atk_1", "def_1"), cc, new EffectRegistry());

            FieldHelpers.FindResourceByID(state.Player2Field, "def_1").Should().BeNull("装備先が破壊される");
            state.Player2Field.Support.Select(s => s.InstanceID).Should().NotContain("att_1", "アタッチメントも破壊される");
            state.Player2Trash.Should().Contain(c => c.InstanceID == "att_1", "破壊されたアタッチメントはトラッシュへ送られる");
        }
    }
}
