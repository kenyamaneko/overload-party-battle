using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class GuardOpTests
{
    /// <summary>Shared setup for effect-guard tests (card cache, game, context builder, and pass/fail assertion helpers).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
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

        /// <summary>Builds an effect context wired with the shared game and card cache.</summary>
        /// <param name="playerNum">The acting player number.</param>
        /// <param name="source">Optional source resource.</param>
        /// <param name="target">Optional target resource.</param>
        /// <param name="supSource">Optional source support (attachment).</param>
        /// <param name="eventOwnerNum">Optional event owner player number.</param>
        /// <param name="incidentCard">Optional incident card in context.</param>
        /// <param name="eventDamage">Optional event damage value.</param>
        /// <returns>An effect context with the supplied fields.</returns>
        protected EffectContext Ctx(
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

        /// <summary>Asserts that the guard passes for the given context.</summary>
        /// <param name="guard">The guard under test.</param>
        /// <param name="ctx">The effect context.</param>
        protected static void ShouldPass(IEffectGuard guard, EffectContext ctx) =>
            guard.Check(ctx).Should().BeTrue();

        /// <summary>Asserts that the guard fails for the given context.</summary>
        /// <param name="guard">The guard under test.</param>
        /// <param name="ctx">The effect context.</param>
        protected static void ShouldFail(IEffectGuard guard, EffectContext ctx) =>
            guard.Check(ctx).Should().BeFalse();
    }

    /// <summary>Tests for the event-owner guard.</summary>
    public class EventOwnerCheck : Base
    {
        [Fact]
        public void EventOwner_Opponent_PassesWhenEventOwnerIsOpponent()
            => ShouldPass(new EventOwnerGuard(isSelf: false), Ctx(playerNum: 1, eventOwnerNum: 2));

        [Fact]
        public void EventOwner_Opponent_FailsWhenEventOwnerIsSelf()
            => ShouldFail(new EventOwnerGuard(isSelf: false), Ctx(playerNum: 1, eventOwnerNum: 1));

        [Fact]
        public void EventOwner_Self_PassesWhenEventOwnerIsSelf()
            => ShouldPass(new EventOwnerGuard(isSelf: true), Ctx(playerNum: 1, eventOwnerNum: 1));
    }

    /// <summary>Tests for the match guard with the event-card selector.</summary>
    public class MatchEventCard : Base
    {
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
    }

    /// <summary>Tests for the match guard with the attacker selector.</summary>
    public class MatchAttacker : Base
    {
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
    }

    /// <summary>Tests for the match guard with the target selector.</summary>
    public class MatchTarget : Base
    {
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
    }

    /// <summary>Tests for the lethal guard.</summary>
    public class LethalCheck : Base
    {
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
    }

    /// <summary>Tests for the same guard comparing target and equip host.</summary>
    public class SameTargetEquipHost : Base
    {
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
    }

    /// <summary>Tests for the not-same guard comparing source and target.</summary>
    public class NotSameSourceTarget : Base
    {
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
}
