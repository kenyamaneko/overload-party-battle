using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Models;

public class GameStateTests
{
    [Trait("対象", "フィールドアクセサ")]
    public class FieldAccessor
    {
        [Fact(DisplayName = "プレイヤー 1 を指定すると、プレイヤー 1 のフィールドを返す")]
        public void GetField_Player1_ReturnsPlayer1Field()
        {
            var gs = TestFactory.MakeGameState();
            gs.GetField(1).Should().BeSameAs(gs.Player1Field);
        }

        [Fact(DisplayName = "プレイヤー 2 を指定すると、プレイヤー 2 のフィールドを返す")]
        public void GetField_Player2_ReturnsPlayer2Field()
        {
            var gs = TestFactory.MakeGameState();
            gs.GetField(2).Should().BeSameAs(gs.Player2Field);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void GetField_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.GetField(3);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "プレイヤー 1 に設定すると、プレイヤー 1 のフィールドが更新される")]
        public void SetField_Player1_UpdatesPlayer1Field()
        {
            var gs = TestFactory.MakeGameState();
            var newField = new Field();
            gs.SetField(1, newField);
            gs.Player1Field.Should().BeSameAs(newField);
        }

        [Fact(DisplayName = "プレイヤー 2 に設定すると、プレイヤー 2 のフィールドが更新される")]
        public void SetField_Player2_UpdatesPlayer2Field()
        {
            var gs = TestFactory.MakeGameState();
            var newField = new Field();
            gs.SetField(2, newField);
            gs.Player2Field.Should().BeSameAs(newField);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void SetField_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.SetField(0, new Field());
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Trait("対象", "手札アクセサ")]
    public class HandAccessor
    {
        [Fact(DisplayName = "プレイヤー 1 を指定すると、プレイヤー 1 の手札を返す")]
        public void GetHand_Player1_ReturnsPlayer1Hand()
        {
            var gs = TestFactory.MakeGameState();
            gs.GetHand(1).Should().BeSameAs(gs.Player1Hand);
        }

        [Fact(DisplayName = "プレイヤー 2 を指定すると、プレイヤー 2 の手札を返す")]
        public void GetHand_Player2_ReturnsPlayer2Hand()
        {
            var gs = TestFactory.MakeGameState();
            gs.GetHand(2).Should().BeSameAs(gs.Player2Hand);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void GetHand_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.GetHand(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "プレイヤー 1 に設定すると、プレイヤー 1 の手札が更新される")]
        public void SetHand_Player1_UpdatesPlayer1Hand()
        {
            var gs = TestFactory.MakeGameState();
            var newHand = new List<UndeployedCard> { new() { InstanceID = "h1" } };
            gs.SetHand(1, newHand);
            gs.Player1Hand.Should().BeSameAs(newHand);
        }

        [Fact(DisplayName = "プレイヤー 2 に設定すると、プレイヤー 2 の手札が更新される")]
        public void SetHand_Player2_UpdatesPlayer2Hand()
        {
            var gs = TestFactory.MakeGameState();
            var newHand = new List<UndeployedCard> { new() { InstanceID = "h2" } };
            gs.SetHand(2, newHand);
            gs.Player2Hand.Should().BeSameAs(newHand);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void SetHand_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.SetHand(3, []);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Trait("対象", "バジェットアクセサ")]
    public class BudgetAccessor
    {
        [Theory(DisplayName = "指定したプレイヤーのバジェットを返す")]
        [InlineData(1, 5000)]
        [InlineData(2, 5000)]
        public void GetBudget_ReturnsCorrectPlayerBudget(long playerNum, long expected)
        {
            var gs = TestFactory.MakeGameState(p1Budget: 5000, p2Budget: 5000);
            gs.GetBudget(playerNum).Should().Be(expected);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void GetBudget_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.GetBudget(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "プレイヤー 1 に設定すると、プレイヤー 1 のバジェットが更新される")]
        public void SetBudget_Player1_Updates()
        {
            var gs = TestFactory.MakeGameState();
            gs.SetBudget(1, 3000);
            gs.Player1Budget.Should().Be(3000);
        }

