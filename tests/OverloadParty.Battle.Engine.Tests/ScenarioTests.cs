using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// バトル開始から実ルールのターン進行を回し、ターンをまたぐ振る舞いを検証するシナリオテスト。
/// プロセッサを直接複数回叩くのではなく、GameEngine 経由でドローフェーズとフェーズ終了を実際に進める。
/// </summary>
public class ScenarioTests
{
    [Trait("対象", "デプロイターンの経過と稼働")]
    public class TwoTurnDeploy
    {
        private readonly FakeGameRepository _repo = new();
        private readonly TestCardCache _cc = new();
        private readonly GameEngine _engine;
        private bool _onDeployFired;

        /// <summary>デプロイターン 2 のリソースと、そのデプロイ時効果を登録したエンジンを用意する。</summary>
        public TwoTurnDeploy()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0007", deployTurns: 2));
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));
            var effects = new TestEffectRegistry();
            effects.Register("TST-0007", TriggerType.OnDeploy, _ => { _onDeployFired = true; return new EffectResult(); });
            _engine = new GameEngine(_repo, _cc, effects, new InitiativeCatalog([]), new FakeClock());
        }

        [Fact(DisplayName = "デプロイターン 2 のリソースは所有者の 2 回目のドローフェーズで表向きになり、デプロイ時効果が発動する")]
        public async Task BecomesOperational_AfterOwnersSecondDrawPhase()
        {
            var gameID = await _engine.CreateNewGame(
                TestFactory.MakeDeck(_cc, "TST-0007"), TestFactory.MakeDeck(_cc, "TST-0001"), firstPlayer: 1);

            // P1 ターン 1: ドローフェーズを通過し、メインフェーズでデプロイターン 2 のカードをデプロイする。
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);
            var state = (await _repo.GetGameState(gameID))!;
            var cardInstanceId = TestFactory.ReplaceFirstHandCard(state, 1, "TST-0007");
            game = await _repo.GetGame(gameID);
            await _engine.ProcessAction(game!, 1, ActionType.PlayCard,
                new PlayCardRequest { CardInstanceID = cardInstanceId, Zone = Zones.Frontend, Index = 0 });

            // FakeGameRepository は状態をその場で更新するため、この参照は以降の進行でも生き続ける。
            var deployed = state.Player1Field.Frontend[0]!;
            deployed.DeployingTurnsLeft.Should().Be(2);
            deployed.FaceUp.Should().BeFalse("デプロイ直後は裏向きで稼働前");
            _onDeployFired.Should().BeFalse();

            // 1 ラウンド進める (P1 → P2)。手番が P1 に戻る際のドローフェーズでデプロイ完了へ 1 ターン近づく。
            await EndTurn(gameID, 1);
            await EndTurn(gameID, 2);
            deployed.DeployingTurnsLeft.Should().Be(1, "所有者の 1 回目のドローフェーズではデプロイ完了に届かず稼働しない");
            deployed.FaceUp.Should().BeFalse();
            _onDeployFired.Should().BeFalse();

            // もう 1 ラウンド進める。手番が P1 に戻る際のドローフェーズでデプロイが完了し稼働する。
            await EndTurn(gameID, 1);
            await EndTurn(gameID, 2);
            deployed.DeployingTurnsLeft.Should().Be(0);
            deployed.FaceUp.Should().BeTrue("デプロイターン 2 のリソースは所有者の 2 回目のドローフェーズで稼働する");
            _onDeployFired.Should().BeTrue("デプロイ完了時にデプロイ時効果が発動する");
        }

        /// <summary>指定プレイヤーのターンを実ルールどおり終了させ、手札が上限を超える場合は超過分を破棄する。</summary>
        /// <param name="gameID">対象ゲームの ID。</param>
        /// <param name="player">ターンを終了するプレイヤー番号。</param>
        private async Task EndTurn(string gameID, long player)
        {
            while (true)
            {
                var game = await _repo.GetGame(gameID);
                var result = await _engine.ProcessAction(game!, player, ActionType.EndPhase, new object());

                if (result.ShouldDiscard)
                {
                    var state = await _repo.GetGameState(gameID);
                    var hand = state!.GetHand(player);
                    var discardIds = hand.Take(hand.Count - BattleConstants.HandLimit)
                        .Select(c => c.InstanceID).ToArray();
                    game = await _repo.GetGame(gameID);
                    await _engine.ProcessAction(game!, player, ActionType.DiscardHand,
                        new DiscardHandRequest { CardInstanceIDs = [.. discardIds] });
                    return;
                }

                var current = await _repo.GetGameState(gameID);
                if (current!.ActivePlayer != player)
                {
                    return;
                }
            }
        }
    }

    [Trait("対象", "ターンリミットの判定契機")]
    public class TurnLimitTiming
    {
        private const string FreeCardId = "TST-0001";
        private const string HighMaintenanceCardId = "TST-0011";

        private readonly FakeGameRepository _repo = new();
        private readonly TestCardCache _cc = new();
        private readonly GameEngine _engine;

        /// <summary>維持コストのかからないリソースと維持コスト 300 のリソースを登録したエンジンを用意する。</summary>
        public TurnLimitTiming()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: FreeCardId, mc: 0, deployTurns: 0));
            _cc.Add(TestFactory.ComputeCard(cardId: HighMaintenanceCardId, mc: 300, deployTurns: 0));
            _engine = new GameEngine(_repo, _cc, new EffectRegistry(), new InitiativeCatalog([]), new FakeClock());
        }

        /// <summary>
        /// 指定ターンのバトルフェーズから始まる盤面を用意する。
        /// ターンリミットの判定契機だけを観測できるよう、両者を稼働中にして他の敗北条件が成立しない状態にする。
        /// </summary>
        /// <param name="turn">開始する全体ターン数。</param>
        /// <param name="activePlayer">ターンプレイヤーの番号。</param>
        /// <param name="p1Budget">プレイヤー 1 のバジェット。</param>
        /// <param name="p2Budget">プレイヤー 2 のバジェット。</param>
        /// <returns>用意したゲームの ID と、進行に伴って更新され続けるゲーム状態。</returns>
        private async Task<(string GameID, BattleGameState State)> StartBattlePhaseAt(
            long turn, long activePlayer, long p1Budget, long p2Budget)
        {
            var gameID = await _engine.CreateNewGame(
                TestFactory.MakeDeck(_cc, FreeCardId), TestFactory.MakeDeck(_cc, FreeCardId), firstPlayer: 1);

            var state = (await _repo.GetGameState(gameID))!;
            state.CurrentTurn = turn;
            state.ActivePlayer = activePlayer;
            state.CurrentPhase = Phase.Battle;
            state.Player1Budget = p1Budget;
            state.Player2Budget = p2Budget;
            state.SetHasOperated(1, true);
            state.SetHasOperated(2, true);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: FreeCardId, instanceId: "r_p1", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: FreeCardId, instanceId: "r_p2", faceUp: true);

            return (gameID, state);
        }

        /// <summary>指定プレイヤーのアクションを実行する。</summary>
        /// <param name="gameID">対象ゲームの ID。</param>
        /// <param name="playerNum">アクションするプレイヤー番号。</param>
        /// <param name="actionType">アクション種別。</param>
        /// <param name="actionData">アクション固有のリクエスト。</param>
        /// <returns>アクション結果。</returns>
        private async Task<ActionResult> Act(
            string gameID, long playerNum, ActionType actionType, object actionData)
        {
            var game = await _repo.GetGame(gameID);
            return await _engine.ProcessAction(game!, playerNum, actionType, actionData);
        }

        [Fact(DisplayName = "ターン 30 のメインフェーズでカードをプレイしたとき、アクション解決後もゲームが続行する")]
        public async Task PlayCardOnFinalTurn_ContinuesGame()
        {
            var (gameID, state) = await StartBattlePhaseAt(
                turn: 30, activePlayer: 2, p1Budget: 3000, p2Budget: 2000);
            state.CurrentPhase = Phase.Main;
            var cardInstanceId = TestFactory.ReplaceFirstHandCard(state, 2, FreeCardId);

            var result = await Act(gameID, 2, ActionType.PlayCard,
                new PlayCardRequest { CardInstanceID = cardInstanceId, Zone = Zones.Frontend, Index = 1 });

            result.GameOver.Should().BeNull();
            state.Player2Field.Frontend[1].Should().NotBeNull("プレイしたカードがデプロイされる");
            state.CurrentTurn.Should().Be(30);
            state.ActivePlayer.Should().Be(2);
        }

        [Fact(DisplayName = "ターン 30 のエンドフェーズが完了したとき、バジェットが 3000 対 2000 なら多い側がターンリミットで勝ち、ターン 31 に交代しない")]
        public async Task EndPhaseOnFinalTurn_HigherBudgetWins()
        {
            var (gameID, state) = await StartBattlePhaseAt(
                turn: 30, activePlayer: 2, p1Budget: 3000, p2Budget: 2000);

            var result = await Act(gameID, 2, ActionType.EndPhase, new object());

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(1);
            result.GameOver.Reason.Should().Be(WinReasons.TurnLimit);
            state.CurrentTurn.Should().Be(30);
            state.ActivePlayer.Should().Be(2);
        }

        [Fact(DisplayName = "ターン 30 のエンドフェーズが完了したとき、バジェットが 2500 対 2500 なら引き分けになる")]
        public async Task EndPhaseOnFinalTurn_EqualBudgetDraws()
        {
            var (gameID, _) = await StartBattlePhaseAt(
                turn: 30, activePlayer: 2, p1Budget: 2500, p2Budget: 2500);

            var result = await Act(gameID, 2, ActionType.EndPhase, new object());

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(0);
            result.GameOver.Reason.Should().Be(WinReasons.Draw);
        }

        [Fact(DisplayName = "ターン 29 のエンドフェーズが完了したとき、ターン 30 に交代しゲームが続行する")]
        public async Task EndPhaseBeforeFinalTurn_AdvancesToFinalTurn()
        {
            var (gameID, state) = await StartBattlePhaseAt(
                turn: 29, activePlayer: 1, p1Budget: 3000, p2Budget: 2000);

            var result = await Act(gameID, 1, ActionType.EndPhase, new object());

            result.GameOver.Should().BeNull();
            state.CurrentTurn.Should().Be(30);
            state.ActivePlayer.Should().Be(2);
        }

        [Fact(DisplayName = "ターン 30 の維持コスト徴収で自分のバジェットが 0 になるとき、ターンリミットではなくバジェットゼロ敗北になる")]
        public async Task BudgetZeroOnFinalTurn_PrecedesTurnLimit()
        {
            var (gameID, state) = await StartBattlePhaseAt(
                turn: 30, activePlayer: 2, p1Budget: 200, p2Budget: 300);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: HighMaintenanceCardId, instanceId: "r_p2", faceUp: true);

            var result = await Act(gameID, 2, ActionType.EndPhase, new object());

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(1);
            result.GameOver.Reason.Should().Be(WinReasons.BudgetZero);
        }

        [Fact(DisplayName = "ターン 30 で手札が上限を超えているとき、破棄を解決すると、ターンリミットが判定される")]
        public async Task HandOverLimitOnFinalTurn_SettlesAfterDiscard()
        {
            var (gameID, state) = await StartBattlePhaseAt(
                turn: 30, activePlayer: 2, p1Budget: 3000, p2Budget: 2000);
            foreach (var i in Enumerable.Range(0, 3))
            {
                state.Player2Hand.Add(new UndeployedCard { InstanceID = $"extra_{i}", CardID = FreeCardId });
            }

            var endPhaseResult = await Act(gameID, 2, ActionType.EndPhase, new object());

            endPhaseResult.ShouldDiscard.Should().BeTrue();
            endPhaseResult.GameOver.Should().BeNull();

            var discardResult = await Act(gameID, 2, ActionType.DiscardHand,
                new DiscardHandRequest { CardInstanceIDs = ["extra_0", "extra_1"] });

            discardResult.GameOver.Should().NotBeNull();
            discardResult.GameOver!.WinnerNum.Should().Be(1);
            discardResult.GameOver.Reason.Should().Be(WinReasons.TurnLimit);
            state.CurrentTurn.Should().Be(30);
        }
    }
}
