using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// イベント駆動トリガーの解決モデルが仕様どおり動くことを検証します
/// </summary>
public class EventTriggerResolutionTests
{
    /// <summary>イベント駆動トリガー解決テスト共通の card cache・game・リクエスト生成 helper を保持します。</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        /// <summary>イベントトリガー解決テスト用のカード定義を card cache へ登録します。</summary>
        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "ATK", tp: 600, av: 1400, deployTurns: 0));
            _cc.Add(TestFactory.ComputeCard(cardId: "DEF", tp: 600, av: 1400, deployTurns: 0));
            _cc.Add(TestFactory.ComputeCard(cardId: "DEPLOYED", tp: 600, av: 1400, deployTurns: 0));
            _cc.Add(TestFactory.ReactiveCard(cardId: "REACT-A"));
            _cc.Add(TestFactory.ReactiveCard(cardId: "REACT-B"));
            _cc.Add(TestFactory.PlatformCard(cardId: "PLATFORM"));
            _cc.Add(new CardDefinition
            {
                CardId = "INCIDENT",
                CardName = "TestIncident",
                CardType = CardTypes.Incident,
                DeployTurns = 0,
            });
        }

        /// <summary>攻撃リクエストを生成します。</summary>
        /// <param name="attackerId">攻撃側インスタンス ID。</param>
        /// <param name="targetId">対象インスタンス ID。</param>
        /// <returns>生成した攻撃リクエスト。</returns>
        protected static AttackRequest AttackReq(string attackerId, string targetId) =>
            new() { AttackerInstanceID = attackerId, TargetInstanceID = targetId };

        /// <summary>カードプレイリクエストを生成します。</summary>
        /// <param name="instanceId">プレイするカードのインスタンス ID。</param>
        /// <param name="zone">配置先ゾーン。</param>
        /// <param name="index">配置先インデックス。</param>
        /// <returns>生成したカードプレイリクエスト。</returns>
        protected static PlayCardRequest PlayReq(string instanceId, string zone, int index) =>
            new() { CardInstanceID = instanceId, Zone = zone, Index = index };
    }

    /// <summary>on_attack_declared の single-Reactive 解決ルールを検証します。</summary>
    public class OnAttackDeclaredResolution : Base
    {
        [Fact]
        public void OnlyEarliestReactiveFires()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "ATK", instanceId: "atk", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "DEF", instanceId: "def", faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "early", CardID = "REACT-A", DeployOrder = 1 };
            state.Player2Field.Support[1] = new DeployedSupport { InstanceID = "late", CardID = "REACT-B", DeployOrder = 2 };

            int earlyFires = 0;
            int lateFires = 0;
            var effects = new TestEffectRegistry();
            effects.Register("REACT-A", TriggerType.OnAttackDeclared, _ => { earlyFires++; return new EffectResult(); });
            effects.Register("REACT-B", TriggerType.OnAttackDeclared, _ => { lateFires++; return new EffectResult(); });

            AttackProcessor.Process(state, _game, 1, AttackReq("atk", "def"), _cc, effects);

            earlyFires.Should().Be(1, "the earliest-set Reactive is the one that fires");
            lateFires.Should().Be(0, "only one Reactive fires per event");
        }

        [Fact]
        public void GuardFailedReactive_DoesNotConsumeAndYieldsToNext()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "ATK", instanceId: "atk", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "DEF", instanceId: "def", faceUp: true);
            state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "early", CardID = "REACT-A", DeployOrder = 1 };
            state.Player2Field.Support[1] = new DeployedSupport { InstanceID = "late", CardID = "REACT-B", DeployOrder = 2 };

            int lateFires = 0;
            var effects = new TestEffectRegistry();
            effects.Register("REACT-A", TriggerType.OnAttackDeclared, _ => new EffectResult { HasGuardFailed = true });
            effects.Register("REACT-B", TriggerType.OnAttackDeclared, _ => { lateFires++; return new EffectResult(); });

            AttackProcessor.Process(state, _game, 1, AttackReq("atk", "def"), _cc, effects);

            lateFires.Should().Be(1, "a guard-failed Reactive yields the slot to the next eligible one");
            state.Player2Trash.Should().NotContain(c => c.CardID == "REACT-A",
                "a guard-failed Reactive is not consumed");
            state.Player2Trash.Should().Contain(c => c.CardID == "REACT-B",
                "the firing Reactive is consumed to trash");
        }
    }

    /// <summary>FireOnDestroy の support ゾーン scan を検証します。</summary>
    public class OnDestroyResolution : Base
    {
        [Fact]
        public void ScansSupportZoneFaceDownReactive()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "ATK", instanceId: "atk", faceUp: true, maxTP: 5000, currentTP: 5000);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "DEF", instanceId: "def", faceUp: true, maxAV: 100, currentAV: 100);
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "watcher",
                CardID = "REACT-A",
                FaceUp = false,
                DeployOrder = 1,
            };

            bool fired = false;
            var effects = new TestEffectRegistry();
            effects.Register("REACT-A", TriggerType.OnDestroy, _ => { fired = true; return new EffectResult(); });

            AttackProcessor.Process(state, _game, 1, AttackReq("atk", "def"), _cc, effects);

            fired.Should().BeTrue("on_destroy scans face-down Reactives in the support zone");
        }
    }

    /// <summary>on_deploy の two-stage 解決を検証します。</summary>
    public class OnDeployResolution : Base
    {
        [Fact]
        public void WatcherResolvesBeforeEtb()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h", CardID = "DEPLOYED" });
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "watcher",
                CardID = "REACT-A",
                DeployOrder = 1,
            };

            var order = new List<string>();
            var effects = new TestEffectRegistry();
            effects.Register("REACT-A", TriggerType.OnDeploy, _ => { order.Add("watcher"); return new EffectResult(); });
            effects.Register("DEPLOYED", TriggerType.OnDeploy, _ => { order.Add("deploy-self"); return new EffectResult(); });

            PlayCardProcessor.Process(state, _game, 1, PlayReq("h", Zones.Frontend, 0), _cc, effects);

            order.Should().Equal("watcher", "deploy-self");
        }

        [Fact]
        public void CancelledByWatcher_SkipsDeployEffectAndTrashesResource()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h", CardID = "DEPLOYED" });
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "watcher",
                CardID = "REACT-A",
                DeployOrder = 1,
            };

            bool deployEffectFired = false;
            var effects = new TestEffectRegistry();
            effects.Register("REACT-A", TriggerType.OnDeploy, _ => new EffectResult { ShouldCancelAction = true });
            effects.Register("DEPLOYED", TriggerType.OnDeploy, _ => { deployEffectFired = true; return new EffectResult(); });

            PlayCardProcessor.Process(state, _game, 1, PlayReq("h", Zones.Frontend, 0), _cc, effects);

            deployEffectFired.Should().BeFalse("a cancelled deploy never runs the deployed card's own effect");
            state.Player1Field.Frontend[0].Should().BeNull("a cancelled deploy removes the resource");
            state.Player1Trash.Should().Contain(c => c.CardID == "DEPLOYED");
        }
    }

    /// <summary>on_incident の support ゾーン + フィールドリソース scan を検証します。</summary>
    public class OnIncidentResolution : Base
    {
        [Fact]
        public void FiresForComputeResourceWatcher()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h", CardID = "INCIDENT" });
            // A Compute resource (not in the support zone) watches for incidents.
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "DEF", instanceId: "compute_watcher", faceUp: true);

            bool fired = false;
            var effects = new TestEffectRegistry();
            effects.Register("DEF", TriggerType.OnIncident, _ => { fired = true; return new EffectResult(); });

            PlayCardProcessor.Process(state, _game, 1, PlayReq("h", Zones.Support, 0), _cc, effects);

            fired.Should().BeTrue("on_incident scans field resources, not just the support zone");
        }

        [Fact]
        public void CancelledByWatcher_SkipsIncidentBody()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h", CardID = "INCIDENT" });
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "blocker",
                CardID = "REACT-A",
                DeployOrder = 1,
            };

            bool incidentBodyFired = false;
            var effects = new TestEffectRegistry();
            effects.Register("REACT-A", TriggerType.OnIncident, _ => new EffectResult { ShouldCancelAction = true });
            effects.Register("INCIDENT", TriggerType.Ignition, _ =>
            {
                incidentBodyFired = true;
                return new EffectResult();
            });

            PlayCardProcessor.Process(state, _game, 1, PlayReq("h", Zones.Support, 0), _cc, effects);

            incidentBodyFired.Should().BeFalse("a cancelled incident skips its own ops");
        }
    }

    /// <summary>on_damaged が攻撃ダメージ適用後に発火することを検証します。</summary>
    public class OnDamagedResolution : Base
    {
        [Fact]
        public void FiresAfterAttackDamageApplied()
        {
            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            // ATK card base TP is 600; the attack deals 600 damage to the defender.
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "ATK", instanceId: "atk", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "DEF", instanceId: "def", faceUp: true, maxAV: 1400, currentAV: 1400);
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "watcher",
                CardID = "REACT-A",
                DeployOrder = 1,
            };

            long observedDamage = -1;
            var effects = new TestEffectRegistry();
            effects.Register("REACT-A", TriggerType.OnDamaged, ctx =>
            {
                observedDamage = ctx.Target!.Damage;
                return new EffectResult();
            });

            AttackProcessor.Process(state, _game, 1, AttackReq("atk", "def"), _cc, effects);

            observedDamage.Should().Be(600, "on_damaged observes the target after attack damage is applied");
        }
    }
}
