using System.Linq;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class AvailableActionsTests
{
    // ═══════════════════════════════════════════════════════════════
    //  Phase gating — Main vs Battle
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MainPhase_ReturnsPlayScaleDistributeActivateMigrateActions()
    {
        var cc = new TestCardCache();
        var compute = TestFactory.ComputeCard(cardId: "SH-0001");
        var db = TestFactory.DataCard(cardId: "NT-0009");
        cc.Add(compute);
        cc.Add(db);

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 5000);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1");
        myField.Backend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1");

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "SH-0001" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), hand, 5000, 100, cc, null);

        // Main phase should include play_card, scale_up, monetize, migrate
        actions.Should().Contain(a => a.Type == WireActionTypes.PlayCard);
        actions.Should().Contain(a => a.Type == WireActionTypes.ScaleUp);
        actions.Should().Contain(a => a.Type == WireActionTypes.Monetize);
        actions.Should().Contain(a => a.Type == WireActionTypes.Migrate);
        // Main phase should NOT include attack
        actions.Should().NotContain(a => a.Type == WireActionTypes.Attack);
    }

    [Fact]
    public void BattlePhase_ReturnsAttackAndActivateOnly()
    {
        var cc = new TestCardCache();
        var compute = TestFactory.ComputeCard(cardId: "SH-0001");
        cc.Add(compute);

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle, p1Budget: 5000);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1");

        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, [], 5000, 0, cc, null);

        actions.Should().Contain(a => a.Type == WireActionTypes.Attack);
        // Battle phase should NOT include play_card, scale_up, monetize, migrate
        actions.Should().NotContain(a => a.Type == WireActionTypes.PlayCard);
        actions.Should().NotContain(a => a.Type == WireActionTypes.ScaleUp);
        actions.Should().NotContain(a => a.Type == WireActionTypes.Monetize);
        actions.Should().NotContain(a => a.Type == WireActionTypes.Migrate);
    }

    // ═══════════════════════════════════════════════════════════════
    //  PlayCard — ゾーン配置ルール
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void PlayCard_ComputeCanGoToFrontendAndBackend()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 0));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "SH-0001" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_1");
        playAction.ValidZones.Should().Contain(z => z.StartsWith("frontend_"));
        playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
    }

    [Fact]
    public void PlayCard_DatabaseCanOnlyGoToBackend()
    {
        // Database は Backend のみ
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "NT-0009", cardType: CardTypes.Database));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var hand = new List<UndeployedCard> { new() { InstanceID = "h_db", CardID = "NT-0009" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_db");
        playAction.ValidZones.Should().NotContain(z => z.StartsWith("frontend_"));
        playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
    }

    [Fact]
    public void PlayCard_CacheDBCanOnlyGoToBackend()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "NT-0010", cardType: CardTypes.CacheDB));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var hand = new List<UndeployedCard> { new() { InstanceID = "h_cache", CardID = "NT-0010" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_cache");
        playAction.ValidZones.Should().NotContain(z => z.StartsWith("frontend_"));
        playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
    }

    [Fact]
    public void PlayCard_ObjectStorageCanGoToFrontendAndBackend()
    {
        // ObjectStorage は Frontend / Backend 両方
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "NT-0011", cardType: CardTypes.ObjectStorage));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var hand = new List<UndeployedCard> { new() { InstanceID = "h_os", CardID = "NT-0011" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_os");
        playAction.ValidZones.Should().Contain(z => z.StartsWith("frontend_"));
        playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
    }

    [Fact]
    public void PlayCard_AiMlCanGoToFrontendAndBackend()
    {
        // AI/ML は Frontend / Backend 両方
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0004", cardType: CardTypes.AiMl, deployTurns: 0));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var hand = new List<UndeployedCard> { new() { InstanceID = "h_ai", CardID = "SH-0004" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_ai");
        playAction.ValidZones.Should().Contain(z => z.StartsWith("frontend_"));
        playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
    }

    [Fact]
    public void PlayCard_StrategyDoesNotRequireZone()
    {
        var cc = new TestCardCache();
        cc.Add(new CardDefinition { CardId = "SL-0004", CardName = "S", CardType = CardTypes.Strategy });

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var hand = new List<UndeployedCard> { new() { InstanceID = "h_s", CardID = "SL-0004" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_s");
        playAction.ValidZones.Should().BeNullOrEmpty();
    }

    [Fact]
    public void PlayCard_ReactiveGoesToSupportZone()
    {
        var cc = new TestCardCache();
        cc.Add(new CardDefinition { CardId = "SL-0006", CardName = "R", CardType = CardTypes.Reactive });

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var hand = new List<UndeployedCard> { new() { InstanceID = "h_r", CardID = "SL-0006" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_r");
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
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_p");
        playAction.ValidZones.Should().AllSatisfy(z => z.Should().StartWith("support_"));
    }

    // ─── PlayCard: slot capacity ────────────────────────────────

    [Fact]
    public void PlayCard_ExcludedWhenAllSlotsOccupied()
    {
        // 各ゾーン上限3体、スロットが埋まっていれば配置不可
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 0));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        // Fill all frontend and backend slots
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_0");
        myField.Frontend[1] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1");
        myField.Frontend[2] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_2");
        myField.Backend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_0");
        myField.Backend[1] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1");
        myField.Backend[2] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_2");

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "SH-0001" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_1");
    }

    [Fact]
    public void PlayCard_StrategyAvailableEvenWhenAllSupportSlotsOccupied()
    {
        var cc = new TestCardCache();
        cc.Add(new CardDefinition { CardId = "SL-0004", CardName = "S", CardType = CardTypes.Strategy });

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Support[0] = new DeployedSupport { InstanceID = "s_0", CardID = "TEST-0200" };
        myField.Support[1] = new DeployedSupport { InstanceID = "s_1", CardID = "TEST-0200" };
        myField.Support[2] = new DeployedSupport { InstanceID = "s_2", CardID = "TEST-0200" };

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_s", CardID = "SL-0004" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, null);

        actions.Should().Contain(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_s");
    }

    [Fact]
    public void PlayCard_OnlyOffersEmptySlots()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 0));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_0");
        // Frontend slots 1,2 are empty; all backend slots are empty

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "SH-0001" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_1");
        playAction.ValidZones.Should().NotContain(z => z == "frontend_0");
        playAction.ValidZones.Should().Contain(z => z == "frontend_1");
        playAction.ValidZones.Should().Contain(z => z == "frontend_2");
    }

    // ─── PlayCard: Incident rules ───────────────────────────────

    [Fact]
    public void PlayCard_IncidentExcludedOnFirstTurn()
    {
        // 先攻T1ではインシデント使用不可
        var cc = new TestCardCache();
        cc.Add(new CardDefinition { CardId = "SL-0015", CardName = "I", CardType = CardTypes.Incident });

        var state = TestFactory.MakeGameState(turn: 1, phase: Phase.Main);
        var hand = new List<UndeployedCard> { new() { InstanceID = "h_i", CardID = "SL-0015" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_i");
    }

    [Fact]
    public void PlayCard_IncidentAllowedOnLaterTurns()
    {
        var cc = new TestCardCache();
        cc.Add(new CardDefinition { CardId = "SL-0015", CardName = "I", CardType = CardTypes.Incident });

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var hand = new List<UndeployedCard> { new() { InstanceID = "h_i", CardID = "SL-0015" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, null);

        actions.Should().Contain(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_i");
    }

    [Fact]
    public void PlayCard_IncidentExcludedWhenAlreadyPlayedThisTurn()
    {
        // インシデントは1ターン1枚まで
        var cc = new TestCardCache();
        cc.Add(new CardDefinition { CardId = "SL-0015", CardName = "I", CardType = CardTypes.Incident });

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        state.Player1IncidentPlayedThisTurn = true;
        var myField = TestFactory.MakeField();

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_i", CardID = "SL-0015" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_i");
    }

    // ─── PlayCard: Attachment ───────────────────────────────────

    [Fact]
    public void PlayCard_AttachmentTargetsFaceUpResources()
    {
        // Attachment はリソースに装備。ValidTargets を返す
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));
        cc.Add(TestFactory.AttachmentCard(cardId: "TEST-0300"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1");

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_att", CardID = "TEST-0300" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_att");
        playAction.ValidTargets.Should().Contain(t => t == "fe_1");
        playAction.ValidZones.Should().NotBeEmpty();
        playAction.ValidZones.Should().AllSatisfy(z => z.Should().StartWith("support_"));
    }

    [Fact]
    public void PlayCard_AttachmentUnavailableWhenSupportZoneFull()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));
        cc.Add(TestFactory.AttachmentCard(cardId: "TEST-0300"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1");
        myField.Support[0] = new DeployedSupport { InstanceID = "s_0", CardID = "TEST-0300", TargetInstanceID = "fe_1" };
        myField.Support[1] = new DeployedSupport { InstanceID = "s_1", CardID = "TEST-0300", TargetInstanceID = "fe_1" };
        myField.Support[2] = new DeployedSupport { InstanceID = "s_2", CardID = "TEST-0300", TargetInstanceID = "fe_1" };

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_att", CardID = "TEST-0300" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_att");
    }

    [Fact]
    public void PlayCard_AttachmentExcludesFaceDownResources()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));
        cc.Add(TestFactory.AttachmentCard(cardId: "TEST-0300"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1", faceUp: false);

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_att", CardID = "TEST-0300" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_att");
    }

    [Fact]
    public void PlayCard_EmptyHandReturnsNoPlayActions()
    {
        var cc = new TestCardCache();
        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), [], 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.PlayCard);
    }

    // ═══════════════════════════════════════════════════════════════
    //  ScaleUp — ランクアップルール
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ScaleUp_ResizableSmallOffersAllOptions()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", resizable: true));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1", rank: Rank.Small);

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        var scaleActions = actions.Where(a => a.Type == WireActionTypes.ScaleUp && a.SourceInstanceID == "fe_1").ToList();
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
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", resizable: true));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "fe_1", rank: Rank.Medium, family: InstanceFamily.M);

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        var scaleActions = actions.Where(a => a.Type == WireActionTypes.ScaleUp && a.SourceInstanceID == "fe_1").ToList();
        // Medium(M) can only scale up to Large with same family = 1 option
        scaleActions.Should().HaveCount(1);
        scaleActions.Should().Contain(a => a.TargetRank == "large" && a.InstanceFamily == "M");
    }

    [Fact]
    public void ScaleUp_ResizableLargeHasNoOptions()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", resizable: true));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "fe_1", rank: Rank.Large, family: InstanceFamily.M);

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        var scaleActions = actions.Where(a => a.Type == WireActionTypes.ScaleUp && a.SourceInstanceID == "fe_1").ToList();
        // Large is max rank — no scale up options
        scaleActions.Should().BeEmpty();
    }

    [Fact]
    public void ScaleUp_NonResizableExcluded()
    {
        // Resizable でないカードはスケールアップ不可
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", resizable: false));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.ScaleUp);
    }

    [Fact]
    public void ScaleUp_ElasticOnlyExcluded()
    {
        // Elastic-only カード（Resizable=false）は手動スケールアップ不可
        var cc = new TestCardCache();
        cc.Add(TestFactory.ElasticContainerCard(cardId: "TEST-0002"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0002", instanceId: "fe_2");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.ScaleUp);
    }

    [Fact]
    public void ScaleUp_FaceDownResourceExcluded()
    {
        // 裏向きカードは「いないものとみなす」
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", resizable: true));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1", faceUp: false);

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.ScaleUp);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Attack — バトルルール
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Attack_FrontendComputeCanAttack()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "my_1");

        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, [], 5000, 0, cc, null);

        var attack = actions.Single(a => a.Type == WireActionTypes.Attack);
        attack.SourceInstanceID.Should().Be("my_1");
        attack.ValidTargets.Should().Contain(t => t == "opp_1");
    }

    [Fact]
    public void Attack_BackendResourceCannotAttack()
    {
        // バックエンドのリソースは攻撃できない
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        var myField = TestFactory.MakeField();
        myField.Backend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1");

        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, [], 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Attack);
    }

    [Fact]
    public void Attack_FrontendObjectStorageCannotAttack()
    {
        // Object Storage をフロントエンドに置いた場合、攻撃できない
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "NT-0011", cardType: CardTypes.ObjectStorage));
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(
            cardId: "NT-0011", instanceId: "os_1", maxTP: null, currentTP: null, maxYield: 400, currentYield: 400);

        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, [], 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Attack && a.SourceInstanceID == "os_1");
    }

    [Fact]
    public void Attack_AlreadyAttackedExcluded()
    {
        // 1ターンに1回攻撃
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        var myField = TestFactory.MakeField();
        var attacker = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "my_1");
        attacker.HasAttacked = true;
        myField.Frontend[0] = attacker;

        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, [], 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Attack);
    }

    [Fact]
    public void Attack_CannotOperateExcluded()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        var myField = TestFactory.MakeField();
        var attacker = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "my_1");
        attacker.TemporaryEffects.Add(new TemporaryEffect { EffectType = EffectTypes.CannotOperate });
        myField.Frontend[0] = attacker;

        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, [], 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Attack);
    }

    [Fact]
    public void Attack_FaceDownTargetsExcluded()
    {
        // 裏向きカードは攻撃対象にできない
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "my_1");

        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_1", faceUp: false);

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, [], 5000, 0, cc, null);

        // No face-up targets → no attack actions
        actions.Should().NotContain(a => a.Type == WireActionTypes.Attack);
    }

    [Fact]
    public void Attack_CanTargetBackendWhenNoFrontendFaceUp()
    {
        // フロントエンドに表向きリソースが0体ならバックエンドを攻撃可能
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "my_1");

        var oppField = TestFactory.MakeField();
        // Only face-down in frontend
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_fe", faceUp: false);
        oppField.Backend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_be");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, [], 5000, 0, cc, null);

        var attack = actions.Single(a => a.Type == WireActionTypes.Attack);
        attack.ValidTargets.Should().Contain(t => t == "opp_be");
        attack.ValidTargets.Should().NotContain(t => t == "opp_fe");
    }

    [Fact]
    public void Attack_CannotTargetBackendWhenFrontendHasFaceUp()
    {
        // フロントエンドに表向きリソースがいればバックエンドは攻撃不可
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "my_1");

        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_fe");
        oppField.Backend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_be");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, [], 5000, 0, cc, null);

        var attack = actions.Single(a => a.Type == WireActionTypes.Attack);
        attack.ValidTargets.Should().Contain(t => t == "opp_fe");
        attack.ValidTargets.Should().NotContain(t => t == "opp_be");
    }

    [Fact]
    public void Attack_MigratingResourceCannotAttack()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        var myField = TestFactory.MakeField();
        var attacker = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "my_1");
        attacker.MigrationTarget = "some_target";
        myField.Frontend[0] = attacker;

        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, [], 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Attack);
    }

    [Fact]
    public void Attack_FaceDownAttackerExcluded()
    {
        // 裏向きカードは攻撃できない
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "my_1", faceUp: false);

        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, [], 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Attack);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Monetize — 収益化ルール
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Monetize_BackendComputeIncluded()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Backend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 100, cc, null);

        actions.Should().Contain(a => a.Type == WireActionTypes.Monetize && a.SourceInstanceID == "be_1");
    }

    [Fact]
    public void Monetize_FrontendComputeExcluded()
    {
        // フロントエンドの Compute は収益化できない
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 100, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Monetize);
    }

    [Fact]
    public void Monetize_BackendDatabaseExcluded()
    {
        // 収益化はComputeのみ。DatabaseはInsight生成源だがBudget変換はしない
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "NT-0009"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Backend[0] = TestFactory.MakeResource(
            cardId: "NT-0009", instanceId: "db_1", maxTP: null, currentTP: null, maxYield: 400, currentYield: 400);

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 100, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Monetize);
    }

    [Fact]
    public void Monetize_ExcludedOnFirstTurn()
    {
        // 先攻T1では収益化をスキップ
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 0));

        var state = TestFactory.MakeGameState(turn: 1, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Backend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 100, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Monetize);
    }

    [Fact]
    public void Monetize_ExcludedWhenInsightPoolZero()
    {
        // InsightプールからInsightを消費して変換。0なら変換不可
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Backend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Monetize);
    }

    [Fact]
    public void Monetize_RemainingCapacityBasedOnTP()
    {
        // 各カードの変換上限 = スループット値
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", tp: 600));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        var res = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1");
        res.MonetizedAmount = 200; // 既に200使用
        myField.Backend[0] = res;

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 1000, cc, null);

        var yieldAction = actions.Single(a => a.Type == WireActionTypes.Monetize);
        yieldAction.RemainingCapacity.Should().Be(400); // 600 - 200 = 400
    }

    [Fact]
    public void Monetize_ExcludedWhenCapacityFull()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", tp: 600));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        var res = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1");
        res.MonetizedAmount = 600; // 全容量使用済み
        myField.Backend[0] = res;

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 1000, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Monetize);
    }

    [Fact]
    public void Monetize_MigratingResourceExcluded()
    {
        // マイグレーション中のリソースはロック状態（収益化不可）
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        var res = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1");
        res.MigratingFrom = "some_source";
        myField.Backend[0] = res;

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 100, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Monetize);
    }

    // ═══════════════════════════════════════════════════════════════
    //  UseEffect — 効果発動ルール
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void UseEffect_ResourceWithEffectIncluded()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0009"));

        var registry = new EffectRegistry();
        registry.RegisterComposed("SH-0009", TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(200)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0009", instanceId: "res_10");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

        actions.Should().Contain(a => a.Type == WireActionTypes.UseEffect && a.SourceInstanceID == "res_10");
    }

    [Fact]
    public void UseEffect_ResourceWithoutEffectExcluded()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var registry = new EffectRegistry();
        // No effect registered for card 1

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

        actions.Should().NotContain(a => a.Type == WireActionTypes.UseEffect);
    }

    [Fact]
    public void UseEffect_EffectUsedThisTurnExcluded()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0009"));

        var registry = new EffectRegistry();
        registry.RegisterComposed("SH-0009", TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(200)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        var res = TestFactory.MakeResource(cardId: "SH-0009", instanceId: "res_10");
        res.EffectUsedThisTurn = true;
        myField.Frontend[0] = res;

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

        actions.Should().NotContain(a => a.Type == WireActionTypes.UseEffect);
    }

    [Fact]
    public void UseEffect_CannotOperateExcluded()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0009"));

        var registry = new EffectRegistry();
        registry.RegisterComposed("SH-0009", TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(200)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        var res = TestFactory.MakeResource(cardId: "SH-0009", instanceId: "res_10");
        res.TemporaryEffects.Add(new TemporaryEffect { EffectType = EffectTypes.CannotOperate });
        myField.Frontend[0] = res;

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

        actions.Should().NotContain(a => a.Type == WireActionTypes.UseEffect);
    }

    [Fact]
    public void UseEffect_MigratingResourceExcluded()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0009"));

        var registry = new EffectRegistry();
        registry.RegisterComposed("SH-0009", TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(200)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        var res = TestFactory.MakeResource(cardId: "SH-0009", instanceId: "res_10");
        res.MigrationTarget = "some_target";
        myField.Frontend[0] = res;

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

        actions.Should().NotContain(a => a.Type == WireActionTypes.UseEffect);
    }

    [Fact]
    public void UseEffect_SupportWithEffectIncluded()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200"));

        var registry = new EffectRegistry();
        registry.RegisterComposed("TEST-0200", TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(100)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Support[0] = new DeployedSupport
        {
            InstanceID = "sup_1", CardID = "TEST-0200", DeployingTurnsLeft = 0
        };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

        actions.Should().Contain(a => a.Type == WireActionTypes.UseEffect && a.SourceInstanceID == "sup_1");
    }

    [Fact]
    public void UseEffect_DeployingSupportExcluded()
    {
        // デプロイ中のカードは稼働していない
        var cc = new TestCardCache();
        cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200"));

        var registry = new EffectRegistry();
        registry.RegisterComposed("TEST-0200", TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(100)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Support[0] = new DeployedSupport
        {
            InstanceID = "sup_1", CardID = "TEST-0200", DeployingTurnsLeft = 1
        };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

        actions.Should().NotContain(a => a.Type == WireActionTypes.UseEffect);
    }

    [Fact]
    public void UseEffect_SupportEffectUsedThisTurnExcluded()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.PlatformCard(cardId: "TEST-0200"));

        var registry = new EffectRegistry();
        registry.RegisterComposed("TEST-0200", TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(100)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Support[0] = new DeployedSupport
        {
            InstanceID = "sup_1", CardID = "TEST-0200", DeployingTurnsLeft = 0, EffectUsedThisTurn = true
        };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

        actions.Should().NotContain(a => a.Type == WireActionTypes.UseEffect);
    }

    [Fact]
    public void UseEffect_AvailableInBattlePhase()
    {
        // エフェクト発動はバトルフェーズでも可能
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0009"));

        var registry = new EffectRegistry();
        registry.RegisterComposed("SH-0009", TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(200)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0009", instanceId: "res_10");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

        actions.Should().Contain(a => a.Type == WireActionTypes.UseEffect && a.SourceInstanceID == "res_10");
    }

    // ─── UseEffect: budget filtering (既存テスト) ───────────

    [Fact]
    public void UseEffect_ExcludedWhenBudgetBelowMinimum()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0009", tp: 600, av: 1400));

        var registry = new EffectRegistry();
        registry.RegisterComposed("SH-0009", TriggerType.Activate,
            new RequireBudgetOp(400),
            new LoseBudgetOp(PlayerRef.Self, new StaticAmount(400)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 300);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0009", instanceId: "res_10");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 300, 0, cc, registry);

        actions.Should().NotContain(a => a.Type == WireActionTypes.UseEffect && a.SourceInstanceID == "res_10");
    }

    [Fact]
    public void UseEffect_IncludedWhenBudgetMeetsMinimum()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0009", tp: 600, av: 1400));

        var registry = new EffectRegistry();
        registry.RegisterComposed("SH-0009", TriggerType.Activate,
            new RequireBudgetOp(400),
            new LoseBudgetOp(PlayerRef.Self, new StaticAmount(400)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 500);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0009", instanceId: "res_10");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 500, 0, cc, registry);

        actions.Should().Contain(a => a.Type == WireActionTypes.UseEffect && a.SourceInstanceID == "res_10");
    }

    [Fact]
    public void UseEffect_ExcludedWhenBudgetExceedsMaximum()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "NT-0026", tp: 600, av: 1400));

        var registry = new EffectRegistry();
        registry.RegisterComposed("NT-0026", TriggerType.Activate,
            new RequireMaxBudgetOp(1000),
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(900)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 1500);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "NT-0026", instanceId: "res_120");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 1500, 0, cc, registry);

        actions.Should().NotContain(a => a.Type == WireActionTypes.UseEffect && a.SourceInstanceID == "res_120");
    }

    [Fact]
    public void UseEffect_IncludedWhenBudgetWithinMaximum()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "NT-0026", tp: 600, av: 1400));

        var registry = new EffectRegistry();
        registry.RegisterComposed("NT-0026", TriggerType.Activate,
            new RequireMaxBudgetOp(1000),
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(900)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 800);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "NT-0026", instanceId: "res_120");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 800, 0, cc, registry);

        actions.Should().Contain(a => a.Type == WireActionTypes.UseEffect && a.SourceInstanceID == "res_120");
    }

    [Fact]
    public void UseEffect_IncludedWhenNoBudgetRequirement()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "TK-0008", tp: 600, av: 1400));

        var registry = new EffectRegistry();
        registry.RegisterComposed("TK-0008", TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(200)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 0);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "TK-0008", instanceId: "res_30");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 0, 0, cc, registry);

        actions.Should().Contain(a => a.Type == WireActionTypes.UseEffect && a.SourceInstanceID == "res_30");
    }

    // ─── PlayCard: budget filtering for Strategy/Incident ───────

    [Fact]
    public void PlayStrategy_ExcludedWhenBudgetInsufficient()
    {
        var cc = new TestCardCache();
        cc.Add(new CardDefinition { CardId = "SL-0004", CardName = "S", CardType = CardTypes.Strategy });

        var registry = new EffectRegistry();
        registry.RegisterComposed("SL-0004", TriggerType.Activate,
            new RequireBudgetOp(500),
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(1000)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 200);
        var hand = new List<UndeployedCard> { new() { InstanceID = "hand_50", CardID = "SL-0004" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 200, 0, cc, registry);

        actions.Should().NotContain(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "hand_50");
    }

    [Fact]
    public void PlayStrategy_IncludedWhenBudgetSufficient()
    {
        var cc = new TestCardCache();
        cc.Add(new CardDefinition { CardId = "SL-0004", CardName = "S", CardType = CardTypes.Strategy });

        var registry = new EffectRegistry();
        registry.RegisterComposed("SL-0004", TriggerType.Activate,
            new RequireBudgetOp(500),
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(1000)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 600);
        var hand = new List<UndeployedCard> { new() { InstanceID = "hand_50", CardID = "SL-0004" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 600, 0, cc, registry);

        actions.Should().Contain(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "hand_50");
    }

    [Fact]
    public void PlayIncident_ExcludedWhenBudgetInsufficient()
    {
        var cc = new TestCardCache();
        cc.Add(new CardDefinition { CardId = "SL-0015", CardName = "I", CardType = CardTypes.Incident });

        var registry = new EffectRegistry();
        registry.RegisterComposed("SL-0015", TriggerType.Activate,
            new RequireBudgetOp(300),
            new LoseBudgetOp(PlayerRef.Opponent, new StaticAmount(300)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, p1Budget: 100);
        var hand = new List<UndeployedCard> { new() { InstanceID = "hand_60", CardID = "SL-0015" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 100, 0, cc, registry);

        actions.Should().NotContain(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "hand_60");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Migrate — マイグレーションルール
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Migrate_FaceUpResourcesCanMigrate()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 1));
        cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", deployTurns: 1, name: "Compute2"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "res_1");
        myField.Frontend[1] = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "res_2");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        actions.Should().Contain(a => a.Type == WireActionTypes.Migrate && a.SourceInstanceID == "res_1");
        actions.Should().Contain(a => a.Type == WireActionTypes.Migrate && a.SourceInstanceID == "res_2");
    }

    [Fact]
    public void Migrate_TargetDeployTurnsMustBeGreaterOrEqual()
    {
        // 新リソースのデプロイターン >= 旧リソースのデプロイターン
        var cc = new TestCardCache();
        var orchestrator = TestFactory.ComputeCard(cardId: "SH-0003", cardType: CardTypes.Orchestrator, deployTurns: 2);
        var serverless = TestFactory.ServerlessCard(cardId: "SH-0002"); // deployTurns: 0
        cc.Add(orchestrator);
        cc.Add(serverless);

        var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0003", instanceId: "orch_1");
        myField.Frontend[1] = TestFactory.MakeResource(cardId: "SH-0002", instanceId: "sless_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        // Orchestrator (DT=2) can migrate to Orchestrator (DT=2) — equal is OK, but can't migrate to itself
        // Serverless (DT=0) → Orchestrator (DT=2): OK (target >= source)
        var serverlessMigrate = actions.FirstOrDefault(
            a => a.Type == WireActionTypes.Migrate && a.SourceInstanceID == "sless_1");
        serverlessMigrate.Should().NotBeNull();
        serverlessMigrate!.ValidTargets.Should().Contain(t => t == "orch_1");

        // Orchestrator (DT=2) → Serverless (DT=0): NG (target < source)
        var orchMigrate = actions.FirstOrDefault(
            a => a.Type == WireActionTypes.Migrate && a.SourceInstanceID == "orch_1");
        if (orchMigrate is not null)
        {
            orchMigrate.ValidTargets.Should().NotContain(t => t == "sless_1");
        }
    }

    [Fact]
    public void Migrate_FaceDownResourcesExcluded()
    {
        // 表向き（稼働中）であること
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 1));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "res_1", faceUp: false);
        myField.Frontend[1] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "res_2");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        // Face-down resource is not a source
        actions.Should().NotContain(a => a.Type == WireActionTypes.Migrate && a.SourceInstanceID == "res_1");
        // Face-down resource is not a valid target
        var migrate = actions.FirstOrDefault(a => a.Type == WireActionTypes.Migrate && a.SourceInstanceID == "res_2");
        if (migrate is not null)
        {
            migrate.ValidTargets.Should().NotContain(t => t == "res_1");
        }
    }

    [Fact]
    public void Migrate_AlreadyMigratingExcluded()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 1));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        var res1 = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "res_1");
        res1.MigrationTarget = "res_2";
        myField.Frontend[0] = res1;

        var res2 = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "res_2");
        res2.MigratingFrom = "res_1";
        myField.Frontend[1] = res2;

        myField.Frontend[2] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "res_3");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        // Neither res_1 nor res_2 should appear as migrate sources
        actions.Should().NotContain(a => a.Type == WireActionTypes.Migrate && a.SourceInstanceID == "res_1");
        actions.Should().NotContain(a => a.Type == WireActionTypes.Migrate && a.SourceInstanceID == "res_2");
    }

    [Fact]
    public void Migrate_SingleResourceCannotMigrate()
    {
        // マイグレーションにはソースとターゲットの2体が必要
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 1));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "res_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        // Cannot migrate to self → no migrate actions
        actions.Should().NotContain(a => a.Type == WireActionTypes.Migrate);
    }

    // ═══════════════════════════════════════════════════════════════
    //  TurnControls — ターン制御情報
    // ═══════════════════════════════════════════════════════════════

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
            .Select(i => new UndeployedCard { InstanceID = $"h_{i}", CardID = "SH-0001" })
            .ToList();

        var controls = AvailableActions.ComputeTurnControls(state, hand);
        controls.DiscardRequired.Should().Be(2); // 8 - 6 = 2
    }

    [Fact]
    public void TurnControls_EndPhaseNoDiscardWhenWithinLimit()
    {
        var state = TestFactory.MakeGameState(phase: Phase.End);
        var hand = Enumerable.Range(0, 5)
            .Select(i => new UndeployedCard { InstanceID = $"h_{i}", CardID = "SH-0001" })
            .ToList();

        var controls = AvailableActions.ComputeTurnControls(state, hand);
        controls.DiscardRequired.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════
    //  PlayCard — フィールド埋まりパターン
    //  フロントが満杯でもバックエンドに空きがあれば出せる、等
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void PlayCard_ComputeOnlyBackendWhenFrontendFull()
    {
        // フロントが3枠埋まっていてもバックエンドに空きがあればCompute配置可能
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 0));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_0");
        myField.Frontend[1] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1");
        myField.Frontend[2] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_2");

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "SH-0001" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_1");
        playAction.ValidZones.Should().NotContain(z => z.StartsWith("frontend_"));
        playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
    }

    [Fact]
    public void PlayCard_DatabaseStillPlayableWhenFrontendFull()
    {
        // Database はそもそも Backend のみ。フロントが満杯でも関係ない
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "NT-0009"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_0");
        myField.Frontend[1] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1");
        myField.Frontend[2] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_2");

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_db", CardID = "NT-0009" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, null);

        actions.Should().Contain(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_db");
    }

    [Fact]
    public void PlayCard_ComputeExcludedWhenBothZonesFull()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 0));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        for (int i = 0; i < 3; i++)
        {
            myField.Frontend[i] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: $"fe_{i}");
            myField.Backend[i] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: $"be_{i}");
        }

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = "SH-0001" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_1");
    }

    // ─── リソースタイプ別配置パターン ────────────────────────────

    [Fact]
    public void PlayCard_ContainerCanGoToFrontendAndBackend()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ElasticContainerCard(cardId: "TEST-0002"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var hand = new List<UndeployedCard> { new() { InstanceID = "h_c", CardID = "TEST-0002" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_c");
        playAction.ValidZones.Should().Contain(z => z.StartsWith("frontend_"));
        playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
    }

    [Fact]
    public void PlayCard_ServerlessCanGoToFrontendAndBackend()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ServerlessCard(cardId: "SH-0002"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var hand = new List<UndeployedCard> { new() { InstanceID = "h_s", CardID = "SH-0002" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_s");
        playAction.ValidZones.Should().Contain(z => z.StartsWith("frontend_"));
        playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
    }

    [Fact]
    public void PlayCard_OrchestratorCanGoToFrontendAndBackend()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.OrchestratorCard(cardId: "SH-0003"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var hand = new List<UndeployedCard> { new() { InstanceID = "h_o", CardID = "SH-0003" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, TestFactory.MakeField(), TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_o");
        playAction.ValidZones.Should().Contain(z => z.StartsWith("frontend_"));
        playAction.ValidZones.Should().Contain(z => z.StartsWith("backend_"));
    }

    // ─── Attachment 上限 ────────────────────────────────────────

    [Fact]
    public void PlayCard_AttachmentTargetsAllFaceUpResources()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));
        cc.Add(TestFactory.AttachmentCard(cardId: "TEST-0300"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();

        var res1 = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1");
        myField.Frontend[0] = res1;
        myField.Support.TryPlace(new DeployedSupport { InstanceID = "a1", CardID = "TEST-0300", TargetInstanceID = "fe_1" });
        myField.Support.TryPlace(new DeployedSupport { InstanceID = "a2", CardID = "TEST-0300", TargetInstanceID = "fe_1" });

        var res2 = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_2");
        myField.Frontend[1] = res2;

        var hand = new List<UndeployedCard> { new() { InstanceID = "h_att", CardID = "TEST-0300" } };

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), hand, 5000, 0, cc, null);

        var playAction = actions.Single(a => a.Type == WireActionTypes.PlayCard && a.HandInstanceID == "h_att");
        playAction.ValidTargets.Should().Contain(t => t == "fe_1");
        playAction.ValidTargets.Should().Contain(t => t == "fe_2");
    }

    // ─── ScaleUp 追加パターン ───────────────────────────────────

    [Fact]
    public void ScaleUp_ResizableElasticCanScaleUp()
    {
        // R+E カードは手動スケールアップも可能
        var cc = new TestCardCache();
        cc.Add(TestFactory.OrchestratorCard(cardId: "SH-0003")); // R+E

        var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0003", instanceId: "orch_1", rank: Rank.Small);

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        actions.Should().Contain(a => a.Type == WireActionTypes.ScaleUp && a.SourceInstanceID == "orch_1");
    }

    [Fact]
    public void ScaleUp_DataCardResizableCanScaleUp()
    {
        // Resizable な Database もスケールアップ可能
        var cc = new TestCardCache();
        cc.Add(TestFactory.DataCard(cardId: "NT-0009", resizable: true));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Backend[0] = TestFactory.MakeResource(
            cardId: "NT-0009", instanceId: "db_1", maxTP: null, currentTP: null, maxYield: 400, currentYield: 400);

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        actions.Should().Contain(a => a.Type == WireActionTypes.ScaleUp && a.SourceInstanceID == "db_1");
    }

    // ─── Attack 追加パターン ────────────────────────────────────

    [Fact]
    public void Attack_MultipleAttackersEachGetOwnAction()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "my_1");
        myField.Frontend[1] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "my_2");

        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, [], 5000, 0, cc, null);

        actions.Count(a => a.Type == WireActionTypes.Attack).Should().Be(2);
        actions.Should().Contain(a => a.Type == WireActionTypes.Attack && a.SourceInstanceID == "my_1");
        actions.Should().Contain(a => a.Type == WireActionTypes.Attack && a.SourceInstanceID == "my_2");
    }

    [Fact]
    public void Attack_MigratingFromResourceCannotAttack()
    {
        // MigratingFrom が設定されているリソース（マイグレーション先）も攻撃不可
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        var myField = TestFactory.MakeField();
        var attacker = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "my_1");
        attacker.MigratingFrom = "some_source";
        myField.Frontend[0] = attacker;

        var oppField = TestFactory.MakeField();
        oppField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "opp_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, oppField, [], 5000, 0, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Attack);
    }

    // ─── Migrate 追加パターン ───────────────────────────────────

    [Fact]
    public void Migrate_CrossZoneBetweenFrontendAndBackend()
    {
        // フロントエンドとバックエンドをまたいでマイグレーション可能
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 1));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "fe_1");
        myField.Backend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, null);

        // Frontend → Backend and Backend → Frontend should both be possible
        var feMigrate = actions.Single(a => a.Type == WireActionTypes.Migrate && a.SourceInstanceID == "fe_1");
        feMigrate.ValidTargets.Should().Contain(t => t == "be_1");

        var beMigrate = actions.Single(a => a.Type == WireActionTypes.Migrate && a.SourceInstanceID == "be_1");
        beMigrate.ValidTargets.Should().Contain(t => t == "fe_1");
    }

    // ─── Monetize 追加パターン ───────────────────────────

    [Fact]
    public void Monetize_FaceDownBackendComputeExcluded()
    {
        // 裏向きリソースは収益化できない（稼働していない）
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Backend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1", faceUp: false);

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 100, cc, null);

        actions.Should().NotContain(a => a.Type == WireActionTypes.Monetize);
    }

    [Fact]
    public void Monetize_MultipleBackendComputeEachGetAction()
    {
        // 複数のバックエンドComputeがある場合、それぞれにmonetizeが生成される
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", tp: 600));
        cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", tp: 500, name: "Compute2"));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Backend[0] = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "be_1");
        myField.Backend[1] = TestFactory.MakeResource(cardId: "TEST-0002", instanceId: "be_2");

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 1000, cc, null);

        actions.Count(a => a.Type == WireActionTypes.Monetize).Should().Be(2);
        var a1 = actions.Single(a => a.Type == WireActionTypes.Monetize && a.SourceInstanceID == "be_1");
        var a2 = actions.Single(a => a.Type == WireActionTypes.Monetize && a.SourceInstanceID == "be_2");
        a1.RemainingCapacity.Should().Be(600);
        a2.RemainingCapacity.Should().Be(500);
    }

    // ─── UseEffect: FaceDown resource excluded ─────────────

    [Fact]
    public void UseEffect_FaceDownResourceExcluded()
    {
        var cc = new TestCardCache();
        cc.Add(TestFactory.ComputeCard(cardId: "SH-0009"));

        var registry = new EffectRegistry();
        registry.RegisterComposed("SH-0009", TriggerType.Activate,
            new GainBudgetOp(PlayerRef.Self, new StaticAmount(200)));

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        var myField = TestFactory.MakeField();
        myField.Frontend[0] = TestFactory.MakeResource(cardId: "SH-0009", instanceId: "res_10", faceUp: false);

        var actions = AvailableActions.GetAllAvailableActions(
            state, myField, TestFactory.MakeField(), [], 5000, 0, cc, registry);

        actions.Should().NotContain(a => a.Type == WireActionTypes.UseEffect);
    }
}
