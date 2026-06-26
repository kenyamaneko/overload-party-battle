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

    /// <summary>Tests for AttackProcessor.Process — basic attack deals damage.</summary>
    public class BasicAttack : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — attack destroys defender with SLA penalty.</summary>
    public class DestroysDefender : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — attacker not on frontend.</summary>
    public class AttackerNotOnFrontend : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — attacker not compute type.</summary>
    public class AttackerNotComputeType : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — attacker already attacked.</summary>
    public class AttackerAlreadyAttacked : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — dormant attacker.</summary>
    public class AttackerDormant : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — cannot_attack attacker.</summary>
    public class AttackerCannotAttack : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — attacker or defender face-down.</summary>
    public class FaceDown : Base
    {
        [Theory]
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

    /// <summary>Tests for AttackProcessor.Process — backend target while frontend present.</summary>
    public class BackendTargetWithFrontend : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — backend target with no frontend.</summary>
    public class BackendTargetNoFrontend : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — elastic defender that survives.</summary>
    public class ElasticDefender : Base
    {
        [Fact]
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
    }

    /// <summary>Tests for AttackProcessor.Process — attack event payload contents.</summary>
    public class AttackEventContent : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — attacker not found on field.</summary>
    public class AttackerNotFound : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — defender not found on field.</summary>
    public class DefenderNotFound : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — non-elastic defender that survives.</summary>
    public class NonElasticDefender : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — OnAttack trigger on the attacker.</summary>
    public class OnAttackEffect : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — reactive cancels the attack.</summary>
    public class ReactiveCancelsAttack : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — OnDestroy trigger when defender destroyed.</summary>
    public class OnDestroyEffect : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — allied OnDestroy fires for other resources.</summary>
    public class AlliedOnDestroy : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — elastic defender that is destroyed.</summary>
    public class ElasticDefenderDestroyed : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — slaPenalty=0 when defender survives.</summary>
    public class NotDestroyed : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — no effects registry means no reactive cancel.</summary>
    public class NullEffects : Base
    {
        [Fact]
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

    /// <summary>Tests for AttackProcessor.Process — player 2 attacks player 1.</summary>
    public class Player2Attacks : Base
    {
        [Fact]
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

    /// <summary>攻撃者が最後に攻撃したターンを記録することを検証する。</summary>
    public class AttackerState
    {
        [Fact]
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

    /// <summary>attack_damage_reduction バフが適用 ダメージ を軽減することを検証する。</summary>
    public class DamageReduction
    {
        [Theory]
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

    /// <summary>攻撃を受けた防御者の OnHit 誘発効果が発動することを検証する。</summary>
    public class OnHitEffect
    {
        [Fact]
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

    /// <summary>破壊で盤面が変化した後に OnFieldChange が発動することを検証する。</summary>
    public class OnFieldChangeAfterDestroy
    {
        [Fact]
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

    /// <summary>target_shield で保護された防御者を攻撃できないことを検証する。</summary>
    public class TargetShieldProtection
    {
        [Fact]
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

    /// <summary>装備先リソースが破壊されると、そのアタッチメントも破壊されトラッシュへ送られることを検証する。</summary>
    public class AttachmentDestroyedWithHost
    {
        [Fact]
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
