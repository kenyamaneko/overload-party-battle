using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// 発動条件 op が条件を満たすときは通過し、満たさないときは例外を投げることを検証します
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

    private EffectContext Ctx(
        long playerNum = 1,
        DeployedResource? source = null,
        DeployedResource? target = null,
        DeployedSupport? supSource = null,
        long? eventOwnerNum = null,
        CardDefinition? incidentCard = null,
        long? eventDamage = null)
    {
        return new EffectContext
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
            Effects = new EffectRegistry(),
        };
    }

    private static void ShouldPass(IEffectGuard guard, EffectContext ctx) =>
        guard.Check(ctx).Should().BeTrue();

    private static void ShouldFail(IEffectGuard guard, EffectContext ctx) =>
        guard.Check(ctx).Should().BeFalse();

    // ─── event_owner ────────────────────────────────────────────

    [Fact]
    public void EventOwner_Opponent_PassesWhenEventOwnerIsOpponent()
        => ShouldPass(new EventOwnerGuard(isSelf: false), Ctx(playerNum: 1, eventOwnerNum: 2));

    [Fact]
    public void EventOwner_Opponent_FailsWhenEventOwnerIsSelf()
        => ShouldFail(new EventOwnerGuard(isSelf: false), Ctx(playerNum: 1, eventOwnerNum: 1));

    [Fact]
    public void EventOwner_Self_PassesWhenEventOwnerIsSelf()
        => ShouldPass(new EventOwnerGuard(isSelf: true), Ctx(playerNum: 1, eventOwnerNum: 1));

    // ─── match: event_card selector ─────────────────────────────

    [Fact]
    public void MatchEventCard_PassesWhenEventCardIdMatches()
        => ShouldPass(
            new MatchGuard(MatchSelector.EventCard, cardIds: ["INC-A", "INC-B"]),
            Ctx(incidentCard: _cc.MustGet("INC-A")));

    [Fact]
    public void MatchEventCard_FailsWhenEventCardIdNotInSet()
        => ShouldFail(
            new MatchGuard(MatchSelector.EventCard, cardIds: ["INC-B"]),
            Ctx(incidentCard: _cc.MustGet("INC-A")));

    [Fact]
    public void MatchEventCard_FailsWhenNoEventCardInContext()
        => ShouldFail(
            new MatchGuard(MatchSelector.EventCard, cardIds: ["INC-A"]),
            Ctx());

    // ─── match: attacker selector ───────────────────────────────

    [Fact]
    public void MatchAttacker_FactionMatch_PassesWhenAttackerFactionMatches()
    {
        var attacker = TestFactory.MakeResource(cardId: "TENKI-VM", instanceId: "atk");
        ShouldPass(
            new MatchGuard(MatchSelector.Attacker, faction: "Tenki"),
            Ctx(source: attacker));
    }

    [Fact]
    public void MatchAttacker_FactionMatch_FailsWhenAttackerFactionDiffers()
    {
        var attacker = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "atk");
        ShouldFail(
            new MatchGuard(MatchSelector.Attacker, faction: "Tenki"),
            Ctx(source: attacker));
    }

    [Fact]
    public void MatchAttacker_Owner_PassesWhenAttackerBelongsToOpponent()
    {
        var attacker = TestFactory.MakeResource(cardId: "TENKI-VM", instanceId: "atk");
        ShouldPass(
            new MatchGuard(MatchSelector.Attacker, ownerIsOpponent: true),
            Ctx(playerNum: 1, source: attacker, eventOwnerNum: 2));
    }

    [Fact]
    public void MatchAttacker_Owner_FailsWhenAttackerBelongsToSelf()
    {
        var attacker = TestFactory.MakeResource(cardId: "TENKI-VM", instanceId: "atk");
        ShouldFail(
            new MatchGuard(MatchSelector.Attacker, ownerIsOpponent: true),
            Ctx(playerNum: 1, source: attacker, eventOwnerNum: 1));
    }

    // ─── match: target selector ─────────────────────────────────

    [Fact]
    public void MatchTarget_FactionAndCardTypeList_PassesWhenTargetMatchesAnyType()
    {
        var target = TestFactory.MakeResource(cardId: "SHE-DB", instanceId: "def");
        ShouldPass(
            new MatchGuard(MatchSelector.Target, faction: "SHE", cardTypes: ["Compute", "DataResource"]),
            Ctx(target: target));
    }

    [Fact]
    public void MatchTarget_FactionAndCardTypeList_FailsWhenTargetMatchesNoType()
    {
        var target = TestFactory.MakeResource(cardId: "SHE-DB", instanceId: "def");
        ShouldFail(
            new MatchGuard(MatchSelector.Target, faction: "SHE", cardTypes: ["Compute"]),
            Ctx(target: target));
    }

    [Fact]
    public void MatchTarget_Faction_FailsWhenTargetFactionDiffers()
    {
        var target = TestFactory.MakeResource(cardId: "TENKI-VM", instanceId: "def");
        ShouldFail(
            new MatchGuard(MatchSelector.Target, faction: "SHE"),
            Ctx(target: target));
    }

    // ─── lethal ─────────────────────────────────────────────────

    [Fact]
    public void Lethal_PassesWhenDamageAtOrAboveTargetAv()
    {
        var target = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "def", maxAV: 800, currentAV: 800);
        ShouldPass(LethalGuard.Instance, Ctx(target: target, eventDamage: 800));
    }

    [Fact]
    public void Lethal_FailsWhenDamageBelowTargetAv()
    {
        var target = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "def", maxAV: 800, currentAV: 800);
        ShouldFail(LethalGuard.Instance, Ctx(target: target, eventDamage: 799));
    }

    // ─── same: target / equip_host ──────────────────────────────

    [Fact]
    public void Same_TargetAndEquipHost_PassesWhenAttachmentHostIsEventTarget()
    {
        var host = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "host");
        var attachment = new DeployedSupport { InstanceID = "att", CardID = "SHE-VM", TargetInstanceID = "host" };
        ShouldPass(
            new SameGuard(ResourceRef.Target, ResourceRef.EquipHost),
            Ctx(target: host, supSource: attachment));
    }

    [Fact]
    public void Same_TargetAndEquipHost_FailsWhenAttachmentHostIsNotEventTarget()
    {
        var other = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "other");
        var attachment = new DeployedSupport { InstanceID = "att", CardID = "SHE-VM", TargetInstanceID = "host" };
        ShouldFail(
            new SameGuard(ResourceRef.Target, ResourceRef.EquipHost),
            Ctx(target: other, supSource: attachment));
    }

    [Fact]
    public void Same_FailsWhenSourceIsNotAnAttachment()
    {
        var host = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "host");
        ShouldFail(
            new SameGuard(ResourceRef.Target, ResourceRef.EquipHost),
            Ctx(target: host));
    }

    // ─── not_same: source / target ──────────────────────────────

    [Fact]
    public void NotSame_SourceAndTarget_PassesWhenDifferentResources()
    {
        var source = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "src");
        var target = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "tgt");
        ShouldPass(
            new NotSameGuard(ResourceRef.Source, ResourceRef.Target),
            Ctx(source: source, target: target));
    }

    [Fact]
    public void NotSame_SourceAndTarget_FailsWhenSameResource()
    {
        var resource = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "myself");
        ShouldFail(
            new NotSameGuard(ResourceRef.Source, ResourceRef.Target),
            Ctx(source: resource, target: resource));
    }
}
