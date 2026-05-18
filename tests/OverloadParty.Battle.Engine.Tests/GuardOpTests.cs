using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// Verifies the ADR-044 guard ops: each guard either passes silently or throws
/// <see cref="GameRuleException"/> (which the composer surfaces as GuardFailed).
/// </summary>
public class GuardOpTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public GuardOpTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TENKI-VM", faction: "Tenki"));
        _cc.Add(TestFactory.ComputeCard(cardId: "SHE-VM", faction: "SHE"));
        _cc.Add(TestFactory.DataCard(cardId: "SHE-DB", faction: "SHE", subtype: "Database"));
        _cc.Add(new CardDefinition
        {
            CardId = "INC-A",
            CardName = "IncidentA",
            CardType = CardTypes.Incident,
            DeployTurns = 0,
        });
        _cc.Add(new CardDefinition
        {
            CardId = "INC-B",
            CardName = "IncidentB",
            CardType = CardTypes.Incident,
            DeployTurns = 0,
        });
    }

    private OpContext Ctx(
        long playerNum = 1,
        DeployedResource? source = null,
        DeployedResource? target = null,
        DeployedSupport? supSource = null,
        long? eventOwnerNum = null,
        CardDefinition? incidentCard = null,
        long? eventDamage = null)
    {
        return new OpContext(new EffectContext
        {
            State = TestFactory.MakeGameState(),
            Game = _game,
            PlayerNum = playerNum,
            Source = source,
            Target = target,
            SupSource = supSource,
            EventOwnerNum = eventOwnerNum,
            IncidentCard = incidentCard,
            EventDamage = eventDamage,
            CardCache = _cc,
        });
    }

    private static void ShouldPass(IEffectOp op, OpContext ctx) =>
        op.Invoking(o => o.Execute(ctx)).Should().NotThrow();

    private static void ShouldFail(IEffectOp op, OpContext ctx) =>
        op.Invoking(o => o.Execute(ctx)).Should().Throw<GameRuleException>();

    // ─── event_owner ────────────────────────────────────────────

    [Fact]
    public void EventOwner_Opponent_PassesWhenEventOwnerIsOpponent()
        => ShouldPass(new GuardEventOwnerOp(isSelf: false), Ctx(playerNum: 1, eventOwnerNum: 2));

    [Fact]
    public void EventOwner_Opponent_FailsWhenEventOwnerIsSelf()
        => ShouldFail(new GuardEventOwnerOp(isSelf: false), Ctx(playerNum: 1, eventOwnerNum: 1));

    [Fact]
    public void EventOwner_Self_PassesWhenEventOwnerIsSelf()
        => ShouldPass(new GuardEventOwnerOp(isSelf: true), Ctx(playerNum: 1, eventOwnerNum: 1));

    // ─── incident ───────────────────────────────────────────────

    [Fact]
    public void Incident_PassesWhenIncidentCardIdMatches()
        => ShouldPass(new GuardIncidentOp(["INC-A", "INC-B"]), Ctx(incidentCard: _cc.MustGet("INC-A")));

    [Fact]
    public void Incident_FailsWhenIncidentCardIdNotInSet()
        => ShouldFail(new GuardIncidentOp(["INC-B"]), Ctx(incidentCard: _cc.MustGet("INC-A")));

    [Fact]
    public void Incident_FailsWhenNoIncidentInContext()
        => ShouldFail(new GuardIncidentOp(["INC-A"]), Ctx());

    // ─── attacker ───────────────────────────────────────────────

    [Fact]
    public void Attacker_FactionMatch_PassesWhenAttackerFactionMatches()
    {
        var attacker = TestFactory.MakeResource(cardId: "TENKI-VM", instanceId: "atk");
        ShouldPass(new GuardAttackerOp(ownerIsOpponent: null, faction: "Tenki", cardType: null),
            Ctx(source: attacker));
    }

    [Fact]
    public void Attacker_FactionMatch_FailsWhenAttackerFactionDiffers()
    {
        var attacker = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "atk");
        ShouldFail(new GuardAttackerOp(ownerIsOpponent: null, faction: "Tenki", cardType: null),
            Ctx(source: attacker));
    }

    [Fact]
    public void Attacker_Owner_PassesWhenAttackerBelongsToOpponent()
    {
        var attacker = TestFactory.MakeResource(cardId: "TENKI-VM", instanceId: "atk");
        ShouldPass(new GuardAttackerOp(ownerIsOpponent: true, faction: null, cardType: null),
            Ctx(playerNum: 1, source: attacker, eventOwnerNum: 2));
    }

    // ─── lethal ─────────────────────────────────────────────────

    [Fact]
    public void Lethal_PassesWhenDamageAtOrAboveTargetAv()
    {
        var target = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "def", maxAV: 800, currentAV: 800);
        ShouldPass(GuardLethalOp.Instance, Ctx(target: target, eventDamage: 800));
    }

    [Fact]
    public void Lethal_FailsWhenDamageBelowTargetAv()
    {
        var target = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "def", maxAV: 800, currentAV: 800);
        ShouldFail(GuardLethalOp.Instance, Ctx(target: target, eventDamage: 799));
    }

    // ─── equip_host_is_target ───────────────────────────────────

    [Fact]
    public void EquipHostIsTarget_PassesWhenAttachmentHostIsEventTarget()
    {
        var host = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "host");
        var attachment = new DeployedSupport { InstanceID = "att", CardID = "SHE-VM", TargetInstanceID = "host" };
        ShouldPass(GuardEquipHostIsTargetOp.Instance, Ctx(target: host, supSource: attachment));
    }

    [Fact]
    public void EquipHostIsTarget_FailsWhenAttachmentHostIsNotEventTarget()
    {
        var other = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "other");
        var attachment = new DeployedSupport { InstanceID = "att", CardID = "SHE-VM", TargetInstanceID = "host" };
        ShouldFail(GuardEquipHostIsTargetOp.Instance, Ctx(target: other, supSource: attachment));
    }

    // ─── match guard with card_type list ────────────────────────

    [Fact]
    public void MatchGuard_CardTypeList_PassesWhenTargetMatchesAnyType()
    {
        var target = TestFactory.MakeResource(cardId: "SHE-DB", instanceId: "def");
        ShouldPass(new GuardFactionOp("SHE", ["Compute", "Data"]), Ctx(target: target));
    }

    [Fact]
    public void MatchGuard_CardTypeList_FailsWhenTargetMatchesNoType()
    {
        var target = TestFactory.MakeResource(cardId: "SHE-DB", instanceId: "def");
        ShouldFail(new GuardFactionOp("SHE", ["Compute"]), Ctx(target: target));
    }
}
