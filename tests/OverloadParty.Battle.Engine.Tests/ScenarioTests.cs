using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Tests.Fakes;

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
            _engine = new GameEngine(_repo, _cc, effects, new InitiativeCatalog([]));
        }

        [Fact(DisplayName = "デプロイターン 2 のリソースは所有者の 2 回目のドローフェーズで表向きになり、デプロイ時効果が発動する")]
        public async Task BecomesOperational_AfterOwnersSecondDrawPhase()
        {
            var gameID = await _engine.CreateNewGame(
                TestFactory.MakeDeck("TST-0007"), TestFactory.MakeDeck("TST-0001"), firstPlayer: 1);

            // P1 ターン 1: ドローフェーズを通過し、メインフェーズでデプロイターン 2 のカードをデプロイする。
            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);
            var state = await _repo.GetGameState(gameID);
            var card = state!.Player1Hand.First(c => c.CardID == "TST-0007");
            game = await _repo.GetGame(gameID);
            await _engine.ProcessAction(game!, 1, ActionType.PlayCard,
                new PlayCardRequest { CardInstanceID = card.InstanceID, Zone = Zones.Frontend, Index = 0 });

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
}
