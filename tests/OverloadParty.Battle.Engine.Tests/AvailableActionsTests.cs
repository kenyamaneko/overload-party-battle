using System.Linq;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class AvailableActionsTests
{
    /// <summary>Shared marker base for AvailableActions test groups.</summary>
    public abstract class Base
    {
    }

    /// <summary>Tests for which action types are offered per phase (Main vs Battle).</summary>
    public class PhaseGating : Base
    {
        [Fact]
        public void MainPhase_ReturnsPlayScaleDistributeIgniteActions()
        {
            var cc = new TestCardCache();
            var compute = TestFactory.ComputeCard(cardId: "TST-0001");
            var db = TestFactory.DataCard(cardId: "TST-0002");
            cc.Add(compute);
            cc.Add(db);

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 5000);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1");
            myField.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1");

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "TST-0001" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), hand, 5000, 100, cc, new EffectRegistry());

            actions.Should().Contain(a => a.Type == ActionTypes.PlayCard);
            actions.Should().Contain(a => a.Type == ActionTypes.ScaleUp);
            actions.Should().Contain(a => a.Type == ActionTypes.Monetize);
            actions.Should().NotContain(a => a.Type == ActionTypes.Attack);
        }

        [Fact]
        public void BattlePhase_ReturnsAttackAndIgniteOnly()
        {
            var cc = new TestCardCache();
            var compute = TestFactory.ComputeCard(cardId: "TST-0001");
            cc.Add(compute);

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle, p1Budget: 5000);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1");

            var oppField = TestFactory.MakeField();
            oppField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, [], 5000, 0, cc, new EffectRegistry());

            actions.Should().Contain(a => a.Type == ActionTypes.Attack);
            actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard);
            actions.Should().NotContain(a => a.Type == ActionTypes.ScaleUp);
            actions.Should().NotContain(a => a.Type == ActionTypes.Monetize);
        }
    }

    /// <summary>Tests for enumerating UseInitiative actions (routine/special) per affordability and usage rules.</summary>
    public class UseInitiativeEnumeration : Base
    {
        private const string TestRoutineId = "IN-TST-R";
        private const string TestSpecialId = "IN-TST-S";

        private static InitiativeCatalog InitiativeCatalogWith(long routineCost, long specialCost) =>
            new(
            [
                new Initiative { InitiativeId = TestRoutineId, Kind = InitiativeKinds.Routine, Name = "R", InsightCost = routineCost },
                new Initiative { InitiativeId = TestSpecialId, Kind = InitiativeKinds.Special, Name = "S", InsightCost = specialCost },
            ]);

        private static BattleGameState MakeInitiativeState(long turn = 3)
        {
            var state = TestFactory.MakeGameState(turn: turn, phase: Phase.Main);
            state.Player1RoutineId = TestRoutineId;
            state.Player1SpecialId = TestSpecialId;
            return state;
        }

        [Fact]
        public void UseInitiative_EnumeratesRoutineAndSpecial_WhenAffordableAndUnused()
        {
            var state = MakeInitiativeState();

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), [], 5000, insightPool: 1000,
                new TestCardCache(), new EffectRegistry(), InitiativeCatalogWith(routineCost: 100, specialCost: 300));

            var inits = actions.Where(a => a.Type == ActionTypes.UseInitiative).ToList();
            inits.Should().Contain(a => a.Kind == InitiativeKinds.Routine && a.CardID == TestRoutineId && a.Cost == 100);
            inits.Should().Contain(a => a.Kind == InitiativeKinds.Special && a.CardID == TestSpecialId && a.Cost == 300);
        }

        [Fact]
        public void UseInitiative_ExcludesUnaffordableKind()
        {
            var state = MakeInitiativeState();

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), [], 5000, insightPool: 150,
                new TestCardCache(), new EffectRegistry(), InitiativeCatalogWith(routineCost: 100, specialCost: 300));

            var inits = actions.Where(a => a.Type == ActionTypes.UseInitiative).ToList();
            inits.Should().Contain(a => a.Kind == InitiativeKinds.Routine);    // 100 <= 150
            inits.Should().NotContain(a => a.Kind == InitiativeKinds.Special); // 300 > 150
        }

        [Fact]
        public void UseInitiative_ExcludesAlreadyUsed()
        {
            var state = MakeInitiativeState();
            state.SetRoutineUsedThisTurn(1, true);
            state.SetSpecialUsedThisGame(1, true);

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), [], 5000, insightPool: 1000,
                new TestCardCache(), new EffectRegistry(), InitiativeCatalogWith(routineCost: 100, specialCost: 300));

            actions.Should().NotContain(a => a.Type == ActionTypes.UseInitiative);
        }

        [Fact]
        public void UseInitiative_NotEnumeratedOnFirstTurn()
        {
            var state = MakeInitiativeState(turn: 1);

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), [], 5000, insightPool: 1000,
                new TestCardCache(), new EffectRegistry(), InitiativeCatalogWith(routineCost: 100, specialCost: 300));

            actions.Should().NotContain(a => a.Type == ActionTypes.UseInitiative);
        }

        [Fact]
        public void UseInitiative_NotEnumeratedWhenCatalogOmitted()
        {
            var state = MakeInitiativeState();

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), [], 5000, insightPool: 1000,
                new TestCardCache(), new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.UseInitiative);
        }

        [Fact]
        public void UseInitiative_Throws_WhenSlotInitiativeNotInCatalog()
        {
            var state = MakeInitiativeState();
            state.Player1RoutineId = "IN-NOPE"; // カタログに無い ID = データ整合性エラー

            var act = () => AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), [], 5000, insightPool: 1000,
                new TestCardCache(), new EffectRegistry(), InitiativeCatalogWith(routineCost: 100, specialCost: 300));

            act.Should().Throw<GameRuleException>().WithMessage("*not found*");
        }
    }

    /// <summary>Tests for which zones a played card may occupy by card type/subtype.</summary>
    public class PlayCardZonePlacement : Base
    {
        [Fact]
        public void PlayCard_ComputeCanGoToFrontendAndBackend()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "TST-0001" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_1");
            playAction.ValidZones.Should().Contain(z => z.StartsWith("frontend_"));
            playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
        }

        [Fact]
        public void PlayCard_DatabaseCanOnlyGoToBackend()
        {
            // Database は Backend のみ
            var cc = new TestCardCache();
            cc.Add(TestFactory.DataCard(cardId: "TST-0002", subtype: "Database"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_db", CardID = "TST-0002" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_db");
            playAction.ValidZones.Should().NotContain(z => z.StartsWith("frontend_"));
            playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
        }

        [Fact]
        public void PlayCard_CacheDBCanOnlyGoToBackend()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.DataCard(cardId: "TST-0003", subtype: "CacheDB"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_cache", CardID = "TST-0003" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_cache");
            playAction.ValidZones.Should().NotContain(z => z.StartsWith("frontend_"));
            playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
        }

        [Fact]
        public void PlayCard_ObjectStorageCanGoToFrontendAndBackend()
        {
            // ObjectStorage は Frontend / Backend 両方
            var cc = new TestCardCache();
            cc.Add(TestFactory.DataCard(cardId: "TST-0004", subtype: "ObjectStorage"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_os", CardID = "TST-0004" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_os");
            playAction.ValidZones.Should().Contain(z => z.StartsWith("frontend_"));
            playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
        }

        [Fact]
        public void PlayCard_AiMlCanGoToFrontendAndBackend()
        {
            // AI/ML は Frontend / Backend 両方
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0005", subtype: "AI/ML", deployTurns: 0));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_ai", CardID = "TST-0005" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_ai");
            playAction.ValidZones.Should().Contain(z => z.StartsWith("frontend_"));
            playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
        }

        [Fact]
        public void PlayCard_StrategyDoesNotRequireZone()
        {
            var cc = new TestCardCache();
            cc.Add(new CardDefinition { CardId = "TST-0006", CardName = "S", CardType = CardTypes.Strategy });

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_s", CardID = "TST-0006" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_s");
            playAction.ValidZones.Should().BeNullOrEmpty();
        }

        [Fact]
        public void PlayCard_ReactiveGoesToSupportZone()
        {
            var cc = new TestCardCache();
            cc.Add(new CardDefinition { CardId = "TST-0007", CardName = "R", CardType = CardTypes.Reactive });

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_r", CardID = "TST-0007" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_r");
            playAction.ValidZones.Should().AllSatisfy(z => z.Should().StartWith("support_"));
        }

        [Fact]
        public void PlayCard_PlatformGoesToSupportZone()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_p", CardID = "TEST-0200" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_p");
            playAction.ValidZones.Should().AllSatisfy(z => z.Should().StartWith("support_"));
        }
    }

    /// <summary>Tests for PlayCard availability based on slot capacity.</summary>
    public class PlayCardSlotCapacity : Base
    {
        [Fact]
        public void PlayCard_ExcludedWhenAllSlotsOccupied()
        {
            // 各ゾーン上限3体、スロットが埋まっていれば配置不可
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            // Fill all frontend and backend slots
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_0");
            myField.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1");
            myField.Frontend[2] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_2");
            myField.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_0");
            myField.Backend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1");
            myField.Backend[2] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_2");

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "TST-0001" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_1");
        }

        [Fact]
        public void PlayCard_StrategyAvailableEvenWhenAllSupportSlotsOccupied()
        {
            var cc = new TestCardCache();
            cc.Add(new CardDefinition { CardId = "TST-0006", CardName = "S", CardType = CardTypes.Strategy });
            cc.Add(TestFactory.PlatformCard(cardId: "TST-0007"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Support[0] = new DeployedSupport { InstanceID = "s_0", CardID = "TST-0007" };
            myField.Support[1] = new DeployedSupport { InstanceID = "s_1", CardID = "TST-0007" };
            myField.Support[2] = new DeployedSupport { InstanceID = "s_2", CardID = "TST-0007" };

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_s", CardID = "TST-0006" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            actions.Should().Contain(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_s");
        }

        [Fact]
        public void PlayCard_OnlyOffersEmptySlots()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_0");
            // Frontend slots 1,2 are empty; all backend slots are empty

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "TST-0001" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_1");
            playAction.ValidZones.Should().NotContain(z => z == "frontend_0");
            playAction.ValidZones.Should().Contain(z => z == "frontend_1");
            playAction.ValidZones.Should().Contain(z => z == "frontend_2");
        }
    }

    /// <summary>Tests for Incident play restrictions (first turn and once-per-turn).</summary>
    public class PlayCardIncident : Base
    {
        [Fact]
        public void PlayCard_IncidentExcludedOnFirstTurn()
        {
            // 先攻T1ではインシデント使用不可
            var cc = new TestCardCache();
            cc.Add(new CardDefinition { CardId = "TST-0008", CardName = "I", CardType = CardTypes.Incident });

            var state = TestFactory.MakeGameState(turn: 1, phase: Phase.Main);
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_i", CardID = "TST-0008" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_i");
        }

        [Fact]
        public void PlayCard_IncidentAllowedOnLaterTurns()
        {
            var cc = new TestCardCache();
            cc.Add(new CardDefinition { CardId = "TST-0008", CardName = "I", CardType = CardTypes.Incident });

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_i", CardID = "TST-0008" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            actions.Should().Contain(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_i");
        }

        [Fact]
        public void PlayCard_IncidentExcludedWhenAlreadyPlayedThisTurn()
        {
            // インシデントは1ターン1枚まで
            var cc = new TestCardCache();
            cc.Add(new CardDefinition { CardId = "TST-0008", CardName = "I", CardType = CardTypes.Incident });

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            state.Player1IncidentPlayedThisTurn = true;
            var myField = TestFactory.MakeField();

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_i", CardID = "TST-0008" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_i");
        }
    }

    /// <summary>Tests for Attachment targeting and placement rules.</summary>
    public class PlayCardAttachment : Base
    {
        [Fact]
        public void PlayCard_AttachmentTargetsFaceUpResources()
        {
            // Attachment はリソースに装備。ValidTargets を返す
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.AttachmentCard(cardId: "TEST-0300"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1");

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_att", CardID = "TEST-0300" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_att");
            playAction.ValidTargets.Should().Contain(t => t == "fe_1");
            playAction.ValidZones.Should().NotBeEmpty();
            playAction.ValidZones.Should().AllSatisfy(z => z.Should().StartWith("support_"));
        }

        [Fact]
        public void PlayCard_AttachmentAvailableWhenSupportZoneFull()
        {
            // サポートゾーンが満杯でも張り替えで配置可能
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.AttachmentCard(cardId: "TEST-0300"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1");
            myField.Support[0] = new DeployedSupport { InstanceID = "s_0", CardID = "TEST-0300", TargetInstanceID = "fe_1" };
            myField.Support[1] = new DeployedSupport { InstanceID = "s_1", CardID = "TEST-0300", TargetInstanceID = "fe_1" };
            myField.Support[2] = new DeployedSupport { InstanceID = "s_2", CardID = "TEST-0300", TargetInstanceID = "fe_1" };

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_att", CardID = "TEST-0300" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            actions.Should().Contain(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_att");
        }

        [Fact]
        public void PlayCard_AttachmentExcludesFaceDownResources()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.AttachmentCard(cardId: "TEST-0300"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1", faceUp: false);

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_att", CardID = "TEST-0300" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_att");
        }

        [Fact]
        public void PlayCard_AttachmentTargetsAllFaceUpResources()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.AttachmentCard(cardId: "TEST-0300"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();

            var res1 = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1");
            myField.Frontend[0] = res1;
            myField.Support.TryPlace(new DeployedSupport { InstanceID = "a1", CardID = "TEST-0300", TargetInstanceID = "fe_1" });
            myField.Support.TryPlace(new DeployedSupport { InstanceID = "a2", CardID = "TEST-0300", TargetInstanceID = "fe_1" });

            var res2 = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_2");
            myField.Frontend[1] = res2;

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_att", CardID = "TEST-0300" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_att");
            playAction.ValidTargets.Should().Contain(t => t == "fe_1");
            playAction.ValidTargets.Should().Contain(t => t == "fe_2");
        }
    }

    /// <summary>Tests for PlayCard with an empty hand.</summary>
    public class PlayCardEmptyHand : Base
    {
        [Fact]
        public void PlayCard_EmptyHandReturnsNoPlayActions()
        {
            var cc = new TestCardCache();
            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard);
        }
    }

    /// <summary>Tests for budget-gated availability of Strategy/Incident plays.</summary>
    public class PlayCardBudgetFiltering : Base
    {
        [Fact]
        public void PlayStrategy_ExcludedWhenBudgetInsufficient()
        {
            var cc = new TestCardCache();
            cc.Add(new CardDefinition { CardId = "TST-0006", CardName = "S", CardType = CardTypes.Strategy });

            var registry = new EffectRegistry();
            registry.RegisterComposed("TST-0006", TriggerType.Ignition,
                [new MinBudgetGuard(500)],
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(1000)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 200);
            var hand = new List<UndeployedCard> { new() { InstanceID = "hand_50", CardID = "TST-0006" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 200, 0, cc, registry);

            actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "hand_50");
        }

        [Fact]
        public void PlayStrategy_IncludedWhenBudgetSufficient()
        {
            var cc = new TestCardCache();
            cc.Add(new CardDefinition { CardId = "TST-0006", CardName = "S", CardType = CardTypes.Strategy });

            var registry = new EffectRegistry();
            registry.RegisterComposed("TST-0006", TriggerType.Ignition,
                [new MinBudgetGuard(500)],
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(1000)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 600);
            var hand = new List<UndeployedCard> { new() { InstanceID = "hand_50", CardID = "TST-0006" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 600, 0, cc, registry);

            actions.Should().Contain(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "hand_50");
        }

        [Fact]
        public void PlayIncident_ExcludedWhenBudgetInsufficient()
        {
            var cc = new TestCardCache();
            cc.Add(new CardDefinition { CardId = "TST-0008", CardName = "I", CardType = CardTypes.Incident });

            var registry = new EffectRegistry();
            registry.RegisterComposed("TST-0008", TriggerType.Ignition,
                [new MinBudgetGuard(300)],
                new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(300)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 100);
            var hand = new List<UndeployedCard> { new() { InstanceID = "hand_60", CardID = "TST-0008" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 100, 0, cc, registry);

            actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "hand_60");
        }
    }

    /// <summary>Tests for PlayCard availability when frontend/backend zones are full.</summary>
    public class PlayCardFieldOccupancy : Base
    {
        [Fact]
        public void PlayCard_ComputeOnlyBackendWhenFrontendFull()
        {
            // フロントが3枠埋まっていてもバックエンドに空きがあればCompute配置可能
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_0");
            myField.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1");
            myField.Frontend[2] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_2");

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "TST-0001" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_1");
            playAction.ValidZones.Should().NotContain(z => z.StartsWith("frontend_"));
            playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
        }

        [Fact]
        public void PlayCard_DatabaseStillPlayableWhenFrontendFull()
        {
            // Database はそもそも Backend のみ。フロントが満杯でも関係ない
            var cc = new TestCardCache();
            cc.Add(TestFactory.DataCard(cardId: "TST-0002"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_0");
            myField.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1");
            myField.Frontend[2] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_2");

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_db", CardID = "TST-0002" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            actions.Should().Contain(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_db");
        }

        [Fact]
        public void PlayCard_ComputeExcludedWhenBothZonesFull()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            for (int i = 0; i < 3; i++)
            {
                myField.Frontend[i] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: $"fe_{i}");
                myField.Backend[i] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: $"be_{i}");
            }

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "TST-0001" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_1");
        }
    }

    /// <summary>Tests for zone placement of additional resource subtypes (Container/Serverless/Orchestrator).</summary>
    public class PlayCardResourceTypePlacement : Base
    {
        [Fact]
        public void PlayCard_ContainerCanGoToFrontendAndBackend()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ElasticContainerCard(cardId: "TEST-0002"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_c", CardID = "TEST-0002" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_c");
            playAction.ValidZones.Should().Contain(z => z.StartsWith("frontend_"));
            playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
        }

        [Fact]
        public void PlayCard_ServerlessCanGoToFrontendAndBackend()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ServerlessCard(cardId: "TST-0012"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_s", CardID = "TST-0012" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_s");
            playAction.ValidZones.Should().Contain(z => z.StartsWith("frontend_"));
            playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
        }

        [Fact]
        public void PlayCard_OrchestratorCanGoToFrontendAndBackend()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.OrchestratorCard(cardId: "TST-0013"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_o", CardID = "TST-0013" } };

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, new EffectRegistry());

            var playAction = actions.Single(a => a.Type == ActionTypes.PlayCard && a.HandInstanceID == "h_o");
            playAction.ValidZones.Should().Contain(z => z.StartsWith("frontend_"));
            playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
        }
    }

    /// <summary>Tests for ScaleUp rank-up options and exclusions.</summary>
    public class ScaleUpRules : Base
    {
        [Fact]
        public void ScaleUp_ResizableSmallOffersAllOptions()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", resizable: true));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1", rank: Rank.Small);

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            var scaleActions = actions.Where(a => a.Type == ActionTypes.ScaleUp && a.SourceInstanceID == "fe_1").ToList();
            // Small → Medium×3 families + Small → Large×3 families = 6 options
            scaleActions.Should().HaveCount(6);
            scaleActions.Should().Contain(a => a.TargetRank == "medium" && a.InstanceFamily == "M");
            scaleActions.Should().Contain(a => a.TargetRank == "medium" && a.InstanceFamily == "C");
            scaleActions.Should().Contain(a => a.TargetRank == "medium" && a.InstanceFamily == "R");
            scaleActions.Should().Contain(a => a.TargetRank == "large" && a.InstanceFamily == "M");
            scaleActions.Should().Contain(a => a.TargetRank == "large" && a.InstanceFamily == "C");
            scaleActions.Should().Contain(a => a.TargetRank == "large" && a.InstanceFamily == "R");
        }

        [Fact]
        public void ScaleUp_ResizableMediumOffersAllOptions()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", resizable: true));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "fe_1", rank: Rank.Medium, family: InstanceFamily.M);

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            var scaleActions = actions.Where(a => a.Type == ActionTypes.ScaleUp && a.SourceInstanceID == "fe_1").ToList();
            // Medium(M) can only scale up to Large with same family = 1 option
            scaleActions.Should().HaveCount(1);
            scaleActions.Should().Contain(a => a.TargetRank == "large" && a.InstanceFamily == "M");
        }

        [Fact]
        public void ScaleUp_ResizableLargeHasNoOptions()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", resizable: true));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "fe_1", rank: Rank.Large, family: InstanceFamily.M);

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            var scaleActions = actions.Where(a => a.Type == ActionTypes.ScaleUp && a.SourceInstanceID == "fe_1").ToList();
            // Large is max rank — no scale up options
            scaleActions.Should().BeEmpty();
        }

        [Fact]
        public void ScaleUp_NonResizableExcluded()
        {
            // Resizable でないカードはスケールアップ不可
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", resizable: false));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.ScaleUp);
        }

        [Fact]
        public void ScaleUp_DormantExcluded()
        {
            // 休止リソースはスケールアップ不可
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", resizable: true));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1", rank: Rank.Small);
            resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = BuffTypes.Dormant });
            myField.Frontend[0] = resource;

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.ScaleUp);
        }

        [Fact]
        public void ScaleUp_ElasticOnlyExcluded()
        {
            // Elastic-only カード（Resizable=false）は手動スケールアップ不可
            var cc = new TestCardCache();
            cc.Add(TestFactory.ElasticContainerCard(cardId: "TEST-0002"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "fe_2");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.ScaleUp);
        }

        [Fact]
        public void ScaleUp_FaceDownResourceExcluded()
        {
            // 裏向きカードは「いないものとみなす」
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", resizable: true));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1", faceUp: false);

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.ScaleUp);
        }

        [Fact]
        public void ScaleUp_ResizableElasticCanScaleUp()
        {
            // R+E カードは手動スケールアップも可能
            var cc = new TestCardCache();
            cc.Add(TestFactory.OrchestratorCard(cardId: "TST-0013")); // R+E

            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0013", instanceId: "orch_1", rank: Rank.Small);

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            actions.Should().Contain(a => a.Type == ActionTypes.ScaleUp && a.SourceInstanceID == "orch_1");
        }

        [Fact]
        public void ScaleUp_DataCardResizableCanScaleUp()
        {
            // Resizable な Database もスケールアップ可能
            var cc = new TestCardCache();
            cc.Add(TestFactory.DataCard(cardId: "TST-0002", resizable: true));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Backend[0] = TestFactory.MakeResource(
                cardId: "TST-0002", instanceId: "db_1", maxTP: null, currentTP: null, maxYield: 400, currentYield: 400);

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            actions.Should().Contain(a => a.Type == ActionTypes.ScaleUp && a.SourceInstanceID == "db_1");
        }
    }

    /// <summary>Tests for Attack eligibility and valid targets in the battle phase.</summary>
    public class AttackRules : Base
    {
        [Fact]
        public void Attack_FrontendComputeCanAttack()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "my_1");

            var oppField = TestFactory.MakeField();
            oppField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, [], 5000, 0, cc, new EffectRegistry());

            var attack = actions.Single(a => a.Type == ActionTypes.Attack);
            attack.SourceInstanceID.Should().Be("my_1");
            attack.ValidTargets.Should().Contain(t => t == "opp_1");
        }

        [Fact]
        public void Attack_BackendResourceCannotAttack()
        {
            // バックエンドのリソースは攻撃できない
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
            var myField = TestFactory.MakeField();
            myField.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1");

            var oppField = TestFactory.MakeField();
            oppField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, [], 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.Attack);
        }

        [Fact]
        public void Attack_FrontendObjectStorageCannotAttack()
        {
            // Object Storage をフロントエンドに置いた場合、攻撃できない
            var cc = new TestCardCache();
            cc.Add(TestFactory.DataCard(cardId: "TST-0004", subtype: "ObjectStorage"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-0004", instanceId: "os_1", maxTP: null, currentTP: null, maxYield: 400, currentYield: 400);

            var oppField = TestFactory.MakeField();
            oppField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, [], 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.Attack && a.SourceInstanceID == "os_1");
        }

        [Fact]
        public void Attack_AlreadyAttackedExcluded()
        {
            // 1ターンに1回攻撃
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
            var myField = TestFactory.MakeField();
            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "my_1");
            attacker.HasAttacked = true;
            myField.Frontend[0] = attacker;

            var oppField = TestFactory.MakeField();
            oppField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, [], 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.Attack);
        }

        [Fact]
        public void Attack_DormantExcluded()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
            var myField = TestFactory.MakeField();
            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "my_1");
            attacker.TemporaryEffects.Add(new TemporaryEffect { EffectType = BuffTypes.Dormant });
            myField.Frontend[0] = attacker;

            var oppField = TestFactory.MakeField();
            oppField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, [], 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.Attack);
        }

        [Fact]
        public void Attack_CannotAttackExcluded()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
            var myField = TestFactory.MakeField();
            var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "my_1");
            attacker.TemporaryEffects.Add(new TemporaryEffect { EffectType = BuffTypes.CannotAttack });
            myField.Frontend[0] = attacker;

            var oppField = TestFactory.MakeField();
            oppField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, [], 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.Attack);
        }

        [Fact]
        public void Attack_FaceDownTargetsExcluded()
        {
            // 裏向きカードは攻撃対象にできない
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "my_1");

            var oppField = TestFactory.MakeField();
            oppField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_1", faceUp: false);

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, [], 5000, 0, cc, new EffectRegistry());

            // No face-up targets → no attack actions
            actions.Should().NotContain(a => a.Type == ActionTypes.Attack);
        }

        [Fact]
        public void Attack_CanTargetBackendWhenNoFrontendFaceUp()
        {
            // フロントエンドに表向きリソースが0体ならバックエンドを攻撃可能
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "my_1");

            var oppField = TestFactory.MakeField();
            // Only face-down in frontend
            oppField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_fe", faceUp: false);
            oppField.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_be");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, [], 5000, 0, cc, new EffectRegistry());

            var attack = actions.Single(a => a.Type == ActionTypes.Attack);
            attack.ValidTargets.Should().Contain(t => t == "opp_be");
            attack.ValidTargets.Should().NotContain(t => t == "opp_fe");
        }

        [Fact]
        public void Attack_CannotTargetBackendWhenFrontendHasFaceUp()
        {
            // フロントエンドに表向きリソースがいればバックエンドは攻撃不可
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "my_1");

            var oppField = TestFactory.MakeField();
            oppField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_fe");
            oppField.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_be");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, [], 5000, 0, cc, new EffectRegistry());

            var attack = actions.Single(a => a.Type == ActionTypes.Attack);
            attack.ValidTargets.Should().Contain(t => t == "opp_fe");
            attack.ValidTargets.Should().NotContain(t => t == "opp_be");
        }

        [Fact]
        public void Attack_FaceDownAttackerExcluded()
        {
            // 裏向きカードは攻撃できない
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "my_1", faceUp: false);

            var oppField = TestFactory.MakeField();
            oppField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, [], 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.Attack);
        }

        [Fact]
        public void Attack_MultipleAttackersEachGetOwnAction()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "my_1");
            myField.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "my_2");

            var oppField = TestFactory.MakeField();
            oppField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, [], 5000, 0, cc, new EffectRegistry());

            actions.Count(a => a.Type == ActionTypes.Attack).Should().Be(2);
            actions.Should().Contain(a => a.Type == ActionTypes.Attack && a.SourceInstanceID == "my_1");
            actions.Should().Contain(a => a.Type == ActionTypes.Attack && a.SourceInstanceID == "my_2");
        }

        [Fact]
        public void Attack_TargetShieldedDefenderExcludedFromTargets()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var shield = TestFactory.AttachmentCard(cardId: "TST-SHIELD");
            shield.Effects = [new EffectDef { Trigger = TriggerTypes.Passive, Custom = CustomEffects.TargetShield }];
            cc.Add(shield);

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "my_1");

            var oppField = TestFactory.MakeField();
            oppField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "shielded");
            // target_shield は同フィールドに他の表向きフロントエンドが居るときだけ機能する
            oppField.Frontend[1] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "wall");
            oppField.Support[0] = new DeployedSupport
            {
                InstanceID = "shield_1",
                CardID = "TST-SHIELD",
                TargetInstanceID = "shielded",
                FaceUp = true,
            };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, oppField, [], 5000, 0, cc, new EffectRegistry());

            var attack = actions.Single(a => a.Type == ActionTypes.Attack);
            attack.ValidTargets.Should().NotContain("shielded", "target_shield で保護された対象は攻撃候補から外れる");
            attack.ValidTargets.Should().Contain("wall");
        }
    }

    /// <summary>Tests for Monetize eligibility and remaining-capacity computation.</summary>
    public class MonetizeRules : Base
    {
        [Fact]
        public void Monetize_BackendComputeIncluded()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 100, cc, new EffectRegistry());

            actions.Should().Contain(a => a.Type == ActionTypes.Monetize && a.SourceInstanceID == "be_1");
        }

        [Fact]
        public void Monetize_FrontendComputeExcluded()
        {
            // フロントエンドの Compute は収益化できない
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 100, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.Monetize);
        }

        [Fact]
        public void Monetize_BackendDatabaseExcluded()
        {
            // 収益化はComputeのみ。DatabaseはInsight生成源だがBudget変換はしない
            var cc = new TestCardCache();
            cc.Add(TestFactory.DataCard(cardId: "TST-0002"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Backend[0] = TestFactory.MakeResource(
                cardId: "TST-0002", instanceId: "db_1", maxTP: null, currentTP: null, maxYield: 400, currentYield: 400);

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 100, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.Monetize);
        }

        [Fact]
        public void Monetize_ExcludedOnFirstTurn()
        {
            // 先攻T1では収益化をスキップ
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));

            var state = TestFactory.MakeGameState(turn: 1, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 100, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.Monetize);
        }

        [Fact]
        public void Monetize_ExcludedWhenInsightPoolZero()
        {
            // InsightプールからInsightを消費して変換。0なら変換不可
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.Monetize);
        }

        [Fact]
        public void Monetize_RemainingCapacityBasedOnTP()
        {
            // 各カードの変換上限 = スループット値
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1");
            res.MonetizedAmount = 200; // 既に200使用
            myField.Backend[0] = res;

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 1000, cc, new EffectRegistry());

            var yieldAction = actions.Single(a => a.Type == ActionTypes.Monetize);
            yieldAction.RemainingCapacity.Should().Be(400); // 600 - 200 = 400
        }

        [Fact]
        public void Monetize_ExcludedWhenCapacityFull()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1");
            res.MonetizedAmount = 600; // 全容量使用済み
            myField.Backend[0] = res;

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 1000, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.Monetize);
        }

        [Fact]
        public void Monetize_FaceDownBackendComputeExcluded()
        {
            // 裏向きリソースは収益化できない（稼働していない）
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1", faceUp: false);

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 100, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.Monetize);
        }

        [Fact]
        public void Monetize_MultipleBackendComputeEachGetAction()
        {
            // 複数のバックエンドComputeがある場合、それぞれにmonetizeが生成される
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));
            cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", tp: 500, name: "Compute2"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1");
            myField.Backend[1] = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "be_2");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 1000, cc, new EffectRegistry());

            actions.Count(a => a.Type == ActionTypes.Monetize).Should().Be(2);
            var a1 = actions.Single(a => a.Type == ActionTypes.Monetize && a.SourceInstanceID == "be_1");
            var a2 = actions.Single(a => a.Type == ActionTypes.Monetize && a.SourceInstanceID == "be_2");
            a1.RemainingCapacity.Should().Be(600);
            a2.RemainingCapacity.Should().Be(500);
        }

        [Fact]
        public void Monetize_DormantBackendComputeExcluded()
        {
            // 休止リソースは収益化できない
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1");
            res.TemporaryEffects.Add(new TemporaryEffect { EffectType = BuffTypes.Dormant });
            myField.Backend[0] = res;

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 100, cc, new EffectRegistry());

            actions.Should().NotContain(a => a.Type == ActionTypes.Monetize);
        }
    }

    /// <summary>Tests for UseIgnition availability by resource/support state and effect registration.</summary>
    public class UseIgnitionRules : Base
    {
        [Fact]
        public void UseIgnition_ResourceWithEffectIncluded()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0009"));

            var registry = new EffectRegistry();
            registry.RegisterComposed("TST-0009", TriggerType.Ignition,
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(200)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0009", instanceId: "res_10");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

            actions.Should().Contain(a => a.Type == ActionTypes.UseIgnition && a.SourceInstanceID == "res_10");
        }

        [Fact]
        public void UseIgnition_ResourceWithoutEffectExcluded()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

            var registry = new EffectRegistry();
            // No effect registered for card 1

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "fe_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

            actions.Should().NotContain(a => a.Type == ActionTypes.UseIgnition);
        }

        [Fact]
        public void UseIgnition_DormantExcluded()
        {
            // 休止リソースは起動効果を使用不可
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0009"));

            var registry = new EffectRegistry();
            registry.RegisterComposed("TST-0009", TriggerType.Ignition,
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(200)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            var resource = TestFactory.MakeResource(cardId: "TST-0009", instanceId: "res_10");
            resource.TemporaryEffects.Add(new TemporaryEffect { EffectType = BuffTypes.Dormant });
            myField.Frontend[0] = resource;

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

            actions.Should().NotContain(a => a.Type == ActionTypes.UseIgnition);
        }

        [Fact]
        public void UseIgnition_EffectUsedThisTurnExcluded()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0009"));

            var registry = new EffectRegistry();
            registry.RegisterComposed("TST-0009", TriggerType.Ignition,
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(200)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            var res = TestFactory.MakeResource(cardId: "TST-0009", instanceId: "res_10");
            res.EffectUsedThisTurn = true;
            myField.Frontend[0] = res;

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

            actions.Should().NotContain(a => a.Type == ActionTypes.UseIgnition);
        }

        [Fact]
        public void UseIgnition_SupportWithEffectIncluded()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200"));

            var registry = new EffectRegistry();
            registry.RegisterComposed("TEST-0200", TriggerType.Ignition,
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(100)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TEST-0200",
                DeployingTurnsLeft = 0
            };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

            actions.Should().Contain(a => a.Type == ActionTypes.UseIgnition && a.SourceInstanceID == "sup_1");
        }

        [Fact]
        public void UseIgnition_DeployingSupportExcluded()
        {
            // デプロイ中のカードは稼働していない
            var cc = new TestCardCache();
            cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200"));

            var registry = new EffectRegistry();
            registry.RegisterComposed("TEST-0200", TriggerType.Ignition,
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(100)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TEST-0200",
                DeployingTurnsLeft = 1
            };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

            actions.Should().NotContain(a => a.Type == ActionTypes.UseIgnition);
        }

        [Fact]
        public void UseIgnition_SupportEffectUsedThisTurnExcluded()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200"));

            var registry = new EffectRegistry();
            registry.RegisterComposed("TEST-0200", TriggerType.Ignition,
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(100)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TEST-0200",
                DeployingTurnsLeft = 0,
                EffectUsedThisTurn = true
            };

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

            actions.Should().NotContain(a => a.Type == ActionTypes.UseIgnition);
        }

        [Fact]
        public void UseIgnition_AvailableInBattlePhase()
        {
            // 効果発動はバトルフェーズでも可能
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0009"));

            var registry = new EffectRegistry();
            registry.RegisterComposed("TST-0009", TriggerType.Ignition,
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(200)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0009", instanceId: "res_10");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

            actions.Should().Contain(a => a.Type == ActionTypes.UseIgnition && a.SourceInstanceID == "res_10");
        }

        [Fact]
        public void UseIgnition_FaceDownResourceExcluded()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0009"));

            var registry = new EffectRegistry();
            registry.RegisterComposed("TST-0009", TriggerType.Ignition,
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(200)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0009", instanceId: "res_10", faceUp: false);

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

            actions.Should().NotContain(a => a.Type == ActionTypes.UseIgnition);
        }
    }

    /// <summary>Tests for budget-gated UseIgnition availability via min/max budget guards.</summary>
    public class UseIgnitionBudgetFiltering : Base
    {
        [Fact]
        public void UseIgnition_ExcludedWhenBudgetBelowMinimum()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0009", tp: 600, av: 1400));

            var registry = new EffectRegistry();
            registry.RegisterComposed("TST-0009", TriggerType.Ignition,
                [new MinBudgetGuard(400)],
                new LoseBudgetOp(PlayerRef.Myself, new StaticAmount(400)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 300);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0009", instanceId: "res_10");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 300, 0, cc, registry);

            actions.Should().NotContain(a => a.Type == ActionTypes.UseIgnition && a.SourceInstanceID == "res_10");
        }

        [Fact]
        public void UseIgnition_IncludedWhenBudgetMeetsMinimum()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0009", tp: 600, av: 1400));

            var registry = new EffectRegistry();
            registry.RegisterComposed("TST-0009", TriggerType.Ignition,
                [new MinBudgetGuard(400)],
                new LoseBudgetOp(PlayerRef.Myself, new StaticAmount(400)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 500);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0009", instanceId: "res_10");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 500, 0, cc, registry);

            actions.Should().Contain(a => a.Type == ActionTypes.UseIgnition && a.SourceInstanceID == "res_10");
        }

        [Fact]
        public void UseIgnition_ExcludedWhenBudgetExceedsMaximum()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0010", tp: 600, av: 1400));

            var registry = new EffectRegistry();
            registry.RegisterComposed("TST-0010", TriggerType.Ignition,
                [new MaxBudgetGuard(1000)],
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(900)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 1500);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0010", instanceId: "res_120");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 1500, 0, cc, registry);

            actions.Should().NotContain(a => a.Type == ActionTypes.UseIgnition && a.SourceInstanceID == "res_120");
        }

        [Fact]
        public void UseIgnition_IncludedWhenBudgetWithinMaximum()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0010", tp: 600, av: 1400));

            var registry = new EffectRegistry();
            registry.RegisterComposed("TST-0010", TriggerType.Ignition,
                [new MaxBudgetGuard(1000)],
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(900)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 800);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0010", instanceId: "res_120");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 800, 0, cc, registry);

            actions.Should().Contain(a => a.Type == ActionTypes.UseIgnition && a.SourceInstanceID == "res_120");
        }

        [Fact]
        public void UseIgnition_IncludedWhenNoBudgetRequirement()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0011", tp: 600, av: 1400));

            var registry = new EffectRegistry();
            registry.RegisterComposed("TST-0011", TriggerType.Ignition,
                new GainBudgetOp(PlayerRef.Myself, new StaticAmount(200)));

            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 0);
            var myField = TestFactory.MakeField();
            myField.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0011", instanceId: "res_30");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), [], 0, 0, cc, registry);

            actions.Should().Contain(a => a.Type == ActionTypes.UseIgnition && a.SourceInstanceID == "res_30");
        }
    }

    /// <summary>Tests for turn-control information (phase ending and required discards).</summary>
    public class TurnControls : Base
    {
        [Fact]
        public void TurnControls_MainPhaseCanEndPhase()
        {
            var state = TestFactory.MakeGameState(phase: Phase.Main);
            var controls = AvailableActions.ComputeTurnControls(state, []);
            controls.CanEndPhase.Should().BeTrue();
            controls.DiscardRequired.Should().Be(0);
        }

        [Fact]
        public void TurnControls_BattlePhaseCanEndPhase()
        {
            var state = TestFactory.MakeGameState(phase: Phase.Battle);
            var controls = AvailableActions.ComputeTurnControls(state, []);
            controls.CanEndPhase.Should().BeTrue();
        }

        [Fact]
        public void TurnControls_EndPhaseRequiresDiscardWhenOverLimit()
        {
            // 手札上限6枚、超過分を捨てる
            var state = TestFactory.MakeGameState(phase: Phase.End);
            var hand = Enumerable.Range(0, 8)
                .Select(i => new UndeployedCard { InstanceID = $"h_{i}", CardID = "TST-0001" })
                .ToList();

            var controls = AvailableActions.ComputeTurnControls(state, hand);
            controls.DiscardRequired.Should().Be(2); // 8 - 6 = 2
        }

        [Fact]
        public void TurnControls_EndPhaseNoDiscardWhenWithinLimit()
        {
            var state = TestFactory.MakeGameState(phase: Phase.End);
            var hand = Enumerable.Range(0, 5)
                .Select(i => new UndeployedCard { InstanceID = $"h_{i}", CardID = "TST-0001" })
                .ToList();

            var controls = AvailableActions.ComputeTurnControls(state, hand);
            controls.DiscardRequired.Should().Be(0);
        }

        [Theory]
        [InlineData(Phase.Draw)]
        [InlineData(Phase.End)]
        public void TurnControls_CannotEndPhaseOutsideMainAndBattle(Phase phase)
        {
            var state = TestFactory.MakeGameState(phase: phase);
            var controls = AvailableActions.ComputeTurnControls(state, []);
            controls.CanEndPhase.Should().BeFalse();
        }
    }

    /// <summary>Tests enumeration of ResolvePendingChoice actions while a choice is pending.</summary>
    public class ResolvePendingChoiceEnumeration : Base
    {
        private static PendingEffectChoice Pending(string choiceKind, params string[] candidates) =>
            new()
            {
                ChooserPlayerNum = 1,
                OwnerPlayerNum = 1,
                EffectCardId = "TST-EFFECT",
                EffectInstanceId = "eff_inst",
                Trigger = TriggerType.OnDestroy,
                ChoiceKey = "instanceId",
                ChoiceKind = choiceKind,
                Candidates = [.. candidates],
            };

        [Fact]
        public void PendingChoice_EnumeratesOneActionPerCandidate()
        {
            var cc = new TestCardCache();
            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            state.PendingEffectChoice = Pending(ChoiceKinds.FieldTarget, "c1", "c2", "c3");

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            actions.Should().HaveCount(3);
            actions.Should().OnlyContain(a => a.Type == ActionTypes.ResolvePendingChoice);
            actions.Should().OnlyContain(a => a.SourceInstanceID == "eff_inst");
        }

        [Fact]
        public void PendingChoice_FieldTarget_PutsCandidateInValidTargets()
        {
            var cc = new TestCardCache();
            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            state.PendingEffectChoice = Pending(ChoiceKinds.FieldTarget, "target_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            var action = actions.Should().ContainSingle().Subject;
            action.CardID.Should().Be("TST-EFFECT");
            action.ValidTargets.Should().BeEquivalentTo(["target_1"]);
        }

        [Fact]
        public void PendingChoice_HandCard_PutsCandidateInCardId()
        {
            var cc = new TestCardCache();
            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            state.PendingEffectChoice = Pending(ChoiceKinds.HandCard, "hand_card_1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, TestFactory.MakeField(), TestFactory.MakeField(), [], 5000, 0, cc, new EffectRegistry());

            var action = actions.Should().ContainSingle().Subject;
            action.CardID.Should().Be("hand_card_1");
            action.ValidTargets.Should().BeNull();
        }

        [Fact]
        public void PendingChoice_ShortCircuitsOtherActions()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
            var myField = TestFactory.MakeField();
            myField.Backend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1");
            var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "TST-0001" } };
            state.PendingEffectChoice = Pending(ChoiceKinds.FieldTarget, "c1");

            var actions = AvailableActions.GetAllAvailableActions(
                state, myField, TestFactory.MakeField(), hand, 5000, 100, cc, new EffectRegistry());

            actions.Should().OnlyContain(a => a.Type == ActionTypes.ResolvePendingChoice);
            actions.Should().NotContain(a => a.Type == ActionTypes.PlayCard || a.Type == ActionTypes.Monetize);
        }
    }
}
