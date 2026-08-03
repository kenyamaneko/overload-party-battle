using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

public class GameEngineTests
{
    /// <summary>Shared setup for GameEngine tests (repository, card cache, engine, and deck helper).</summary>
    public abstract class Base
    {
        protected readonly FakeGameRepository _repo = new();
        protected readonly TestCardCache _cc = new();
        protected readonly GameEngine _engine;

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600, av: 1400, slaPenalty: 400, deployTurns: 0));
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0003", tp: 800, av: 1600, slaPenalty: 500, deployTurns: 1, name: "SlowCompute"));
            _cc.Add(TestFactory.DataCard(cardId: "TST-0002"));
            _engine = new GameEngine(_repo, _cc, new EffectRegistry(), new InitiativeCatalog([]), new FakeClock());
        }

        /// <summary>指定カードを含むデッキ規約どおりの 30 枚デッキを組む。</summary>
        /// <param name="cardId">デッキに入れるカードの ID。</param>
        /// <returns>30 枚のデッキスナップショット。</returns>
        protected DeckSnapshot MakeDeckWith(string cardId)
        {
            return TestFactory.MakeDeck(_cc, cardId);
        }
    }

    [Trait("対象", "新規ゲームの作成")]
    public class CreateNewGame : Base
    {
        [Fact(DisplayName = "ゲームを作成すると、ゲーム ID を返し初期ターン・フェーズ・バジェット・手札・デッキを初期化する")]
        public async Task CreateNewGame_ReturnsGameID_And_InitializesState()
        {
            var deck = MakeDeckWith("TST-0001");

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

            // All hand cards should come from the deck
            var deckCardIds = deck.Cards.Select(c => c.CardId).ToHashSet();
            state.Player1Hand.Should().AllSatisfy(h => deckCardIds.Should().Contain(h.CardID));
        }

        [Fact(DisplayName = "先攻をプレイヤー 2 で作成すると、アクティブプレイヤーが 2 になる")]
        public async Task CreateNewGame_FirstPlayer2_SetsActivePlayer2()
        {
            var deck = MakeDeckWith("TST-0001");

            var gameID = await _engine.CreateNewGame(deck, deck, 2);

            var state = await _repo.GetGameState(gameID);
            state!.ActivePlayer.Should().Be(2);
        }

        [Fact(DisplayName = "エンジンバージョンとカードデータバージョンがゲームに記録される")]
        public async Task CreateNewGame_RecordsVersions()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1,
                engineVersion: "1.2.3", cardDataVersion: "4.5.6");

            var game = await _repo.GetGame(gameID);
            game!.EngineVersion.Should().Be("1.2.3");
            game.CardDataVersion.Should().Be("4.5.6");
        }
    }

    [Trait("対象", "フェーズの自動進行")]
    public class RunAutoAdvance : Base
    {
        [Fact(DisplayName = "ドローフェーズで自動進行すると、1 枚引いてメインフェーズへ進む")]
        public async Task RunAutoAdvance_DrawPhase_DrawsCardAndAdvancesToMain()
        {
            var deck = MakeDeckWith("TST-0001");
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
    }

    [Trait("対象", "カードのデプロイ")]
    public class ProcessPlayCard : Base
    {
        [Fact(DisplayName = "カードプレイアクションで手札のカードをフィールドに出し、カードプレイイベントを返す")]
        public async Task ProcessAction_PlayCard_PlaysCardAndReturnsEvents()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);

            // Advance past draw phase
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            state!.CurrentPhase.Should().Be(Phase.Main);

            var cardInstanceId = TestFactory.ReplaceFirstHandCard(state, 1, "TST-0001");
            var req = new PlayCardRequest
            {
                CardInstanceID = cardInstanceId,
                Zone = Zones.Frontend,
                Index = 0,
            };

            game = await _repo.GetGame(gameID);
            var result = await _engine.ProcessAction(
                game!, 1, ActionType.PlayCard, req);

            result.Should().NotBeNull();
            result.Events.Should().Contain(e => e.EventType == ActionTypes.PlayCard);

            // Card removed from hand
            state.Player1Hand.Should().NotContain(h => h.InstanceID == cardInstanceId);

            // Card placed on field (deployTurns=0 → face-up)
            state.Player1Field.Frontend[0].Should().NotBeNull();
            state.Player1Field.Frontend[0]!.FaceUp.Should().BeTrue();
        }
    }

    [Trait("対象", "攻撃の処理")]
    public class ProcessAttack : Base
    {
        [Fact(DisplayName = "攻撃アクションで対象に 600 ダメージを与え、攻撃イベントを返して永続化する")]
        public async Task ProcessAction_Attack_DealsDamageAndReturnsEvents()
        {
            var deck = MakeDeckWith("TST-0001");
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
    }

    [Trait("対象", "アクションの検証と強制決着")]
    public class ProcessActionValidation : Base
    {
        [Fact(DisplayName = "自分のターンでないプレイヤーがアクションすると、GameRuleException を投げる")]
        public async Task ProcessAction_WrongPlayer_Throws()
        {
            var deck = MakeDeckWith("TST-0001");
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

        [Fact(DisplayName = "NPC モデル取得に不正なプレイヤー番号 0 を渡すと、ArgumentOutOfRangeException を投げる")]
        public async Task GetNpcModel_InvalidPlayer_Throws()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);

            var game = await _repo.GetGame(gameID);

            var act = () => game!.GetNpcModel(0);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "プレイヤー 1 が強制決着すると、相手を勝者・理由 Surrender としてゲームが即座に終了する")]
        public async Task Forfeit_EndsGameImmediately()
        {
            var deck = MakeDeckWith("TST-0001");
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

        [Fact(DisplayName = "終了済みゲームにアクションすると、GameRuleException を投げる")]
        public async Task ProcessAction_FinishedGame_Throws()
        {
            var deck = MakeDeckWith("TST-0001");
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
    }

    [Trait("対象", "両者強制決着の処理")]
    public class ProcessForfeitBoth : Base
    {
        [Fact(DisplayName = "両者強制決着すると、勝者なし・理由 Disconnect としてゲームが即座に終了する")]
        public async Task ForfeitBoth_EndsGameAsDraw()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);

            var game = await _repo.GetGame(gameID);
            var result = await _engine.ForfeitBoth(game!);

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(0, "neither player wins when both forfeit");
            result.GameOver.Reason.Should().Be(WinReasons.Disconnect);

            game = await _repo.GetGame(gameID);
            game!.Status.Should().Be(GameStatus.Finished);
            game.WinningPlayerNum.Should().Be(0);
            game.WinReason.Should().Be(WinReasons.Disconnect);
        }

        [Fact(DisplayName = "終了済みゲームで両者強制決着すると、GameRuleException を投げる")]
        public async Task ForfeitBoth_FinishedGame_Throws()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);

            var game = await _repo.GetGame(gameID);
            await _engine.Forfeit(game!, 1, WinReason.Surrender);

            game = await _repo.GetGame(gameID);
            var act = () => _engine.ForfeitBoth(game!);

            await act.Should().ThrowAsync<GameRuleException>().WithMessage("*not in playing state*");
        }
    }

    [Trait("対象", "初期状態の保存")]
    public class InitialStatePreservation : Base
    {
        [Fact(DisplayName = "ゲーム進行で状態が変化しても、保存された初期状態は元のバジェットと手札枚数を保つ")]
        public async Task GetInitialState_ReturnsOriginalState_AfterMutations()
        {
            var deck = MakeDeckWith("TST-0001");
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
    }

    [Trait("対象", "スロット選択待ちによるアクション制御")]
    public class PendingSlotSelectGate : Base
    {
        [Fact(DisplayName = "スロット選択待ちのプレイヤーが別アクションをすると、GameRuleException を投げる")]
        public async Task ProcessAction_PendingSlotSelect_BlocksOtherActions()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            state!.PendingSlotSelects.Add(new AwaitingSlotSelect
            {
                PlayerNum = 1,
                Resource = TestFactory.MakeResource(instanceId: "pending_1"),
                SourceZone = SlotSelectSources.Repository,
            });

            game = await _repo.GetGame(gameID);
            var act = () => _engine.ProcessAction(
                game!, 1, ActionType.EndPhase, new object());

            await act.Should().ThrowAsync<GameRuleException>().WithMessage("*slot selection*");
        }

        [Fact(DisplayName = "スロット選択待ち中にスロットを選択すると、選択したスロットへ配置し待ちが解消される")]
        public async Task ProcessAction_PendingSlotSelect_AllowsSelectSlot()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            state!.PendingSlotSelects.Add(new AwaitingSlotSelect
            {
                PlayerNum = 1,
                Resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "pending_1"),
                SourceZone = SlotSelectSources.Repository,
            });

            game = await _repo.GetGame(gameID);
            var result = await _engine.ProcessAction(
                game!, 1, ActionType.SelectSlot,
                new SelectSlotRequest { Zone = Zones.Frontend, Index = 1 });

            result.Events.Should().Contain(e => e.EventType == ActionTypes.SelectSlot);
            state.PendingSlotSelects.Should().BeEmpty();
            state.Player1Field.Frontend[1].Should().NotBeNull();
        }

        [Fact(DisplayName = "相手がスロット選択待ちのとき、ターンプレイヤーのカードプレイは拒否される")]
        public async Task ProcessAction_OpponentPendingSlotSelect_BlocksTurnPlayer()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            // 手番は P1 だが、P2 にスロット選択が残っている
            state!.PendingSlotSelects.Add(new AwaitingSlotSelect
            {
                PlayerNum = 2,
                Resource = TestFactory.MakeResource(instanceId: "pending_1"),
                SourceZone = SlotSelectSources.Repository,
            });

            var cardToPlay = state.Player1Hand.First();
            game = await _repo.GetGame(gameID);
            var act = async () => await _engine.ProcessAction(
                game!, 1, ActionType.PlayCard,
                new PlayCardRequest
                {
                    CardInstanceID = cardToPlay.InstanceID,
                    Zone = Zones.Frontend,
                    Index = 0,
                });

            await act.Should().ThrowAsync<GameRuleException>().WithMessage("*slot selection required*");
        }

        [Fact(DisplayName = "相手ターン中に自分に割り当てられたスロット選択は、自分で解決できる")]
        public async Task ProcessAction_OwnSlotSelect_ResolvableOnOpponentTurn()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            // 手番は P1 だが、選択の所有者は P2
            state!.PendingSlotSelects.Add(new AwaitingSlotSelect
            {
                PlayerNum = 2,
                Resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "pending_1"),
                SourceZone = SlotSelectSources.Repository,
            });

            game = await _repo.GetGame(gameID);
            var result = await _engine.ProcessAction(
                game!, 2, ActionType.SelectSlot,
                new SelectSlotRequest { Zone = Zones.Frontend, Index = 0 });

            result.Events.Should().Contain(e => e.EventType == ActionTypes.SelectSlot);
            state.Player2Field.Frontend[0].Should().NotBeNull();
            state.PendingSlotSelects.Should().BeEmpty();
        }

        [Fact(DisplayName = "相手の選択がキュー先頭にあっても、自分の選択を解決できる")]
        public async Task ProcessAction_OwnSlotSelect_ResolvableBehindOpponentsQueuedOne()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            state!.PendingSlotSelects.Add(new AwaitingSlotSelect
            {
                PlayerNum = 2,
                Resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "opp_pending"),
                SourceZone = SlotSelectSources.Repository,
            });
            state.PendingSlotSelects.Add(new AwaitingSlotSelect
            {
                PlayerNum = 1,
                Resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "my_pending"),
                SourceZone = SlotSelectSources.Repository,
            });

            game = await _repo.GetGame(gameID);
            await _engine.ProcessAction(
                game!, 1, ActionType.SelectSlot,
                new SelectSlotRequest { Zone = Zones.Frontend, Index = 1 });

            state.Player1Field.Frontend[1]!.InstanceID.Should().Be("my_pending");
            state.PendingSlotSelects.Should().ContainSingle()
                .Which.Resource.InstanceID.Should().Be("opp_pending");
        }

        [Fact(DisplayName = "スロット選択待ちが生じた後の次アクションは、GameRuleException を投げる")]
        public async Task ProcessAction_WhilePendingSlotSelect_BlocksNextAction()
        {
            var deck = MakeDeckWith("TST-0001");
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
                SourceZone = SlotSelectSources.Repository,
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

        [Fact(DisplayName = "スロット選択待ちが 2 件あるとき、1 件目の解決では選択継続を示し 2 件目で解消される")]
        public async Task ProcessAction_SelectSlot_ResolvesAndReturnsNeedsSlotSelectTrue_WhenQueueRemains()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);

            // 2件のスロット選択をキューに積む
            state!.PendingSlotSelects.Add(new AwaitingSlotSelect
            {
                PlayerNum = 1,
                Resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "pending_1"),
                SourceZone = SlotSelectSources.Repository,
            });
            state.PendingSlotSelects.Add(new AwaitingSlotSelect
            {
                PlayerNum = 1,
                Resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "pending_2"),
                SourceZone = SlotSelectSources.Repository,
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

    [Trait("対象", "効果によるデプロイの稼働開始")]
    public class EffectDeployCompletion
    {
        private const string IgnitionCardId = "TST-0009";
        private const string DeployCardId = "TST-0001";
        private const string SlowDeployCardId = "TST-0003";
        private const string WatcherCardId = "TST-0400";
        private const string AttackerCardId = "TST-0005";
        private const string AttachmentCardId = "TST-0300";

        private readonly FakeGameRepository _repo = new();
        private readonly TestCardCache _cc = new();
        private readonly EffectRegistry _effects = new();
        private readonly GameEngine _engine;

        public EffectDeployCompletion()
        {
            _cc.Add(TestFactory.PlatformCard(cardId: IgnitionCardId));
            _cc.Add(TestFactory.ComputeCard(cardId: DeployCardId, mc: 0, deployTurns: 0));
            _cc.Add(TestFactory.ComputeCard(cardId: SlowDeployCardId, mc: 0, deployTurns: 1, name: "SlowCompute"));
            _cc.Add(TestFactory.ReactiveCard(cardId: WatcherCardId));
            _cc.Add(TestFactory.ComputeCard(cardId: AttackerCardId, mc: 0, tp: 2000, deployTurns: 0, name: "StrongCompute"));
            _cc.Add(TestFactory.AttachmentCard(cardId: AttachmentCardId));
            _engine = new GameEngine(_repo, _cc, _effects, new InitiativeCatalog([]), new FakeClock());
        }

        /// <summary>プレイヤー 1 のサポートゾーンに発動元を置いた、メインフェーズのゲームを始める。</summary>
        /// <param name="deployedCardId">効果でデプロイされるカードの ID。</param>
        /// <returns>ゲーム ID と、進行に伴って更新され続けるゲーム状態。</returns>
        private async Task<(string GameID, BattleGameState State)> StartWithIgnitionSource(string deployedCardId)
        {
            var deck = TestFactory.MakeDeck(_cc, DeployCardId);
            var gameID = await _engine.CreateNewGame(deck, deck, firstPlayer: 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = (await _repo.GetGameState(gameID))!;
            state.Player1Field.Support[0] = new DeployedSupport
            {
                InstanceID = "src",
                CardID = IgnitionCardId,
                FaceUp = true,
                DeployOrder = 1,
            };
            state.Player1Repository = [new UndeployedCard { InstanceID = "r_1", CardID = deployedCardId }];
            return (gameID, state);
        }

        /// <summary>指定プレイヤーのアクションを実行する。</summary>
        /// <param name="gameID">対象ゲームの ID。</param>
        /// <param name="playerNum">アクションするプレイヤー番号。</param>
        /// <param name="actionType">アクション種別。</param>
        /// <param name="actionData">アクション固有のリクエスト。</param>
        /// <returns>アクション結果。</returns>
        private async Task<ActionResult> Act(string gameID, long playerNum, ActionType actionType, object actionData)
        {
            var game = await _repo.GetGame(gameID);
            return await _engine.ProcessAction(game!, playerNum, actionType, actionData);
        }

        private Task<ActionResult> Ignite(string gameID) =>
            Act(gameID, 1, ActionType.UseIgnition, new UseIgnitionRequest { InstanceID = "src" });

        private Task<ActionResult> SelectSlot(string gameID, long playerNum, string zone, int index) =>
            Act(gameID, playerNum, ActionType.SelectSlot, new SelectSlotRequest { Zone = zone, Index = index });

        /// <summary>指定プレイヤーのターンを、手札上限の調整を挟みながら終了させる。</summary>
        /// <param name="gameID">対象ゲームの ID。</param>
        /// <param name="playerNum">ターンを終了するプレイヤー番号。</param>
        private async Task EndTurn(string gameID, long playerNum)
        {
            while (true)
            {
                var result = await Act(gameID, playerNum, ActionType.EndPhase, new object());

                if (result.ShouldDiscard)
                {
                    var hand = (await _repo.GetGameState(gameID))!.GetHand(playerNum);
                    var discardIds = hand.Take(hand.Count - BattleConstants.HandLimit)
                        .Select(c => c.InstanceID).ToList();
                    await Act(gameID, playerNum, ActionType.DiscardHand,
                        new DiscardHandRequest { CardInstanceIDs = discardIds });
                    return;
                }

                if ((await _repo.GetGameState(gameID))!.ActivePlayer != playerNum)
                {
                    return;
                }
            }
        }

        [Fact(DisplayName = "効果デプロイのスロット選択を解決して表向きリソースが場に出ると、稼働実績が立つ")]
        public async Task SlotSelectResolved_FaceUpResource_SetsHasOperated()
        {
            _effects.RegisterComposed(IgnitionCardId, TriggerType.Ignition, new RequestSlotFromRepoOp());
            var (gameID, state) = await StartWithIgnitionSource(DeployCardId);

            await Ignite(gameID);
            await SelectSlot(gameID, 1, Zones.Frontend, 0);

            state.Player1Field.Frontend[0]!.CardID.Should().Be(DeployCardId);
            state.Player1HasOperated.Should().BeTrue();
        }

        [Fact(DisplayName = "効果デプロイで裏向きに置かれたとき、稼働実績は立たない")]
        public async Task SlotSelectResolved_FaceDownResource_LeavesHasOperatedFalse()
        {
            _effects.RegisterComposed(IgnitionCardId, TriggerType.Ignition, new RequestSlotFromRepoOp());
            var (gameID, state) = await StartWithIgnitionSource(SlowDeployCardId);

            await Ignite(gameID);
            await SelectSlot(gameID, 1, Zones.Frontend, 0);

            state.Player1Field.Frontend[0]!.FaceUp.Should().BeFalse();
            state.Player1Field.Frontend[0]!.DeployingTurnsLeft.Should().Be(1);
            state.Player1HasOperated.Should().BeFalse();
        }

        [Fact(DisplayName = "効果デプロイのスロット選択を解決すると、置かれたカードのデプロイ時効果が発動する")]
        public async Task SlotSelectResolved_FiresDeployedCardsOwnDeployEffect()
        {
            bool deployEffectFired = false;
            _effects.RegisterComposed(IgnitionCardId, TriggerType.Ignition, new RequestSlotFromRepoOp());
            _effects.Register(DeployCardId, TriggerType.OnDeploy,
                _ => { deployEffectFired = true; return new EffectResult(); });
            var (gameID, _) = await StartWithIgnitionSource(DeployCardId);

            await Ignite(gameID);
            await SelectSlot(gameID, 1, Zones.Frontend, 0);

            deployEffectFired.Should().BeTrue();
        }

        [Fact(DisplayName = "効果デプロイのスロット選択を解決すると、相手のデプロイ反応リアクティブが発動する")]
        public async Task SlotSelectResolved_FiresOpponentDeployWatcher()
        {
            string? watchedInstanceId = null;
            _effects.RegisterComposed(IgnitionCardId, TriggerType.Ignition, new RequestSlotFromRepoOp());
            _effects.Register(WatcherCardId, TriggerType.OnDeploy,
                ctx => { watchedInstanceId = ctx.Target?.InstanceID; return new EffectResult(); });
            var (gameID, state) = await StartWithIgnitionSource(DeployCardId);
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "watcher",
                CardID = WatcherCardId,
                DeployOrder = 1,
            };

            await Ignite(gameID);
            await SelectSlot(gameID, 1, Zones.Frontend, 0);

            watchedInstanceId.Should().Be(state.Player1Field.Frontend[0]!.InstanceID);
        }

        [Fact(DisplayName = "デッキから自動配置する効果で表向きに置かれると、置かれたカードのデプロイ時効果が発動し稼働実績が立つ")]
        public async Task ImmediateDeployFromRepo_FiresDeployEffectAndSetsHasOperated()
        {
            bool deployEffectFired = false;
            _effects.RegisterComposed(IgnitionCardId, TriggerType.Ignition, new DeployFromRepoOp());
            _effects.Register(DeployCardId, TriggerType.OnDeploy,
                _ => { deployEffectFired = true; return new EffectResult(); });
            var (gameID, state) = await StartWithIgnitionSource(DeployCardId);

            await Ignite(gameID);

            deployEffectFired.Should().BeTrue();
            state.Player1HasOperated.Should().BeTrue();
        }

        [Fact(DisplayName = "盤面に表向きリソースがないプレイヤーのデプロイが相手リアクティブでキャンセルされると、稼働実績は立たずゲームが続行する")]
        public async Task CancelledDeploy_LeavesHasOperatedFalse_AndGameContinues()
        {
            _effects.RegisterComposed(IgnitionCardId, TriggerType.Ignition, new RequestSlotFromRepoOp());
            _effects.Register(WatcherCardId, TriggerType.OnDeploy,
                _ => new EffectResult { ShouldCancelAction = true });
            var (gameID, state) = await StartWithIgnitionSource(DeployCardId);
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "watcher",
                CardID = WatcherCardId,
                DeployOrder = 1,
            };

            await Ignite(gameID);
            var result = await SelectSlot(gameID, 1, Zones.Frontend, 0);

            state.Player1Field.Frontend[0].Should().BeNull();
            state.Player1Trash.Should().Contain(c => c.CardID == DeployCardId);
            state.Player1HasOperated.Should().BeFalse();
            result.GameOver.Should().BeNull();
        }

        [Fact(DisplayName = "効果デプロイだけで稼働したプレイヤーが自分の 3 ターン目を終えても、ローンチ失敗で敗北しない")]
        public async Task DeployedOnlyByEffect_SurvivesLaunchFailureCheck()
        {
            _effects.RegisterComposed(IgnitionCardId, TriggerType.Ignition, new RequestSlotFromRepoOp());
            var (gameID, state) = await StartWithIgnitionSource(DeployCardId);
            state.CurrentTurn = 5;

            await Ignite(gameID);
            await SelectSlot(gameID, 1, Zones.Frontend, 0);
            await EndTurn(gameID, 1);

            state.ActivePlayer.Should().Be(2);
            (await _repo.GetGame(gameID))!.Status.Should().Be(GameStatus.Playing);
        }

        [Fact(DisplayName = "効果デプロイだけで稼働したプレイヤーの表向きリソースが全滅すると、システムダウンで敗北する")]
        public async Task DeployedOnlyByEffect_AllResourcesDestroyed_LosesBySystemDown()
        {
            _effects.RegisterComposed(IgnitionCardId, TriggerType.Ignition, new RequestSlotFromRepoOp());
            var (gameID, state) = await StartWithIgnitionSource(DeployCardId);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: AttackerCardId, instanceId: "atk", faceUp: true, maxTP: 2000, currentTP: 2000);

            await Ignite(gameID);
            await SelectSlot(gameID, 1, Zones.Frontend, 0);
            await EndTurn(gameID, 1);
            await Act(gameID, 2, ActionType.EndPhase, new object());
            var result = await Act(gameID, 2, ActionType.Attack, new AttackRequest
            {
                AttackerInstanceID = "atk",
                TargetInstanceID = state.Player1Field.Frontend[0]!.InstanceID,
            });

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(2);
            result.GameOver.Reason.Should().Be(WinReason.SystemDown.ToWireString());
        }

        [Fact(DisplayName = "カウントダウン完了のデプロイが相手リアクティブでキャンセルされると、装備中のアタッチメントもトラッシュへ送られる")]
        public async Task CancelledCountdownDeploy_TrashesAttachmentsToo()
        {
            _effects.Register(WatcherCardId, TriggerType.OnDeploy,
                _ => new EffectResult { ShouldCancelAction = true });
            var (gameID, state) = await StartWithIgnitionSource(DeployCardId);
            state.Player1Repository =
            [
                new UndeployedCard { InstanceID = "r_1", CardID = DeployCardId },
                new UndeployedCard { InstanceID = "r_2", CardID = DeployCardId },
                new UndeployedCard { InstanceID = "r_3", CardID = DeployCardId },
            ];
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "watcher",
                CardID = WatcherCardId,
                DeployOrder = 1,
            };

            var slowCardInstanceId = TestFactory.ReplaceFirstHandCard(state, 1, SlowDeployCardId);
            await Act(gameID, 1, ActionType.PlayCard, new PlayCardRequest
            {
                CardInstanceID = slowCardInstanceId,
                Zone = Zones.Frontend,
                Index = 0,
            });
            string deployingInstanceId = state.Player1Field.Frontend[0]!.InstanceID;

            var attachmentInstanceId = TestFactory.ReplaceFirstHandCard(state, 1, AttachmentCardId);
            await Act(gameID, 1, ActionType.PlayCard, new PlayCardRequest
            {
                CardInstanceID = attachmentInstanceId,
                Zone = Zones.Support,
                Index = 1,
                TargetInstanceID = deployingInstanceId,
            });

            await EndTurn(gameID, 1);
            await EndTurn(gameID, 2);

            state.Player1Field.Frontend[0].Should().BeNull();
            state.Player1Field.Support[1].Should().BeNull();
            state.Player1Trash.Should().Contain(c => c.CardID == SlowDeployCardId);
            state.Player1Trash.Should().Contain(c => c.CardID == AttachmentCardId);
        }

        [Fact(DisplayName = "解決までに空きスロットが埋まった選択待ちは不発になり、取り出したカードがデッキに戻る")]
        public async Task RemainingSlotSelect_NoSlotLeftAtResolution_ReturnsCardToRepository()
        {
            _effects.RegisterComposed(IgnitionCardId, TriggerType.Ignition,
                new RequestSlotFromRepoOp(), new RequestSlotFromRepoOp());
            var (gameID, state) = await StartWithIgnitionSource(DeployCardId);
            state.Player1Repository =
            [
                new UndeployedCard { InstanceID = "r_1", CardID = DeployCardId },
                new UndeployedCard { InstanceID = "r_2", CardID = DeployCardId },
            ];
            FillAllSlotsExceptFrontendZero(state);

            await Ignite(gameID);
            state.PendingSlotSelects.Should().HaveCount(2);

            var result = await SelectSlot(gameID, 1, Zones.Frontend, 0);

            state.PendingSlotSelects.Should().BeEmpty();
            result.ShouldSelectSlot.Should().BeFalse();
            state.Player1Repository.Should().ContainSingle(c => c.CardID == DeployCardId);
        }

        /// <summary>フロントエンドの先頭スロットだけを空けて、プレイヤー 1 の盤面を埋める。</summary>
        /// <param name="state">対象のゲーム状態。</param>
        private static void FillAllSlotsExceptFrontendZero(BattleGameState state)
        {
            for (int i = 1; i < BattleConstants.SlotsPerZone; i++)
            {
                state.Player1Field.Frontend[i] = TestFactory.MakeResource(
                    cardId: DeployCardId, instanceId: $"fe_{i}", faceUp: true);
            }
            for (int i = 0; i < BattleConstants.SlotsPerZone; i++)
            {
                state.Player1Field.Backend[i] = TestFactory.MakeResource(
                    cardId: DeployCardId, instanceId: $"be_{i}", faceUp: true);
            }
        }
    }

    [Trait("対象", "アクションゲートと入口経由のディスパッチ")]
    public class EntryActionGate : Base
    {
        private const string ChoiceCardId = "TST-0700";

        private sealed class InlineOp(Action<OpContext> action) : IEffectOp
        {
            public void Execute(OpContext ctx) => action(ctx);
        }

        private static PendingEffectChoice MakePending(long chooser) => new()
        {
            ChooserPlayerNum = chooser,
            OwnerPlayerNum = chooser,
            EffectCardId = ChoiceCardId,
            EffectInstanceId = "src_choice",
            Trigger = TriggerType.Ignition,
            ChoiceKey = "instanceId",
            ChoiceKind = ChoiceKinds.FieldTarget,
            Candidates = ["cand_1"],
        };

        [Fact(DisplayName = "効果中選択の待ちがある選択者が別のアクションを送ると、GameRuleException になる")]
        public async Task PendingChoice_ChooserSendsOtherAction_Throws()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            state!.PendingEffectChoice = MakePending(1);

            game = await _repo.GetGame(gameID);
            var act = () => _engine.ProcessAction(game!, 1, ActionType.EndPhase, new object());

            await act.Should().ThrowAsync<GameRuleException>().WithMessage("*reactive choice required*");
        }

        [Fact(DisplayName = "相手が効果中選択の選択者のとき、ターンプレイヤーの攻撃は拒否される")]
        public async Task PendingChoice_OpponentIsChooser_BlocksTurnPlayerAttack()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            state!.CurrentPhase = Phase.Battle;
            state.CurrentTurn = 2;
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def", faceUp: true);
            // 選択者は非手番の P2
            state.PendingEffectChoice = MakePending(2);

            game = await _repo.GetGame(gameID);
            var act = () => _engine.ProcessAction(game!, 1, ActionType.Attack,
                new AttackRequest { AttackerInstanceID = "atk", TargetInstanceID = "def" });

            await act.Should().ThrowAsync<GameRuleException>().WithMessage("*reactive choice required*");
        }

        [Fact(DisplayName = "相手が効果中選択の選択者のとき、ターンプレイヤーのフェーズ終了は拒否される")]
        public async Task PendingChoice_OpponentIsChooser_BlocksTurnPlayerEndPhase()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            state!.PendingEffectChoice = MakePending(2);

            game = await _repo.GetGame(gameID);
            var act = () => _engine.ProcessAction(game!, 1, ActionType.EndPhase, new object());

            await act.Should().ThrowAsync<GameRuleException>().WithMessage("*reactive choice required*");
        }

        [Fact(DisplayName = "選択者以外が効果中選択を解決しようとすると拒否される")]
        public async Task PendingChoice_NonChooserResolve_IsRejected()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            state!.PendingEffectChoice = MakePending(2);

            game = await _repo.GetGame(gameID);
            var act = () => _engine.ProcessAction(game!, 1, ActionType.ResolvePendingChoice,
                new ResolvePendingChoiceRequest { ChosenId = "cand_1" });

            await act.Should().ThrowAsync<GameRuleException>().WithMessage("*different player*");
        }

        /// <summary>効果中選択を最後まで解決できるハンドラを備えたエンジンとゲームを用意する。</summary>
        /// <returns>エンジンとゲーム ID。</returns>
        private async Task<(GameEngine Engine, string GameID)> SetupChoiceResolvableGame()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            cc.Add(TestFactory.ComputeCard(cardId: ChoiceCardId));
            var effects = new EffectRegistry();
            effects.RegisterComposed(ChoiceCardId, TriggerType.Ignition,
                new InlineOp(octx => new GainInsightOp(new StaticAmount(300)).Execute(octx)));
            var engine = new GameEngine(_repo, cc, effects, new InitiativeCatalog([]), new FakeClock());
            var deck = TestFactory.MakeDeck(cc, "TST-0001");
            var gameID = await engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await engine.RunAutoAdvance(game!);
            return (engine, gameID);
        }

        private static AwaitingSlotSelect MakeAwaitingSlotSelect(long playerNum) => new()
        {
            PlayerNum = playerNum,
            Resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "pending_1"),
            SourceZone = SlotSelectSources.Repository,
        };

        [Fact(DisplayName = "相手ターン中でも、選択者は効果中選択の解決アクションを送れる")]
        public async Task PendingChoice_ChooserResolves_EvenOnOpponentTurn()
        {
            var (engine, gameID) = await SetupChoiceResolvableGame();

            var state = await _repo.GetGameState(gameID);
            // Turn 1 のアクティブプレイヤーは 1。選択者は非アクティブなプレイヤー 2。
            state!.PendingEffectChoice = MakePending(2);

            var game = await _repo.GetGame(gameID);
            await engine.ProcessAction(
                game!, 2, ActionType.ResolvePendingChoice,
                new ResolvePendingChoiceRequest { ChosenId = "cand_1" });

            state.PendingEffectChoice.Should().BeNull("resolving the choice should clear the pending state");
        }

        [Fact(DisplayName = "スロット選択と効果中選択が同時に待っているとき、選択者は効果中選択を解決でき、スロット選択は残る")]
        public async Task BothPending_ChooserResolvesEffectChoiceFirst()
        {
            var (engine, gameID) = await SetupChoiceResolvableGame();

            var state = await _repo.GetGameState(gameID);
            state!.PendingEffectChoice = MakePending(2);
            state.PendingSlotSelects.Add(MakeAwaitingSlotSelect(1));

            var game = await _repo.GetGame(gameID);
            await engine.ProcessAction(
                game!, 2, ActionType.ResolvePendingChoice,
                new ResolvePendingChoiceRequest { ChosenId = "cand_1" });

            state.PendingEffectChoice.Should().BeNull();
            state.PendingSlotSelects.Should().ContainSingle();
        }

        [Fact(DisplayName = "スロット選択と効果中選択が同時に待っているとき、スロット選択は効果中選択を解決するまで拒否される")]
        public async Task BothPending_SelectSlotRejectedUntilChoiceResolved()
        {
            var (engine, gameID) = await SetupChoiceResolvableGame();

            var state = await _repo.GetGameState(gameID);
            state!.PendingEffectChoice = MakePending(2);
            state.PendingSlotSelects.Add(MakeAwaitingSlotSelect(1));

            var game = await _repo.GetGame(gameID);
            var act = () => engine.ProcessAction(game!, 1, ActionType.SelectSlot,
                new SelectSlotRequest { Zone = Zones.Frontend, Index = 0 });

            await act.Should().ThrowAsync<GameRuleException>().WithMessage("*reactive choice required*");
        }

        [Fact(DisplayName = "メインフェーズに攻撃を送ると、GameRuleException になる")]
        public async Task MainPhase_Attack_Throws()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            state!.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "atk_1", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "def_1", faceUp: true);

            game = await _repo.GetGame(gameID);
            var act = () => _engine.ProcessAction(
                game!, 1, ActionType.Attack,
                new AttackRequest { AttackerInstanceID = "atk_1", TargetInstanceID = "def_1" });

            await act.Should().ThrowAsync<GameRuleException>().WithMessage("*not allowed in phase*");
        }

        [Fact(DisplayName = "バトルフェーズにカードプレイを送ると、GameRuleException になる")]
        public async Task BattlePhase_PlayCard_Throws()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            state!.CurrentTurn = 2;
            state.CurrentPhase = Phase.Battle;
            var cardToPlay = state.Player1Hand.First();

            game = await _repo.GetGame(gameID);
            var act = () => _engine.ProcessAction(
                game!, 1, ActionType.PlayCard,
                new PlayCardRequest { CardInstanceID = cardToPlay.InstanceID, Zone = Zones.Frontend, Index = 0 });

            await act.Should().ThrowAsync<GameRuleException>().WithMessage("*not allowed in phase*");
        }

        [Fact(DisplayName = "エンジン経由でスケールアップを処理すると、ランクが上がる")]
        public async Task ScaleUp_ThroughEngine_RaisesRank()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "res_1", rank: Rank.Small, faceUp: true);
            resource.DeployedOnTurn = 1;
            state!.Player1Field.Frontend[0] = resource;

            game = await _repo.GetGame(gameID);
            var result = await _engine.ProcessAction(
                game!, 1, ActionType.ScaleUp,
                new ScaleUpRequest { InstanceID = "res_1", TargetRank = "medium", InstanceFamily = "M" });

            result.Events.Should().Contain(e => e.EventType == ActionTypes.ScaleUp);
            resource.Rank.Should().Be(Rank.Medium);
        }

        [Fact(DisplayName = "エンジン経由で収益化を処理すると、バジェットが増えインサイトプールが減る")]
        public async Task Monetize_ThroughEngine_ConvertsInsightToBudget()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            state!.CurrentTurn = 2;
            var resource = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "be_1", faceUp: true);
            state.Player1Field.Backend[0] = resource;
            state.SetInsightPool(1, 500);
            var budgetBefore = state.GetBudget(1);

            game = await _repo.GetGame(gameID);
            var result = await _engine.ProcessAction(
                game!, 1, ActionType.Monetize,
                new MonetizeRequest { Distributions = [new MonetizeDistribution { InstanceID = "be_1", Amount = 300 }] });

            result.Events.Should().Contain(e => e.EventType == ActionTypes.Monetize);
            state.GetInsightPool(1).Should().Be(200);
            state.GetBudget(1).Should().Be(budgetBefore + 300);
        }

        [Fact(DisplayName = "エンジン経由で起動効果を処理すると、効果が適用される")]
        public async Task UseIgnition_ThroughEngine_AppliesEffect()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
            var effects = new EffectRegistry();
            effects.RegisterComposed("TST-0001", TriggerType.Ignition, new GainBudgetOp(PlayerRef.Myself, new StaticAmount(500)));
            var engine = new GameEngine(_repo, cc, effects, new InitiativeCatalog([]), new FakeClock());
            var deck = TestFactory.MakeDeck(cc, "TST-0001");
            var gameID = await engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            state!.Player1Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "src_1", faceUp: true);
            var budgetBefore = state.GetBudget(1);

            game = await _repo.GetGame(gameID);
            var result = await engine.ProcessAction(
                game!, 1, ActionType.UseIgnition,
                new UseIgnitionRequest { InstanceID = "src_1" });

            result.Events.Should().Contain(e => e.EventType == ActionTypes.UseIgnition);
            state.GetBudget(1).Should().Be(budgetBefore + 500);
        }

        [Fact(DisplayName = "エンジン経由で施策を処理すると、施策が使用済みになる")]
        public async Task UseInitiative_ThroughEngine_MarksRoutineUsed()
        {
            const string ProductId = "PD-TST";
            const string RoutineId = "IN-TST-R";
            var jsonOpts = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                PropertyNameCaseInsensitive = true,
            };
            var effectDef = System.Text.Json.JsonSerializer.Deserialize<EffectDef>(
                """{"ops":[{"gain_budget":{"target":"myself","amount":50}}]}""", jsonOpts)!;
            var initiative = new Initiative
            {
                InitiativeId = RoutineId,
                ProductId = ProductId,
                Kind = InitiativeKinds.Routine,
                Name = "R",
                InsightCost = 100,
                Effect = effectDef,
            };
            var registry = new EffectRegistry();
            var customs = new CustomEffectRegistry();
            InitiativeEffects.LoadIntoRegistry([initiative], registry, customs);
            var catalog = new InitiativeCatalog([initiative]);
            var engine = new GameEngine(_repo, _cc, registry, catalog, new FakeClock());

            var deck = MakeDeckWith("TST-0001");
            var gameID = await engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await engine.RunAutoAdvance(game!);

            var state = await _repo.GetGameState(gameID);
            state!.CurrentTurn = 3;
            state.Player1RoutineId = RoutineId;
            state.SetInsightPool(1, 1000);

            game = await _repo.GetGame(gameID);
            var result = await engine.ProcessAction(
                game!, 1, ActionType.UseInitiative,
                new UseInitiativeRequest { Kind = InitiativeKinds.Routine });

            result.Events.Should().Contain(e => e.EventType == ActionTypes.UseInitiative);
            state.GetRoutineUsedThisTurn(1).Should().BeTrue();
        }
    }

    [Trait("対象", "投了と自動進行の残分岐")]
    public class ForfeitAndAutoAdvanceEdgeCases : Base
    {
        [Fact(DisplayName = "終了済みゲームを投了すると、GameRuleException になる")]
        public async Task Forfeit_FinishedGame_Throws()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);
            await _engine.Forfeit(game!, 1, WinReason.Surrender);

            game = await _repo.GetGame(gameID);
            var act = () => _engine.Forfeit(game!, 1, WinReason.Surrender);

            await act.Should().ThrowAsync<GameRuleException>().WithMessage("*not in playing state*");
        }

        [Fact(DisplayName = "プレイヤー 2 が投了すると、プレイヤー 1 の勝ち・勝因 surrender で終了する")]
        public async Task Forfeit_Player2_Player1WinsWithSurrender()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);
            var game = await _repo.GetGame(gameID);

            var result = await _engine.Forfeit(game!, 2, WinReason.Surrender);

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(1);
            result.GameOver.Reason.Should().Be(WinReasons.Surrender);

            game = await _repo.GetGame(gameID);
            game!.Status.Should().Be(GameStatus.Finished);
        }

        [Fact(DisplayName = "デッキが空のプレイヤーのドローフェーズ自動進行は、相手の勝ち・勝因 deck_out で決着しゲームが finished で保存される")]
        public async Task RunAutoAdvance_EmptyDeck_OpponentWinsWithDeckOut()
        {
            var deck = MakeDeckWith("TST-0001");
            var gameID = await _engine.CreateNewGame(deck, deck, 1);

            var state = await _repo.GetGameState(gameID);
            state!.Player1Repository = [];

            var game = await _repo.GetGame(gameID);
            var result = await _engine.RunAutoAdvance(game!);

            result.Should().NotBeNull();
            result!.WinnerNum.Should().Be(2);
            result.Reason.Should().Be(WinReasons.DeckOut);

            game = await _repo.GetGame(gameID);
            game!.Status.Should().Be(GameStatus.Finished);
        }
    }
}
