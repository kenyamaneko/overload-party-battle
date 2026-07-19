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

    [Trait("対象", "イベントオーナーの発動条件")]
    public class EventOwnerCheck : Base
    {
        [Fact(DisplayName = "相手のイベントを条件とする発動条件は、イベントオーナーが相手のとき満たす")]
        public void EventOwner_Opponent_PassesWhenEventOwnerIsOpponent()
            => ShouldPass(new EventOwnerGuard(isSelf: false), Ctx(playerNum: 1, eventOwnerNum: 2));

        [Fact(DisplayName = "相手のイベントを条件とする発動条件は、イベントオーナーが自分のとき満たさない")]
        public void EventOwner_Opponent_FailsWhenEventOwnerIsSelf()
            => ShouldFail(new EventOwnerGuard(isSelf: false), Ctx(playerNum: 1, eventOwnerNum: 1));

        [Fact(DisplayName = "自分のイベントを条件とする発動条件は、イベントオーナーが自分のとき満たす")]
        public void EventOwner_Self_PassesWhenEventOwnerIsSelf()
            => ShouldPass(new EventOwnerGuard(isSelf: true), Ctx(playerNum: 1, eventOwnerNum: 1));
    }

    [Trait("対象", "イベントカード一致の発動条件")]
    public class MatchEventCard : Base
    {
        [Fact(DisplayName = "イベントカード ID が候補集合に含まれるとき満たす")]
        public void MatchEventCard_PassesWhenEventCardIdMatches()
            => ShouldPass(
                new MatchGuard(MatchSelector.EventCard, cardIds: ["INC-A", "INC-B"]),
                Ctx(incidentCard: _cc.MustGet("INC-A")));

        [Fact(DisplayName = "イベントカード ID が候補集合に含まれないとき満たさない")]
        public void MatchEventCard_FailsWhenEventCardIdNotInSet()
            => ShouldFail(
                new MatchGuard(MatchSelector.EventCard, cardIds: ["INC-B"]),
                Ctx(incidentCard: _cc.MustGet("INC-A")));

        [Fact(DisplayName = "コンテキストにイベントカードが無いとき満たさない")]
        public void MatchEventCard_FailsWhenNoEventCardInContext()
            => ShouldFail(
                new MatchGuard(MatchSelector.EventCard, cardIds: ["INC-A"]),
                Ctx());
    }

    [Trait("対象", "攻撃側一致の発動条件")]
    public class MatchAttacker : Base
    {
        [Fact(DisplayName = "攻撃側の陣営が指定と一致するとき満たす")]
        public void MatchAttacker_FactionMatch_PassesWhenAttackerFactionMatches()
        {
            var attacker = TestFactory.MakeResource(cardId: "TENKI-VM", instanceId: "atk");
            ShouldPass(
                new MatchGuard(MatchSelector.Attacker, faction: "Tenki"),
                Ctx(source: attacker));
        }

        [Fact(DisplayName = "攻撃側の陣営が指定と異なるとき満たさない")]
        public void MatchAttacker_FactionMatch_FailsWhenAttackerFactionDiffers()
        {
            var attacker = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "atk");
            ShouldFail(
                new MatchGuard(MatchSelector.Attacker, faction: "Tenki"),
                Ctx(source: attacker));
        }

        [Fact(DisplayName = "相手所有を条件とすると、攻撃側が相手のとき満たす")]
        public void MatchAttacker_Owner_PassesWhenAttackerBelongsToOpponent()
        {
            var attacker = TestFactory.MakeResource(cardId: "TENKI-VM", instanceId: "atk");
            ShouldPass(
                new MatchGuard(MatchSelector.Attacker, ownerIsOpponent: true),
                Ctx(playerNum: 1, source: attacker, eventOwnerNum: 2));
        }

        [Fact(DisplayName = "相手所有を条件とすると、攻撃側が自分のとき満たさない")]
        public void MatchAttacker_Owner_FailsWhenAttackerBelongsToSelf()
        {
            var attacker = TestFactory.MakeResource(cardId: "TENKI-VM", instanceId: "atk");
            ShouldFail(
                new MatchGuard(MatchSelector.Attacker, ownerIsOpponent: true),
                Ctx(playerNum: 1, source: attacker, eventOwnerNum: 1));
        }
    }

    [Trait("対象", "対象一致の発動条件")]
    public class MatchTarget : Base
    {
        [Fact(DisplayName = "対象の陣営が一致しカードタイプが候補のいずれかに一致するとき満たす")]
        public void MatchTarget_FactionAndCardTypeList_PassesWhenTargetMatchesAnyType()
        {
            var target = TestFactory.MakeResource(cardId: "SHE-DB", instanceId: "def");
            ShouldPass(
                new MatchGuard(MatchSelector.Target, faction: "SHE", cardTypes: ["Compute", "DataResource"]),
                Ctx(target: target));
        }

        [Fact(DisplayName = "対象のカードタイプが候補のいずれにも一致しないとき満たさない")]
        public void MatchTarget_FactionAndCardTypeList_FailsWhenTargetMatchesNoType()
        {
            var target = TestFactory.MakeResource(cardId: "SHE-DB", instanceId: "def");
            ShouldFail(
                new MatchGuard(MatchSelector.Target, faction: "SHE", cardTypes: ["Compute"]),
                Ctx(target: target));
        }

        [Fact(DisplayName = "対象の陣営が指定と異なるとき満たさない")]
        public void MatchTarget_Faction_FailsWhenTargetFactionDiffers()
        {
            var target = TestFactory.MakeResource(cardId: "TENKI-VM", instanceId: "def");
            ShouldFail(
                new MatchGuard(MatchSelector.Target, faction: "SHE"),
                Ctx(target: target));
        }
    }

    [Trait("対象", "致死ダメージ判定の発動条件")]
    public class LethalCheck : Base
    {
        [Fact(DisplayName = "ダメージが対象の可用性 800 に等しいとき満たす")]
        public void Lethal_PassesWhenDamageAtOrAboveTargetAv()
        {
            var target = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "def", maxAV: 800);
            ShouldPass(LethalGuard.Instance, Ctx(target: target, eventDamage: 800));
        }

        [Fact(DisplayName = "ダメージが対象の可用性 800 を下回る 799 のとき満たさない")]
        public void Lethal_FailsWhenDamageBelowTargetAv()
        {
            var target = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "def", maxAV: 800);
            ShouldFail(LethalGuard.Instance, Ctx(target: target, eventDamage: 799));
        }
    }

    [Trait("対象", "対象と装備先の一致の発動条件")]
    public class SameTargetEquipHost : Base
    {
        [Fact(DisplayName = "アタッチメントの装備先がイベントの対象と同じとき満たす")]
        public void Same_TargetAndEquipHost_PassesWhenAttachmentHostIsEventTarget()
        {
            var host = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "host");
            var attachment = new DeployedSupport { InstanceID = "att", CardID = "SHE-VM", TargetInstanceID = "host" };
            ShouldPass(
                new SameGuard(ResourceRef.Target, ResourceRef.EquipHost),
                Ctx(target: host, supSource: attachment));
        }

        [Fact(DisplayName = "アタッチメントの装備先がイベントの対象と異なるとき満たさない")]
        public void Same_TargetAndEquipHost_FailsWhenAttachmentHostIsNotEventTarget()
        {
            var other = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "other");
            var attachment = new DeployedSupport { InstanceID = "att", CardID = "SHE-VM", TargetInstanceID = "host" };
            ShouldFail(
                new SameGuard(ResourceRef.Target, ResourceRef.EquipHost),
                Ctx(target: other, supSource: attachment));
        }

        [Fact(DisplayName = "発動元がアタッチメントでないとき満たさない")]
        public void Same_FailsWhenSourceIsNotAnAttachment()
        {
            var host = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "host");
            ShouldFail(
                new SameGuard(ResourceRef.Target, ResourceRef.EquipHost),
                Ctx(target: host));
        }
    }

    [Trait("対象", "発動元と対象の相違の発動条件")]
    public class NotSameSourceTarget : Base
    {
        [Fact(DisplayName = "発動元と対象が異なるリソースのとき満たす")]
        public void NotSame_SourceAndTarget_PassesWhenDifferentResources()
        {
            var source = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "src");
            var target = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "tgt");
            ShouldPass(
                new NotSameGuard(ResourceRef.Source, ResourceRef.Target),
                Ctx(source: source, target: target));
        }

        [Fact(DisplayName = "発動元と対象が同じリソースのとき満たさない")]
        public void NotSame_SourceAndTarget_FailsWhenSameResource()
        {
            var resource = TestFactory.MakeResource(cardId: "SHE-VM", instanceId: "myself");
            ShouldFail(
                new NotSameGuard(ResourceRef.Source, ResourceRef.Target),
                Ctx(source: resource, target: resource));
        }
    }
}