        [Fact(DisplayName = "プレイヤー 2 に設定すると、プレイヤー 2 のバジェットが更新される")]
        public void SetBudget_Player2_Updates()
        {
            var gs = TestFactory.MakeGameState();
            gs.SetBudget(2, 4000);
            gs.Player2Budget.Should().Be(4000);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void SetBudget_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.SetBudget(3, 100);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Trait("対象", "インサイトプールアクセサ")]
    public class InsightPoolAccessor
    {
        [Fact(DisplayName = "プレイヤー 1 を指定すると、プレイヤー 1 のインサイトプールを返す")]
        public void GetInsightPool_Player1_ReturnsValue()
        {
            var gs = TestFactory.MakeGameState();
            gs.Player1InsightPool = 10;
            gs.GetInsightPool(1).Should().Be(10);
        }

        [Fact(DisplayName = "プレイヤー 2 を指定すると、プレイヤー 2 のインサイトプールを返す")]
        public void GetInsightPool_Player2_ReturnsValue()
        {
            var gs = TestFactory.MakeGameState();
            gs.Player2InsightPool = 20;
            gs.GetInsightPool(2).Should().Be(20);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void GetInsightPool_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.GetInsightPool(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "プレイヤー 1 に設定すると、プレイヤー 1 のインサイトプールが更新される")]
        public void SetInsightPool_Player1_Updates()
        {
            var gs = TestFactory.MakeGameState();
            gs.SetInsightPool(1, 15);
            gs.Player1InsightPool.Should().Be(15);
        }

        [Fact(DisplayName = "プレイヤー 2 に設定すると、プレイヤー 2 のインサイトプールが更新される")]
        public void SetInsightPool_Player2_Updates()
        {
            var gs = TestFactory.MakeGameState();
            gs.SetInsightPool(2, 25);
            gs.Player2InsightPool.Should().Be(25);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void SetInsightPool_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.SetInsightPool(3, 5);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Trait("対象", "Repository アクセサ")]
    public class RepositoryAccessor
    {
        [Fact(DisplayName = "プレイヤー 1 を指定すると、プレイヤー 1 の Repository を返す")]
        public void GetRepository_Player1_ReturnsPlayer1Repository()
        {
            var gs = TestFactory.MakeGameState();
            gs.GetRepository(1).Should().BeSameAs(gs.Player1Repository);
        }

        [Fact(DisplayName = "プレイヤー 2 を指定すると、プレイヤー 2 の Repository を返す")]
        public void GetRepository_Player2_ReturnsPlayer2Repository()
        {
            var gs = TestFactory.MakeGameState();
            gs.GetRepository(2).Should().BeSameAs(gs.Player2Repository);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void GetRepository_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.GetRepository(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "プレイヤー 1 に設定すると、プレイヤー 1 の Repository が更新される")]
        public void SetRepository_Player1_Updates()
        {
            var gs = TestFactory.MakeGameState();
            var repo = new List<UndeployedCard> { new() { InstanceID = "r1" } };
            gs.SetRepository(1, repo);
            gs.Player1Repository.Should().BeSameAs(repo);
        }

        [Fact(DisplayName = "プレイヤー 2 に設定すると、プレイヤー 2 の Repository が更新される")]
        public void SetRepository_Player2_Updates()
        {
            var gs = TestFactory.MakeGameState();
            var repo = new List<UndeployedCard> { new() { InstanceID = "r2" } };
            gs.SetRepository(2, repo);
            gs.Player2Repository.Should().BeSameAs(repo);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void SetRepository_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.SetRepository(3, []);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Trait("対象", "トラッシュアクセサ")]
    public class TrashAccessor
    {
        [Fact(DisplayName = "プレイヤー 1 を指定すると、プレイヤー 1 のトラッシュを返す")]
        public void GetTrash_Player1_ReturnsPlayer1Trash()
        {
            var gs = TestFactory.MakeGameState();
            gs.GetTrash(1).Should().BeSameAs(gs.Player1Trash);
        }

