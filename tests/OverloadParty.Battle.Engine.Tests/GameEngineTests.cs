using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Tests.Fakes;

namespace OverloadParty.Battle.Tests.Engine;

public class GameEngineTests
{
    private readonly FakeGameRepository _repo = new();
    private readonly TestCardCache _cc = new();
    private readonly GameEngine _engine;

    public GameEngineTests()
    {
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, slaPenalty: 400, deployTurns: 0));
        _cc.Add(TestFactory.ComputeCard(cardId: "TEST-0002", tp: 800, av: 1600, slaPenalty: 500, deployTurns: 1, name: "SlowCompute"));
        _cc.Add(TestFactory.DataCard(cardId: "TST-0002"));
        _engine = new GameEngine(_repo, _cc, new EffectRegistry(), new InitiativeCatalog([]));
    }

    private DeckSnapshot MakeSingleCardDeck(string cardId)
    {
        return TestFactory.MakeDeck(cardId);
    }

    // ─── CreateNewGame ───────────────────────────────────────

    [Fact]
    public async Task CreateNewGame_ReturnsGameID_And_InitializesState()
    {
        var deck = MakeSingleCardDeck("TST-0001");

        var gameID = await _engine.CreateNewGame(deck, deck, 1);

        gameID.Should().NotBeNullOrEmpty();

        var game = await _repo.GetGame(gameID);
        game.Should().NotBeNull();
        game!.Status.Should().Be(GameStatus.Playing);

        var state = await _repo.GetGameState(gameID);
        state.Should().NotBeNull();
        state!.CurrentTurn.Should().Be(1);
        state.CurrentPhase.Should().Be(Phase.Draw);
        state.ActivePlayer.Should().Be(1);
        state.Player1Budget.Should().Be(BattleConstants.InitialBudget);
        state.Player2Budget.Should().Be(BattleConstants.InitialBudget);

        // Each player should have initial hand cards
        state.Player1Hand.Should().HaveCount(BattleConstants.InitialHandSize);
        state.Player2Hand.Should().HaveCount(BattleConstants.InitialHandSize);

        // Repository should have remaining cards
        state.Player1Repository.Should().HaveCount(InitialValues.DeckSize - BattleConstants.InitialHandSize);

        // All hand cards should reference the correct card
        state.Player1Hand.Should().AllSatisfy(h => h.CardID.Should().Be("TST-0001"));
    }

    [Fact]
    public async Task CreateNewGame_FirstPlayer2_SetsActivePlayer2()
    {
        var deck = MakeSingleCardDeck("TST-0001");

        var gameID = await _engine.CreateNewGame(deck, deck, 2);

        var state = await _repo.GetGameState(gameID);
        state!.ActivePlayer.Should().Be(2);
    }

    // ─── RunAutoAdvance ──────────────────────────────────────

    [Fact]
    public async Task RunAutoAdvance_DrawPhase_DrawsCardAndAdvancesToMain()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1);

        var state = await _repo.GetGameState(gameID);
        int handBefore = state!.Player1Hand.Count;
        int repoBefore = state.Player1Repository.Count;

        var game = await _repo.GetGame(gameID);
        var result = await _engine.RunAutoAdvance(game!);

        result.Should().BeNull("no game-over expected");

        // Re-read state (UpdateGameState mutates in place for FakeGameRepository)
        state.Player1Hand.Should().HaveCount(handBefore + 1);
        state.Player1Repository.Should().HaveCount(repoBefore - 1);
        state.CurrentPhase.Should().Be(Phase.Main);
    }

    // ─── ProcessAction: PlayCard ─────────────────────────────

    [Fact]
    public async Task ProcessAction_PlayCard_PlaysCardAndReturnsEvents()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1);

        // Advance past draw phase
        var game = await _repo.GetGame(gameID);
        await _engine.RunAutoAdvance(game!);

        var state = await _repo.GetGameState(gameID);
        state!.CurrentPhase.Should().Be(Phase.Main);

        var cardToPlay = state.Player1Hand.First();
        var req = new PlayCardRequest
        {
            CardInstanceID = cardToPlay.InstanceID,
            Zone = Zones.Frontend,
            Index = 0,
        };

        game = await _repo.GetGame(gameID);
        var result = await _engine.ProcessAction(
            game!, 1, ActionType.PlayCard, req);

        result.Should().NotBeNull();
        result.Events.Should().Contain(e => e.EventType == ActionTypes.PlayCard);

        // Card removed from hand
        state.Player1Hand.Should().NotContain(h => h.InstanceID == cardToPlay.InstanceID);

        // Card placed on field (deployTurns=0 → face-up)
        state.Player1Field.Frontend[0].Should().NotBeNull();
        state.Player1Field.Frontend[0]!.FaceUp.Should().BeTrue();
    }

    // ─── ProcessAction: Attack ───────────────────────────────

    [Fact]
    public async Task ProcessAction_Attack_DealsDamageAndReturnsEvents()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1);

        // Set up the game state directly for attack testing
        var state = await _repo.GetGameState(gameID);

        // Place resources manually
        var attacker = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
        state!.Player1Field.Frontend[0] = attacker;

        var defender = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);
        state.Player2Field.Frontend[0] = defender;

        // Set to battle phase, turn 2+
        state.CurrentTurn = 2;
        state.CurrentPhase = Phase.Battle;
        state.ActivePlayer = 1;

        var req = new AttackRequest
        {
            AttackerInstanceID = "atk_1",
            TargetInstanceID = "def_1",
        };

        var game = await _repo.GetGame(gameID);
        var result = await _engine.ProcessAction(
            game!, 1, ActionType.Attack, req);

        result.Should().NotBeNull();
        result.Events.Should().Contain(e => e.EventType == ActionTypes.Attack);

        defender.Damage.Should().Be(600);
        attacker.HasAttacked.Should().BeTrue();

        // Events should be persisted
        var events = await _repo.GetEvents(gameID);
        events.Should().Contain(e => e.EventType == ActionTypes.Attack);
    }

    // ─── ProcessAction: validation ───────────────────────────

    [Fact]
    public async Task ProcessAction_WrongPlayer_Throws()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1);
        var game = await _repo.GetGame(gameID);
        await _engine.RunAutoAdvance(game!);

        var state = await _repo.GetGameState(gameID);
        var cardToPlay = state!.Player2Hand.First();
        var req = new PlayCardRequest
        {
            CardInstanceID = cardToPlay.InstanceID,
            Zone = Zones.Frontend,
            Index = 0,
        };

        // Player 2 tries to act on player 1's turn
        game = await _repo.GetGame(gameID);
        var act = () => _engine.ProcessAction(
            game!, 2, ActionType.PlayCard, req);

        await act.Should().ThrowAsync<GameRuleException>().WithMessage("*not your turn*");
    }

    [Fact]
    public async Task GetNpcModel_InvalidPlayer_Throws()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1);

        var game = await _repo.GetGame(gameID);

        var act = () => game!.GetNpcModel(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Forfeit_EndsGameImmediately()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1);

        var game = await _repo.GetGame(gameID);
        var result = await _engine.Forfeit(game!, 1, WinReason.Surrender);

        result.GameOver.Should().NotBeNull();
        result.GameOver!.WinnerNum.Should().Be(2, "opponent wins on forfeit");
        result.GameOver.Reason.Should().Be(WinReasons.Surrender);

        game = await _repo.GetGame(gameID);
        game!.Status.Should().Be(GameStatus.Finished);
        game.WinningPlayerNum.Should().Be(2);
        game.WinReason.Should().Be(WinReasons.Surrender);
    }

    [Fact]
    public async Task ProcessAction_FinishedGame_Throws()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1);

        // Forfeit to finish the game
        var game = await _repo.GetGame(gameID);
        await _engine.Forfeit(game!, 1, WinReason.Surrender);

        // Trying to act on a finished game should throw
        game = await _repo.GetGame(gameID);
        var act = () => _engine.ProcessAction(
            game!, 2, ActionType.EndPhase, new object());

        await act.Should().ThrowAsync<GameRuleException>().WithMessage("*not in playing state*");
    }

    // ─── Initial state preservation ──────────────────────────

    [Fact]
    public async Task GetInitialState_ReturnsOriginalState_AfterMutations()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1);

        var initialState = await _repo.GetInitialState(gameID);
        initialState.Should().NotBeNull();
        var originalBudget = initialState!.Player1Budget;
        var originalHandCount = initialState.Player1Hand.Count;

        var game = await _repo.GetGame(gameID);
        await _engine.RunAutoAdvance(game!);

        var currentState = await _repo.GetGameState(gameID);
        currentState!.Player1Hand.Count.Should().NotBe(originalHandCount,
            "draw phase should have changed the hand");

        var preserved = await _repo.GetInitialState(gameID);
        preserved!.Player1Budget.Should().Be(originalBudget);
        preserved.Player1Hand.Should().HaveCount(originalHandCount);
    }

    // ─── Version recording ──────────────────────────────────

    [Fact]
    public async Task CreateNewGame_RecordsVersions()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1,
            engineVersion: "1.2.3", cardDataVersion: "4.5.6");

        var game = await _repo.GetGame(gameID);
        game!.EngineVersion.Should().Be("1.2.3");
        game.CardDataVersion.Should().Be("4.5.6");
    }

    // ─── SelectSlot gate ────────────────────────────────────

    [Fact]
    public async Task ProcessAction_PendingSlotSelect_BlocksOtherActions()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1);
        var game = await _repo.GetGame(gameID);
        await _engine.RunAutoAdvance(game!);

        var state = await _repo.GetGameState(gameID);
        state!.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = 1,
            Resource = TestFactory.MakeResource(instanceId: "pending_1"),
            ValidZones = ["frontend_1"],
        });

        game = await _repo.GetGame(gameID);
        var act = () => _engine.ProcessAction(
            game!, 1, ActionType.EndPhase, new object());

        await act.Should().ThrowAsync<GameRuleException>().WithMessage("*slot selection*");
    }

    [Fact]
    public async Task ProcessAction_PendingSlotSelect_AllowsSelectSlot()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1);
        var game = await _repo.GetGame(gameID);
        await _engine.RunAutoAdvance(game!);

        var state = await _repo.GetGameState(gameID);
        state!.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = 1,
            Resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "pending_1"),
            ValidZones = ["frontend_1"],
        });

        game = await _repo.GetGame(gameID);
        var result = await _engine.ProcessAction(
            game!, 1, ActionType.SelectSlot,
            new SelectSlotRequest { Zone = Zones.Frontend, Index = 1 });

        result.Events.Should().Contain(e => e.EventType == ActionTypes.SelectSlot);
        state.PendingSlotSelects.Should().BeEmpty();
        state.Player1Field.Frontend[1].Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessAction_PendingSlotSelect_DoesNotBlockOtherPlayer()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1);
        var game = await _repo.GetGame(gameID);
        await _engine.RunAutoAdvance(game!);

        var state = await _repo.GetGameState(gameID);
        // Player 2 is awaiting slot select, but it's Player 1's turn
        state!.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = 2,
            Resource = TestFactory.MakeResource(instanceId: "pending_1"),
            ValidZones = ["frontend_0"],
        });

        // Player 1 should still be able to act
        var cardToPlay = state.Player1Hand.First();
        game = await _repo.GetGame(gameID);
        var result = await _engine.ProcessAction(
            game!, 1, ActionType.PlayCard,
            new PlayCardRequest
            {
                CardInstanceID = cardToPlay.InstanceID,
                Zone = Zones.Frontend,
                Index = 0,
            });

        result.Events.Should().Contain(e => e.EventType == ActionTypes.PlayCard);
    }

    [Fact]
    public async Task ProcessAction_WhilePendingSlotSelect_BlocksNextAction()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1);
        var game = await _repo.GetGame(gameID);
        await _engine.RunAutoAdvance(game!);

        var state = await _repo.GetGameState(gameID);
        var cardToPlay = state!.Player1Hand.First();

        game = await _repo.GetGame(gameID);
        await _engine.ProcessAction(
            game!, 1, ActionType.PlayCard,
            new PlayCardRequest
            {
                CardInstanceID = cardToPlay.InstanceID,
                Zone = Zones.Frontend,
                Index = 0,
            });

        state.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = 1,
            Resource = TestFactory.MakeResource(instanceId: "pending_1"),
            ValidZones = ["frontend_1"],
        });

        var cardToPlay2 = state.Player1Hand.First();
        game = await _repo.GetGame(gameID);
        var act = () => _engine.ProcessAction(
            game!, 1, ActionType.PlayCard,
            new PlayCardRequest
            {
                CardInstanceID = cardToPlay2.InstanceID,
                Zone = Zones.Frontend,
                Index = 1,
            });

        await act.Should().ThrowAsync<GameRuleException>().WithMessage("*slot selection*");
    }

    [Fact]
    public async Task ProcessAction_SelectSlot_ResolvesAndReturnsNeedsSlotSelectTrue_WhenQueueRemains()
    {
        var deck = MakeSingleCardDeck("TST-0001");
        var gameID = await _engine.CreateNewGame(deck, deck, 1);
        var game = await _repo.GetGame(gameID);
        await _engine.RunAutoAdvance(game!);

        var state = await _repo.GetGameState(gameID);

        // 2件のスロット選択をキューに積む
        state!.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = 1,
            Resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "pending_1"),
            ValidZones = ["frontend_0"],
        });
        state.PendingSlotSelects.Add(new AwaitingSlotSelect
        {
            PlayerNum = 1,
            Resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "pending_2"),
            ValidZones = ["frontend_1"],
        });

        // 1件目を処理
        game = await _repo.GetGame(gameID);
        var result = await _engine.ProcessAction(
            game!, 1, ActionType.SelectSlot,
            new SelectSlotRequest { Zone = Zones.Frontend, Index = 0 });

        result.ShouldSelectSlot.Should().BeTrue();
        state.Player1Field.Frontend[0]!.InstanceID.Should().Be("pending_1");
        state.PendingSlotSelects.Should().ContainSingle();

        // 2件目を処理
        game = await _repo.GetGame(gameID);
        var result2 = await _engine.ProcessAction(
            game!, 1, ActionType.SelectSlot,
            new SelectSlotRequest { Zone = Zones.Frontend, Index = 1 });

        result2.ShouldSelectSlot.Should().BeFalse();
        state.Player1Field.Frontend[1]!.InstanceID.Should().Be("pending_2");
        state.PendingSlotSelects.Should().BeEmpty();
    }
}
