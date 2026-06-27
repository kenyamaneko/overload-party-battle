using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class BuffTypeTests
{
    private const string IncidentCardId = "TST-0500";
    private const long IncidentDamage = 500;

    /// <summary>インシデントを手札からプレイし、被弾した相手リソースに残るダメージを返す。</summary>
    /// <param name="buffs">被弾するリソースに付与する一時効果。</param>
    /// <returns>インシデント解決後のダメージ量。</returns>
    private static long PlayIncidentAgainst(params TemporaryEffect[] buffs)
    {
        var cc = new TestCardCache();
        cc.Add(new CardDefinition
        {
            CardId = IncidentCardId,
            CardName = "TestIncident",
            CardType = CardTypes.Incident,
            DeployTurns = 0,
        });
        cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

        var effects = new EffectRegistry();
        effects.RegisterComposed(IncidentCardId, TriggerType.Ignition,
            new IncidentDamageOp(new AllOpponentSelector(), new StaticAmount(IncidentDamage)));

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        var victim = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "victim", faceUp: true, damage: 0);
        victim.TemporaryEffects.AddRange(buffs);
        state.Player2Field.Frontend[0] = victim;
        state.Player1Hand = [new UndeployedCard { InstanceID = "h_inc", CardID = IncidentCardId }];

        PlayCardProcessor.Process(state, TestFactory.MakeGame(), 1,
            new PlayCardRequest { CardInstanceID = "h_inc" }, cc, effects);

        return victim.Damage;
    }

    /// <summary>incident_immune の一時効果を作る。</summary>
    /// <returns>インシデント免疫の一時効果。</returns>
    private static TemporaryEffect Immune() =>
        new() { EffectType = BuffTypes.IncidentImmune };

    /// <summary>定額のインシデント軽減の一時効果を作る。</summary>
    /// <param name="value">軽減する固定量。</param>
    /// <returns>定額軽減の一時効果。</returns>
    private static TemporaryEffect FlatReduction(long value) =>
        new() { EffectType = BuffTypes.IncidentReduction, Value = value };

    /// <summary>割合のインシデント軽減の一時効果を作る。</summary>
    /// <param name="percent">軽減する割合 (パーセント)。</param>
    /// <returns>割合軽減の一時効果。</returns>
    private static TemporaryEffect PercentReduction(long percent) =>
        new() { EffectType = BuffTypes.IncidentReduction, Value = percent, Mode = BuffModes.Percent };

    /// <summary>incident_immune を持つリソースはインシデントのダメージを完全に免れることを検証する。</summary>
    public class IncidentImmune
    {
        [Fact]
        public void Incident_ImmuneResource_TakesNoDamage()
        {
            PlayIncidentAgainst(Immune()).Should().Be(0);
        }
    }

    /// <summary>incident_reduction がインシデントのダメージを軽減することを検証する (定額・割合)。</summary>
    public class IncidentReduction
    {
        [Theory]
        [InlineData(200, 300)]
        [InlineData(800, 0)]
        public void Incident_FlatReduction_SubtractsThenClampsAtZero(long reduction, long expected)
        {
            PlayIncidentAgainst(FlatReduction(reduction)).Should().Be(expected);
        }

        [Fact]
        public void Incident_PercentReduction_ScalesDamage()
        {
            PlayIncidentAgainst(PercentReduction(50)).Should().Be(250);
        }

        [Fact]
        public void Incident_FlatThenPercent_AppliedInOrder()
        {
            PlayIncidentAgainst(FlatReduction(100), PercentReduction(50)).Should().Be(200);
        }
    }

    /// <summary>Shared setup for buff-type effect tests (card cache and game).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, slaPenalty: 400));
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-ATK", tp: 600, av: 1400, slaPenalty: 400, name: "Attacker"));
        }
    }

    /// <summary>Tests for the attack_damage_reduction buff reducing combat damage.</summary>
    public class AttackDamageReduction : Base
    {
        [Fact]
        public void AttackDamage_ReducedByAttackDamageReduction()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(
                cardId: "TST-ATK", instanceId: "atk_1", faceUp: true, maxTP: 600, currentTP: 600);
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            defender.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = "attack_damage_reduction",
                Value = 200,
                Duration = "until_end_of_turn",
            });
            state.Player2Field.Frontend[0] = defender;

            var req = new AttackRequest { AttackerInstanceID = "atk_1", TargetInstanceID = "def_1" };
            AttackProcessor.Process(state, _game, 1, req, _cc, new EffectRegistry());

            defender.Damage.Should().Be(400, "600 TP - 200 reduction = 400 damage");
        }

        [Fact]
        public void AttackDamage_ClampedToZero_WhenReductionExceedsTP()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);

            var attacker = TestFactory.MakeResource(
                cardId: "TST-ATK", instanceId: "atk_1", faceUp: true, maxTP: 600, currentTP: 600);
            state.Player1Field.Frontend[0] = attacker;

            var defender = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "def_1", faceUp: true);
            defender.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = "attack_damage_reduction",
                Value = 1000,
                Duration = "until_end_of_turn",
            });
            state.Player2Field.Frontend[0] = defender;

            var req = new AttackRequest { AttackerInstanceID = "atk_1", TargetInstanceID = "def_1" };
            AttackProcessor.Process(state, _game, 1, req, _cc, new EffectRegistry());

            defender.Damage.Should().Be(0, "reduction exceeds TP so damage is clamped to 0");
        }
    }

    /// <summary>Tests for the sla_penalty_reduction buff reducing budget loss on destroy.</summary>
    public class SLAPenaltyReduction : Base
    {
        [Fact]
        public void SLAPenalty_ReducedBySLAPenaltyReduction()
        {
            var state = TestFactory.MakeGameState(p1Budget: 5000);

            var resource = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "r1", faceUp: true);
            resource.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = "sla_penalty_reduction",
                Value = 100,
                Duration = "until_end_of_turn",
            });
            state.Player1Field.Frontend[0] = resource;

            ResourceHelpers.DestroyResource(state, 1, state.Player1Field, resource, _cc);

            // Card SLAPenalty=400, reduction=100 → effective penalty=300
            state.Player1Budget.Should().Be(4700, "5000 - (400 - 100) = 4700");
        }

        [Fact]
        public void SLAPenalty_ClampedToZero_WhenReductionExceedsPenalty()
        {
            var state = TestFactory.MakeGameState(p1Budget: 5000);

            var resource = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "r1", faceUp: true);
            resource.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = "sla_penalty_reduction",
                Value = 500,
                Duration = "until_end_of_turn",
            });
            state.Player1Field.Frontend[0] = resource;

            ResourceHelpers.DestroyResource(state, 1, state.Player1Field, resource, _cc);

            state.Player1Budget.Should().Be(5000, "reduction exceeds penalty so no budget loss");
        }
    }
}