        [Fact(DisplayName = "プレイヤー 2 を指定すると、プレイヤー 2 のトラッシュを返す")]
        public void GetTrash_Player2_ReturnsPlayer2Trash()
        {
            var gs = TestFactory.MakeGameState();
            gs.GetTrash(2).Should().BeSameAs(gs.Player2Trash);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void GetTrash_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.GetTrash(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "プレイヤー 1 に設定すると、プレイヤー 1 のトラッシュが更新される")]
        public void SetTrash_Player1_Updates()
        {
            var gs = TestFactory.MakeGameState();
            var trash = new List<UndeployedCard> { new() { InstanceID = "t1" } };
            gs.SetTrash(1, trash);
            gs.Player1Trash.Should().BeSameAs(trash);
        }

        [Fact(DisplayName = "プレイヤー 2 に設定すると、プレイヤー 2 のトラッシュが更新される")]
        public void SetTrash_Player2_Updates()
        {
            var gs = TestFactory.MakeGameState();
            var trash = new List<UndeployedCard> { new() { InstanceID = "t2" } };
            gs.SetTrash(2, trash);
            gs.Player2Trash.Should().BeSameAs(trash);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void SetTrash_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.SetTrash(3, []);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Trait("対象", "タイムバンクアクセサ")]
    public class TimeBankAccessor
    {
        [Fact(DisplayName = "プレイヤー 1 を指定すると、プレイヤー 1 のタイムバンクを返す")]
        public void GetTimeBank_Player1_ReturnsValue()
        {
            var gs = TestFactory.MakeGameState();
            gs.GetTimeBank(1).Should().Be(480);
        }

        [Fact(DisplayName = "プレイヤー 2 を指定すると、プレイヤー 2 のタイムバンクを返す")]
        public void GetTimeBank_Player2_ReturnsValue()
        {
            var gs = TestFactory.MakeGameState();
            gs.GetTimeBank(2).Should().Be(480);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void GetTimeBank_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.GetTimeBank(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "プレイヤー 1 に設定すると、プレイヤー 1 のタイムバンクが更新される")]
        public void SetTimeBank_Player1_Updates()
        {
            var gs = TestFactory.MakeGameState();
            gs.SetTimeBank(1, 300);
            gs.Player1TimeBank.Should().Be(300);
        }

        [Fact(DisplayName = "プレイヤー 2 に設定すると、プレイヤー 2 のタイムバンクが更新される")]
        public void SetTimeBank_Player2_Updates()
        {
            var gs = TestFactory.MakeGameState();
            gs.SetTimeBank(2, 200);
            gs.Player2TimeBank.Should().Be(200);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void SetTimeBank_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.SetTimeBank(3, 100);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Trait("対象", "インシデント使用フラグアクセサ")]
    public class IncidentPlayedThisTurnAccessor
    {
        [Fact(DisplayName = "初期状態では両プレイヤーのインシデント使用フラグが false になる")]
        public void GetIncidentPlayedThisTurn_DefaultsFalse()
        {
            var gs = TestFactory.MakeGameState();
            gs.GetIncidentPlayedThisTurn(1).Should().BeFalse();
            gs.GetIncidentPlayedThisTurn(2).Should().BeFalse();
        }

        [Fact(DisplayName = "プレイヤー 1 に設定すると、プレイヤー 1 のインシデント使用フラグが更新される")]
        public void SetIncidentPlayedThisTurn_Player1_Updates()
        {
            var gs = TestFactory.MakeGameState();
            gs.SetIncidentPlayedThisTurn(1, true);
            gs.Player1IncidentPlayedThisTurn.Should().BeTrue();
        }

