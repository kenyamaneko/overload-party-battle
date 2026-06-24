using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class CustomEffectTests
{
    /// <summary>Shared setup for custom-effect tests (card cache, game, registry, and op-context builder).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();
        protected readonly CustomEffectRegistry _registry = new();

        /// <summary>Builds an op context wiring the source/target/support and choice data a custom effect reads.</summary>
        /// <param name="state">The game state to operate on.</param>
        /// <param name="playerNum">The effect owner's player number.</param>
        /// <param name="source">The effect source resource.</param>
        /// <param name="target">The effect target resource.</param>
        /// <param name="supSource">The support-zone source (attachment / reactive).</param>
        /// <param name="choiceData">Player choice data.</param>
        /// <param name="eventOwnerNum">The player who triggered the event.</param>
        /// <returns>An op context for the supplied inputs.</returns>
        protected OpContext MakeOpContext(
            BattleGameState state, long playerNum = 1,
            DeployedResource? source = null, DeployedResource? target = null,
            DeployedSupport? supSource = null,
            Dictionary<string, object>? choiceData = null,
            long? eventOwnerNum = null)
        {
            var ctx = new EffectContext
            {
                State = state,
                Game = _game,
                PlayerNum = playerNum,
                Source = source,
                Target = target,
                SupSource = supSource,
                ChoiceData = choiceData,
                EventOwnerNum = eventOwnerNum,
                CardCache = _cc,
                Effects = new EffectRegistry(),
                Trigger = TriggerType.OnDeploy,
            };
            return new OpContext(ctx);
        }

        /// <summary>Parses a custom effect's meta block from JSON.</summary>
        /// <param name="json">The meta object as JSON.</param>
        /// <returns>The deserialized meta dictionary.</returns>
        protected static Dictionary<string, JsonElement> Meta(string json) =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }

    /// <summary>Tests the chain attack bonus that adds ダメージ when a しゅがーらぼ Compute系リソース ally is present.</summary>
    public class ChainAttackBonus : Base
    {
        [Fact]
        public void AppliesBonusDamage_WhenSugarComputeAllyPresent()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "SRC"));
            _cc.Add(TestFactory.ComputeCard(cardId: "ALLY", faction: Factions.Sugar));
            var state = TestFactory.MakeGameState();
            var source = TestFactory.MakeResource(cardId: "SRC", instanceId: "src", faceUp: true);
            state.Player1Field.Frontend[0] = source;
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "ALLY", instanceId: "ally", faceUp: true);
            var target = TestFactory.MakeResource(cardId: "SRC", instanceId: "tgt", faceUp: true, maxAV: 2000, damage: 0);
            state.Player2Field.Frontend[0] = target;

            var effect = _registry.Build(CustomEffects.ChainAttackBonus, Meta("""{"damage":200}"""))!;
            effect(MakeOpContext(state, source: source, target: target));

            target.Damage.Should().Be(200);
        }

        [Fact]
        public void NoBonus_WhenNoSugarComputeAlly()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "SRC"));
            var state = TestFactory.MakeGameState();
            var source = TestFactory.MakeResource(cardId: "SRC", instanceId: "src", faceUp: true);
            state.Player1Field.Frontend[0] = source;
            var target = TestFactory.MakeResource(cardId: "SRC", instanceId: "tgt", faceUp: true, maxAV: 2000, damage: 0);
            state.Player2Field.Frontend[0] = target;

            var effect = _registry.Build(CustomEffects.ChainAttackBonus, Meta("""{"damage":200}"""))!;
            effect(MakeOpContext(state, source: source, target: target));

            target.Damage.Should().Be(0);
        }
    }

    /// <summary>Tests applying 休止 to a high-スループット Compute系リソース on deploy.</summary>
    public class DisableHighTpDeploy : Base
    {
        [Fact]
        public void AppliesDormant_WhenHighTpComputeDeployed()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "BIG"));
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(cardId: "BIG", instanceId: "big", faceUp: true, maxTP: 900);
            state.Player2Field.Frontend[0] = target;

            var effect = _registry.Build(CustomEffects.DisableHighTpDeploy, null)!;
            effect(MakeOpContext(state, playerNum: 1, target: target));

            FieldHelpers.HasTemporaryEffect(target, BuffTypes.Dormant).Should().BeTrue();
        }

        [Fact]
        public void NoDormant_WhenTpBelowThreshold()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "SMALL"));
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(cardId: "SMALL", instanceId: "small", faceUp: true, maxTP: 600);
            state.Player2Field.Frontend[0] = target;

            var effect = _registry.Build(CustomEffects.DisableHighTpDeploy, null)!;
            effect(MakeOpContext(state, playerNum: 1, target: target));

            FieldHelpers.HasTemporaryEffect(target, BuffTypes.Dormant).Should().BeFalse();
        }
    }

    /// <summary>Tests cancelling the 相手 の 3 体目のデプロイ in a turn.</summary>
    public class CancelNthDeploy : Base
    {
        private static void DeployThree(BattleGameState state, long turn)
        {
            for (int i = 0; i < 3; i++)
            {
                var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: $"d_{i}", faceUp: true);
                res.DeployedOnTurn = turn;
                state.Player2Field.Frontend[i] = res;
            }
        }

        [Fact]
        public void CancelsAndMarksUsed_OnThirdDeploy()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(turn: 4);
            DeployThree(state, 4);
            var watcher = new DeployedSupport { InstanceID = "watcher", CardID = "TST-0001", FaceUp = false };
            state.Player1Field.Support[0] = watcher;

            var effect = _registry.Build(CustomEffects.CancelNthDeploy, null)!;
            var opCtx = MakeOpContext(state, playerNum: 1, supSource: watcher);
            effect(opCtx);

            opCtx.Result.ShouldCancelAction.Should().BeTrue();
            watcher.EffectUsedThisTurn.Should().BeTrue();
        }

        [Fact]
        public void Throws_WhenNotThirdDeploy()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(turn: 4);
            var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "d_0", faceUp: true);
            res.DeployedOnTurn = 4;
            state.Player2Field.Frontend[0] = res;
            var watcher = new DeployedSupport { InstanceID = "watcher", CardID = "TST-0001", FaceUp = false };
            state.Player1Field.Support[0] = watcher;

            var effect = _registry.Build(CustomEffects.CancelNthDeploy, null)!;
            var act = () => effect(MakeOpContext(state, playerNum: 1, supSource: watcher));

            act.Should().Throw<GameRuleException>();
        }
    }

    /// <summary>Tests moving an アタッチメント to a different target resource.</summary>
    public class Reattach : Base
    {
        [Fact]
        public void MovesAttachmentToNewTarget()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "old_host", faceUp: true);
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "new_host", faceUp: true);
            var att = new DeployedSupport { InstanceID = "att", CardID = "TST-0001", TargetInstanceID = "old_host", FaceUp = true };
            state.Player1Field.Support[0] = att;

            var effect = _registry.Build(CustomEffects.Reattach, null)!;
            effect(MakeOpContext(state, playerNum: 1, supSource: att,
                choiceData: new Dictionary<string, object> { ["instanceId"] = "new_host" }));

            att.TargetInstanceID.Should().Be("new_host");
        }
    }

    /// <summary>Tests waiving 維持コスト for an idle Elastic リソース.</summary>
    public class ScaleToZero : Base
    {
        [Fact]
        public void AddsMaintenanceReduction_ForIdleElastic()
        {
            _cc.Add(TestFactory.ElasticContainerCard(cardId: "TST-0003"));
            var state = TestFactory.MakeGameState(turn: 5);
            var source = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "src", faceUp: true, elasticBonus: 600);
            source.DeployedOnTurn = 1;
            source.LastAttackTurn = 0;
            state.Player1Field.Frontend[0] = source;

            var effect = _registry.Build(CustomEffects.ScaleToZero, null)!;
            effect(MakeOpContext(state, playerNum: 1, source: source));

            source.TemporaryEffects.Should().Contain(e =>
                e.EffectType == BuffTypes.MaintenanceReduction && e.Value == 60);
        }

        [Fact]
        public void Throws_WhenUsedOnDeployTurn()
        {
            _cc.Add(TestFactory.ElasticContainerCard(cardId: "TST-0003"));
            var state = TestFactory.MakeGameState(turn: 5);
            var source = TestFactory.MakeResource(cardId: "TST-0003", instanceId: "src", faceUp: true);
            source.DeployedOnTurn = 5;
            state.Player1Field.Frontend[0] = source;

            var effect = _registry.Build(CustomEffects.ScaleToZero, null)!;
            var act = () => effect(MakeOpContext(state, playerNum: 1, source: source));

            act.Should().Throw<GameRuleException>().WithMessage("*deploy turn*");
        }
    }

    /// <summary>Tests self-破壊 after the configured number of turns since deploy.</summary>
    public class SpotExpiry : Base
    {
        [Fact]
        public void DestroysSource_AfterExpiryTurns()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(turn: 3);
            var source = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            source.DeployedOnTurn = 1;
            state.Player1Field.Frontend[0] = source;

            var effect = _registry.Build(CustomEffects.SpotExpiry, Meta("""{"turns":2}"""))!;
            effect(MakeOpContext(state, playerNum: 1, source: source));

            FieldHelpers.FindResourceByID(state.Player1Field, "src").Should().BeNull();
        }

        [Fact]
        public void Survives_BeforeExpiry()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(turn: 2);
            var source = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src", faceUp: true);
            source.DeployedOnTurn = 1;
            state.Player1Field.Frontend[0] = source;

            var effect = _registry.Build(CustomEffects.SpotExpiry, Meta("""{"turns":2}"""))!;
            effect(MakeOpContext(state, playerNum: 1, source: source));

            FieldHelpers.FindResourceByID(state.Player1Field, "src").Should().NotBeNull();
        }
    }

    /// <summary>Tests the target_shield marker (deploy no-op) and the FieldHelpers protection check it drives.</summary>
    public class TargetShield : Base
    {
        [Fact]
        public void DeployEffect_IsNoOp()
        {
            var state = TestFactory.MakeGameState();
            var effect = _registry.Build(CustomEffects.TargetShield, null)!;

            var act = () => effect(MakeOpContext(state));

            act.Should().NotThrow();
        }

        [Fact]
        public void IsTargetShielded_True_WithShieldAndAnotherFrontend()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var shield = TestFactory.AttachmentCard(cardId: "TST-SHIELD");
            shield.Effects = [new EffectDef { Trigger = TriggerTypes.Passive, Custom = CustomEffects.TargetShield }];
            _cc.Add(shield);
            var field = TestFactory.MakeField();
            var protectedRes = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "prot", faceUp: true);
            field.Frontend[0] = protectedRes;
            field.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "wall", faceUp: true);
            field.Support[0] = new DeployedSupport { InstanceID = "s", CardID = "TST-SHIELD", TargetInstanceID = "prot", FaceUp = true };

            FieldHelpers.IsTargetShielded(protectedRes, field, _cc).Should().BeTrue();
        }

        [Fact]
        public void IsTargetShielded_False_WithoutAnotherFrontend()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var shield = TestFactory.AttachmentCard(cardId: "TST-SHIELD");
            shield.Effects = [new EffectDef { Trigger = TriggerTypes.Passive, Custom = CustomEffects.TargetShield }];
            _cc.Add(shield);
            var field = TestFactory.MakeField();
            var protectedRes = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "prot", faceUp: true);
            field.Frontend[0] = protectedRes;
            field.Support[0] = new DeployedSupport { InstanceID = "s", CardID = "TST-SHIELD", TargetInstanceID = "prot", FaceUp = true };

            FieldHelpers.IsTargetShielded(protectedRes, field, _cc).Should().BeFalse();
        }
    }

    /// <summary>Tests deploying a same-type card from 手札 to replace a destroyed リソース.</summary>
    public class DeploySameTypeFromHand : Base
    {
        [Fact]
        public void EnqueuesSlotSelect_WhenChoiceMatchesType()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", subtype: "VM"));
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "destroyed");
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];

            var effect = _registry.Build(CustomEffects.DeploySameTypeFromHand, null)!;
            effect(MakeOpContext(state, playerNum: 1, target: target,
                choiceData: new Dictionary<string, object> { ["cardId"] = "TST-0001" }));

            state.PendingSlotSelects.Should().ContainSingle();
            state.Player1Hand.Should().BeEmpty();
        }

        [Fact]
        public void Throws_WhenChoiceTypeMismatch()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", subtype: "VM"));
            _cc.Add(TestFactory.DataCard(cardId: "TST-DB", subtype: "Database"));
            var state = TestFactory.MakeGameState();
            var target = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "destroyed");
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-DB" }];

            var effect = _registry.Build(CustomEffects.DeploySameTypeFromHand, null)!;
            var act = () => effect(MakeOpContext(state, playerNum: 1, target: target,
                choiceData: new Dictionary<string, object> { ["cardId"] = "TST-DB" }));

            act.Should().Throw<GameRuleException>().WithMessage("*same type*");
        }
    }

    /// <summary>Tests cloud_shift: deploy a filtered card from 手札 with a discount, then self-破壊.</summary>
    public class CloudShift : Base
    {
        [Fact]
        public void DeploysFromHand_AppliesDiscount_AndSelfDestructs()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(p1Budget: 5000);
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" }];
            var sup = new DeployedSupport { InstanceID = "sup", CardID = "TST-PLAT", FaceUp = true };
            state.Player1Field.Support[0] = sup;

            var effect = _registry.Build(CustomEffects.CloudShift, Meta("""{"faction":"SHE","deploy_discount":300}"""))!;
            effect(MakeOpContext(state, playerNum: 1, supSource: sup,
                choiceData: new Dictionary<string, object> { ["cardId"] = "TST-0001" }));

            state.GetBudget(1).Should().Be(5300);
            state.PendingSlotSelects.Should().ContainSingle();
            state.Player1Field.Support.Select(s => s.InstanceID).Should().NotContain("sup");
        }
    }

    /// <summary>Tests redirect_attack validating the redirect target is the 相手 の フロントエンド.</summary>
    public class RedirectAttack : Base
    {
        [Fact]
        public void Validates_WhenTargetIsOpponentFrontend()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe", faceUp: true);

            var effect = _registry.Build(CustomEffects.RedirectAttack, null)!;
            var act = () => effect(MakeOpContext(state, playerNum: 1,
                choiceData: new Dictionary<string, object> { ["instanceId"] = "fe" }));

            act.Should().NotThrow();
        }

        [Fact]
        public void Throws_WhenTargetNotOpponentFrontend()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState();
            state.Player2Field.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be", faceUp: true);

            var effect = _registry.Build(CustomEffects.RedirectAttack, null)!;
            var act = () => effect(MakeOpContext(state, playerNum: 1,
                choiceData: new Dictionary<string, object> { ["instanceId"] = "be" }));

            act.Should().Throw<GameRuleException>().WithMessage("*frontend*");
        }
    }
}
