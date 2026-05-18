using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// Verifies the ADR-044 reactive resolution model: event-unit triggers, the single-Reactive
/// rule, on_deploy two-stage resolution, on_incident / on_damaged firing, and new guard ops.
/// </summary>
public class ReactiveResolutionTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public ReactiveResolutionTests()
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

    private static AttackRequest AttackReq(string attackerId, string targetId) =>
        new() { AttackerInstanceID = attackerId, TargetInstanceID = targetId };

    private static PlayCardRequest PlayReq(string instanceId, string zone, int index) =>
        new() { CardInstanceID = instanceId, Zone = zone, Index = index };

    // ─── on_attack_declared: single-Reactive rule ───────────────

    [Fact]
    public void OnAttackDeclared_OnlyEarliestReactiveFires()
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
    public void OnAttackDeclared_GuardFailedReactive_DoesNotConsumeAndYieldsToNext()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "ATK", instanceId: "atk", faceUp: true);
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "DEF", instanceId: "def", faceUp: true);
        state.Player2Field.Support[0] = new DeployedSupport { InstanceID = "early", CardID = "REACT-A", DeployOrder = 1 };
        state.Player2Field.Support[1] = new DeployedSupport { InstanceID = "late", CardID = "REACT-B", DeployOrder = 2 };

        int lateFires = 0;
        var effects = new TestEffectRegistry();
        effects.Register("REACT-A", TriggerType.OnAttackDeclared, _ => new EffectResult { GuardFailed = true });
        effects.Register("REACT-B", TriggerType.OnAttackDeclared, _ => { lateFires++; return new EffectResult(); });

        AttackProcessor.Process(state, _game, 1, AttackReq("atk", "def"), _cc, effects);

        lateFires.Should().Be(1, "a guard-failed Reactive yields the slot to the next eligible one");
        state.Player2Trash.Should().NotContain(c => c.CardID == "REACT-A",
            "a guard-failed Reactive is not consumed");
        state.Player2Trash.Should().Contain(c => c.CardID == "REACT-B",
            "the firing Reactive is consumed to trash");
    }

    // ─── FireOnDestroy: support-zone scan ───────────────────────

    [Fact]
    public void OnDestroy_ScansSupportZoneFaceDownReactive()
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

    // ─── on_deploy: two-stage resolution ────────────────────────

    [Fact]
    public void OnDeploy_WatcherResolvesBeforeEtb()
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
        effects.Register("DEPLOYED", TriggerType.OnDeploy, _ => { order.Add("etb"); return new EffectResult(); });

        PlayCardProcessor.Process(state, _game, 1, PlayReq("h", Zones.Frontend, 0), _cc, effects);

        order.Should().Equal("watcher", "etb");
    }

    [Fact]
    public void OnDeploy_CancelledByWatcher_SkipsEtbAndTrashesResource()
    {
        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h", CardID = "DEPLOYED" });
        state.Player2Field.Support[0] = new DeployedSupport
        {
            InstanceID = "watcher",
            CardID = "REACT-A",
            DeployOrder = 1,
        };

        bool etbFired = false;
        var effects = new TestEffectRegistry();
        effects.Register("REACT-A", TriggerType.OnDeploy, _ => new EffectResult { CancelAction = true });
        effects.Register("DEPLOYED", TriggerType.OnDeploy, _ => { etbFired = true; return new EffectResult(); });

        PlayCardProcessor.Process(state, _game, 1, PlayReq("h", Zones.Frontend, 0), _cc, effects);

        etbFired.Should().BeFalse("a cancelled deploy never reaches the ETB stage");
        state.Player1Field.Frontend[0].Should().BeNull("a cancelled deploy removes the resource");
        state.Player1Trash.Should().Contain(c => c.CardID == "DEPLOYED");
    }

    // ─── on_incident: support zone + field resources ────────────

    [Fact]
    public void OnIncident_FiresForComputeResourceWatcher()
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
    public void OnIncident_CancelledByWatcher_SkipsIncidentBody()
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
        effects.Register("REACT-A", TriggerType.OnIncident, _ => new EffectResult { CancelAction = true });
        effects.Register("INCIDENT", TriggerType.Ignition, _ =>
        {
            incidentBodyFired = true;
            return new EffectResult();
        });

        PlayCardProcessor.Process(state, _game, 1, PlayReq("h", Zones.Support, 0), _cc, effects);

        incidentBodyFired.Should().BeFalse("a cancelled incident skips its own ops");
    }

    // ─── on_damaged: fires after attack damage ──────────────────

    [Fact]
    public void OnDamaged_FiresAfterAttackDamageApplied()
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
