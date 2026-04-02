using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Service;

namespace OverloadParty.Battle.Tests.Service;

public class GameStateViewTests
{
    private readonly TestCardCache _cc = new();
    private readonly Game _game = TestFactory.MakeGame();

    public GameStateViewTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "SH-0001", deployTurns: 0));
        _cc.Add(TestFactory.DataCard(cardId: "NT-0009"));
    }

    // ─── PlayerView (own field fully visible) ─────────────────

    [Fact]
    public void Build_PlayerView_ContainsFullFieldAndHand()
    {
        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, activePlayer: 1,
            p1Budget: 4000, p2Budget: 3000);
        state.Player1InsightPool = 200;

        // Place a resource on player 1's frontend
        var res = TestFactory.MakeResource(cardId: "SH-0001", instanceId: "inst_1", faceUp: true);
        state.Player1Field.Frontend[0] = res;

        // Give player 1 a hand card
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "SH-0001" });

        // Give player 1 repository and trash
        state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "SH-0001" });
        state.Player1Trash.Add(new UndeployedCard { InstanceID = "t_1", CardID = "SH-0001" });

        var result = GameStateView.Build(state, _game, 1, _cc, null);

        result.MyView.PlayerNum.Should().Be(1);
        result.MyView.Budget.Should().Be(4000);
        result.MyView.InsightPool.Should().Be(200);
        result.MyView.Field.Frontend[0].Should().BeSameAs(res);
        result.MyView.Hand.Should().HaveCount(1);
        result.MyView.Hand[0].InstanceID.Should().Be("h_1");
        result.MyView.Hand[0].CardID.Should().Be("SH-0001");
        result.MyView.RepoCount.Should().Be(1);
        result.MyView.TrashCount.Should().Be(1);
        result.MyView.Trash.Should().HaveCount(1);
    }

    // ─── OpponentView (info hiding) ──────────────────────────

    [Fact]
    public void Build_OpponentView_ShowsHandCountNotCards()
    {
        var state = TestFactory.MakeGameState();
        state.Player2Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "SH-0001" });
        state.Player2Hand.Add(new UndeployedCard { InstanceID = "h_2", CardID = "NT-0009" });

        var result = GameStateView.Build(state, _game, 1, _cc, null);

        result.OppView.HandCount.Should().Be(2);
        result.OppView.PlayerNum.Should().Be(2);
    }

    [Fact]
    public void Build_OpponentView_HidesFaceDownResourceDetails()
    {
        var state = TestFactory.MakeGameState();
        var faceDownRes = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "inst_opp", faceUp: false,
            deployLeft: 1, maxAV: 1400, currentAV: 1400, maxTP: 600, currentTP: 600);
        state.Player2Field.Frontend[0] = faceDownRes;

        var result = GameStateView.Build(state, _game, 1, _cc, null);

        var oppSlot = result.OppView.Field.Frontend[0];
        oppSlot.Should().NotBeNull();
        oppSlot!.InstanceID.Should().Be("inst_opp");
        oppSlot.FaceUp.Should().BeFalse();
        oppSlot.DeployingTurnsLeft.Should().Be(1);
        // Hidden details: stats should be zeroed out
        oppSlot.CardID.Should().Be("");
        oppSlot.MaxAV.Should().Be(0);
        oppSlot.MaxTP.Should().BeNull();
    }

    [Fact]
    public void Build_OpponentView_ShowsFaceUpResourceFully()
    {
        var state = TestFactory.MakeGameState();
        var faceUpRes = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "inst_opp_up", faceUp: true,
            maxAV: 1400, currentAV: 1400, maxTP: 600, currentTP: 600);
        state.Player2Field.Frontend[1] = faceUpRes;

        var result = GameStateView.Build(state, _game, 1, _cc, null);

        var oppSlot = result.OppView.Field.Frontend[1];
        oppSlot.Should().NotBeNull();
        oppSlot!.FaceUp.Should().BeTrue();
        oppSlot.CardID.Should().Be("SH-0001");
        oppSlot.MaxAV.Should().Be(1400);
    }

    [Fact]
    public void Build_OpponentView_KeepsCardIDForFaceDownSupport()
    {
        var state = TestFactory.MakeGameState();
        state.Player2Field.Support[0] = new DeployedSupport
        {
            InstanceID = "sup_1",
            CardID = "TEST-0200",
            FaceUp = false,
        };

        var result = GameStateView.Build(state, _game, 1, _cc, null);

        var oppSup = result.OppView.Field.Support[0];
        oppSup.Should().NotBeNull();
        oppSup!.InstanceID.Should().Be("sup_1");
        oppSup.FaceDown.Should().BeTrue();
        oppSup.CardID.Should().BeNull("face-down support should hide CardID");
    }

    [Fact]
    public void Build_OpponentView_RevealsPeekedSupportCardID()
    {
        var state = TestFactory.MakeGameState();
        state.Player2Field.Support[0] = new DeployedSupport
        {
            InstanceID = "sup_peek",
            CardID = "TEST-0200",
            FaceUp = false,
            PeekedBy = [1],
        };

        var result = GameStateView.Build(state, _game, 1, _cc, null);

        var oppSup = result.OppView.Field.Support[0];
        oppSup.Should().NotBeNull();
        oppSup!.FaceDown.Should().BeTrue("card should stay face-down");
        oppSup.Peeked.Should().BeTrue("player 1 has peeked at this card");
        oppSup.CardID.Should().Be("TEST-0200", "peeked card reveals CardID to the peeking player");
    }

    [Fact]
    public void Build_OpponentView_ShowsFaceUpSupportCardID()
    {
        var state = TestFactory.MakeGameState();
        state.Player2Field.Support[1] = new DeployedSupport
        {
            InstanceID = "sup_2",
            CardID = "TEST-0200",
            FaceUp = true,
        };

        var result = GameStateView.Build(state, _game, 1, _cc, null);

        var oppSup = result.OppView.Field.Support[1];
        oppSup.Should().NotBeNull();
        oppSup!.FaceDown.Should().BeFalse();
        oppSup.CardID.Should().Be("TEST-0200");
    }

    // ─── IsMyTurn ─────────────────────────────────────────────

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(2, 2, true)]
    [InlineData(2, 1, false)]
    public void Build_IsMyTurn_ReflectsActivePlayer(long activePlayer, long viewingPlayer, bool expected)
    {
        var state = TestFactory.MakeGameState(activePlayer: activePlayer);

        var result = GameStateView.Build(state, _game, viewingPlayer, _cc, null);

        result.IsMyTurn.Should().Be(expected);
        result.ActivePlayer.Should().Be(activePlayer);
    }

    // ─── Budget and InsightPool for both players ──────────────

    [Fact]
    public void Build_ReportsBudgetAndInsightPool_ForBothPlayers()
    {
        var state = TestFactory.MakeGameState(p1Budget: 3500, p2Budget: 4200);
        state.Player1InsightPool = 150;
        state.Player2InsightPool = 300;

        var result = GameStateView.Build(state, _game, 1, _cc, null);

        result.MyView.Budget.Should().Be(3500);
        result.MyView.InsightPool.Should().Be(150);
        result.OppView.Budget.Should().Be(4200);
        result.OppView.InsightPool.Should().Be(300);
    }

    [Fact]
    public void Build_AsPlayer2_SwapsMyViewAndOppView()
    {
        var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
        state.Player1Hand.Add(new UndeployedCard { InstanceID = "p1_h", CardID = "SH-0001" });
        state.Player2Hand.Add(new UndeployedCard { InstanceID = "p2_h", CardID = "NT-0009" });

        var result = GameStateView.Build(state, _game, 2, _cc, null);

        result.MyView.PlayerNum.Should().Be(2);
        result.MyView.Budget.Should().Be(2000);
        result.MyView.Hand.Should().ContainSingle(h => h.InstanceID == "p2_h");
        result.OppView.PlayerNum.Should().Be(1);
        result.OppView.Budget.Should().Be(1000);
        result.OppView.HandCount.Should().Be(1);
    }

    // ─── Game metadata ───────────────────────────────────────

    [Fact]
    public void Build_SetsGameMetadata()
    {
        var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Battle, activePlayer: 2);

        var result = GameStateView.Build(state, _game, 1, _cc, null);

        result.GameID.Should().Be("test-game");
        result.CurrentTurn.Should().Be(5);
        result.CurrentPhase.Should().Be("battle");
        result.ActivePlayer.Should().Be(2);
    }

    // ─── OpponentView counts ──────────────────────────────────

    [Fact]
    public void Build_OpponentView_ReportsRepoAndTrashCounts()
    {
        var state = TestFactory.MakeGameState();
        state.Player2Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "SH-0001" });
        state.Player2Repository.Add(new UndeployedCard { InstanceID = "r_2", CardID = "SH-0001" });
        state.Player2Trash.Add(new UndeployedCard { InstanceID = "t_1", CardID = "SH-0001" });

        var result = GameStateView.Build(state, _game, 1, _cc, null);

        result.OppView.RepoCount.Should().Be(2);
        result.OppView.TrashCount.Should().Be(1);
    }

    // ─── AvailableActions only for active player ──────────────

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void Build_AvailableActions_DependsOnActivePlayer(long activePlayer, bool expectActions)
    {
        var state = TestFactory.MakeGameState(phase: Phase.Main, activePlayer: activePlayer);

        var result = GameStateView.Build(state, _game, 1, _cc, null);

        if (expectActions)
        {
            result.MyView.AvailableActions.Should().NotBeNull();
        }
        else
        {
            result.MyView.AvailableActions.Should().BeNull();
        }
    }

    [Fact]
    public void Build_FinishedGame_DoesNotGetAvailableActions()
    {
        var game = TestFactory.MakeGame();
        game.Status = GameStatus.Finished;
        var state = TestFactory.MakeGameState(phase: Phase.Main, activePlayer: 1);

        var result = GameStateView.Build(state, game, 1, _cc, null);

        result.MyView.AvailableActions.Should().BeNull();
    }

    // ─── TimeBank ─────────────────────────────────────────────

    [Fact]
    public void Build_ReportsTimeBank()
    {
        var state = TestFactory.MakeGameState();
        state.Player1TimeBank = 300;
        state.Player2TimeBank = 420;

        var result = GameStateView.Build(state, _game, 1, _cc, null);

        result.MyView.TimeBank.Should().Be(300);
        result.OppView.TimeBank.Should().Be(420);
    }

    // ─── Empty field slots ────────────────────────────────────

    [Fact]
    public void Build_OpponentView_EmptySlots_AreNull()
    {
        var state = TestFactory.MakeGameState();
        // Player 2 field is completely empty

        var result = GameStateView.Build(state, _game, 1, _cc, null);

        result.OppView.Field.Frontend.Should().AllSatisfy(slot => slot.Should().BeNull());
        result.OppView.Field.Backend.Should().AllSatisfy(slot => slot.Should().BeNull());
        result.OppView.Field.Support.Should().AllSatisfy(slot => slot.Should().BeNull());
    }
}