        [Fact(DisplayName = "プレイヤー 2 に設定すると、プレイヤー 2 のインシデント使用フラグが更新される")]
        public void SetIncidentPlayedThisTurn_Player2_Updates()
        {
            var gs = TestFactory.MakeGameState();
            gs.SetIncidentPlayedThisTurn(2, true);
            gs.Player2IncidentPlayedThisTurn.Should().BeTrue();
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void GetIncidentPlayedThisTurn_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.GetIncidentPlayedThisTurn(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void SetIncidentPlayedThisTurn_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.SetIncidentPlayedThisTurn(3, true);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Trait("対象", "稼働実績フラグアクセサ")]
    public class HasOperatedAccessor
    {
        [Fact(DisplayName = "初期状態では両プレイヤーの稼働実績フラグが false になる")]
        public void GetHasOperated_DefaultsFalse()
        {
            var gs = TestFactory.MakeGameState();
            gs.GetHasOperated(1).Should().BeFalse();
            gs.GetHasOperated(2).Should().BeFalse();
        }

        [Fact(DisplayName = "プレイヤー 1 に設定すると、プレイヤー 1 の稼働実績フラグが更新される")]
        public void SetHasOperated_Player1_Updates()
        {
            var gs = TestFactory.MakeGameState();
            gs.SetHasOperated(1, true);
            gs.Player1HasOperated.Should().BeTrue();
        }

        [Fact(DisplayName = "プレイヤー 2 に設定すると、プレイヤー 2 の稼働実績フラグが更新される")]
        public void SetHasOperated_Player2_Updates()
        {
            var gs = TestFactory.MakeGameState();
            gs.SetHasOperated(2, true);
            gs.Player2HasOperated.Should().BeTrue();
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void GetHasOperated_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.GetHasOperated(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void SetHasOperated_InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.SetHasOperated(3, true);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Trait("対象", "相手プレイヤー算出")]
    public class OpponentOf
    {
        [Theory(DisplayName = "指定したプレイヤーの相手プレイヤーを返す")]
        [InlineData(1, 2)]
        [InlineData(2, 1)]
        public void ReturnsOtherPlayer(long playerNum, long expected)
        {
            var gs = TestFactory.MakeGameState();
            gs.OpponentOf(playerNum).Should().Be(expected);
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void InvalidPlayer_Throws()
        {
            var gs = TestFactory.MakeGameState();
            var act = () => gs.OpponentOf(3);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Trait("対象", "インスタンス ID 採番")]
    public class NextInstanceID
    {
        [Fact(DisplayName = "呼ぶたびに inst_0 から連番のインスタンス ID を採番しシーケンスを進める")]
        public void ReturnsSequentialIDs()
        {
            var gs = TestFactory.MakeGameState();
            gs.NextInstanceSeq = 0;

            gs.NextInstanceID().Should().Be("inst_0");
            gs.NextInstanceID().Should().Be("inst_1");
            gs.NextInstanceID().Should().Be("inst_2");
            gs.NextInstanceSeq.Should().Be(3);
        }
    }

    [Trait("対象", "デプロイ順採番")]
    public class NextDeployOrder
    {
        [Fact(DisplayName = "呼ぶたびに 1 から増加するデプロイ順を採番しシーケンスを進める")]
        public void ReturnsIncreasingValues()
        {
            var gs = TestFactory.MakeGameState();
            gs.NextDeployOrderSeq = 0;

            gs.NextDeployOrder().Should().Be(1);
            gs.NextDeployOrder().Should().Be(2);
            gs.NextDeployOrder().Should().Be(3);
            gs.NextDeployOrderSeq.Should().Be(3);
        }
    }

    [Trait("対象", "NPC モデル取得")]
    public class GetNpcModel
    {
        [Fact(DisplayName = "プレイヤー 1 を指定すると、プレイヤー 1 の NPC モデルを返す")]
        public void Player1_ReturnsNpc1Model()
        {
            var game = TestFactory.MakeGame();
            game.Npc1Model = "SHE";
            game.GetNpcModel(1).Should().Be("SHE");
        }

        [Fact(DisplayName = "プレイヤー 2 を指定すると、プレイヤー 2 の NPC モデルを返す")]
        public void Player2_ReturnsNpc2Model()
        {
            var game = TestFactory.MakeGame();
            game.Npc2Model = "NTT";
            game.GetNpcModel(2).Should().Be("NTT");
        }

        [Fact(DisplayName = "不正なプレイヤー番号のとき、ArgumentOutOfRangeException を投げる")]
        public void InvalidPlayer_Throws()
        {
            var game = TestFactory.MakeGame();
            var act = () => game.GetNpcModel(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
