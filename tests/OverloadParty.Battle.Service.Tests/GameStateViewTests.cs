using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Service;

namespace OverloadParty.Battle.Tests.Service;

public class GameStateViewTests
{
    /// <summary>Shared setup for GameStateView.Build tests (card cache and game).</summary>
    public abstract class Base
    {
        protected readonly TestCardCache _cc = new();
        protected readonly Game _game = TestFactory.MakeGame();

        protected Base()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", deployTurns: 0));
            _cc.Add(TestFactory.DataCard(cardId: "TST-0002"));
        }
    }

    [Trait("対象", "自分のビュー")]
    public class PlayerView : Base
    {
        [Fact(DisplayName = "自分のビューにフィールドと手札の全内容が載る")]
        public void ContainsFullFieldAndHand()
        {
            var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main, activePlayer: 1,
                p1Budget: 4000, p2Budget: 3000);
            state.Player1InsightPool = 200;

            // Place a resource on player 1's frontend
            var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "inst_1", faceUp: true);
            state.Player1Field.Frontend[0] = res;

            // Give player 1 a hand card
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" });

            // Give player 1 repository and trash
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" });
            state.Player1Trash.Add(new UndeployedCard { InstanceID = "t_1", CardID = "TST-0001" });

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            result.MyView.PlayerNum.Should().Be(1);
            result.MyView.Budget.Should().Be(4000);
            result.MyView.InsightPool.Should().Be(200);
            result.MyView.Field.Frontend[0]!.InstanceID.Should().Be(res.InstanceID);
            result.MyView.Field.Frontend[0]!.FaceUp.Should().BeTrue();
            result.MyView.Hand.Should().HaveCount(1);
            result.MyView.Hand[0].InstanceID.Should().Be("h_1");
            result.MyView.Hand[0].CardID.Should().Be("TST-0001");
            result.MyView.RepoCount.Should().Be(1);
            result.MyView.TrashCount.Should().Be(1);
            result.MyView.Trash.Should().HaveCount(1);
        }

        [Fact(DisplayName = "自分のビューのリソースに、ランク・ダメージ・デプロイ順・効果使用済みなどの盤面状態がそのまま載る")]
        public void ResourceCarriesBoardStateFields()
        {
            var state = TestFactory.MakeGameState();
            var res = new DeployedResource
            {
                InstanceID = "inst_1",
                CardID = "TST-0001",
                ArtNo = 2,
                Rank = Rank.Medium,
                InstanceFamily = InstanceFamily.M,
                FaceUp = true,
                Damage = 200,
                DeployedOnTurn = 3,
                DeployOrder = 2,
                EffectUsedThisTurn = true,
                ElasticBonus = 150,
                LastAttackTurn = 3,
            };
            state.Player1Field.Frontend[0] = res;

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            var mapped = result.MyView.Field.Frontend[0]!;
            mapped.Rank.Should().Be("medium");
            mapped.InstanceFamily.Should().Be("M");
            mapped.Damage.Should().Be(200);
            mapped.DeployedOnTurn.Should().Be(3);
            mapped.DeployOrder.Should().Be(2);
            mapped.EffectUsedThisTurn.Should().BeTrue();
            mapped.ElasticBonus.Should().Be(150);
            mapped.LastAttackTurn.Should().Be(3);
            mapped.ArtNo.Should().Be(2);
        }

        [Fact(DisplayName = "一時効果を持つリソースのビューに、一時効果が載る")]
        public void ResourceWithTemporaryEffect_CarriesTemporaryEffect()
        {
            var state = TestFactory.MakeGameState();
            var res = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "inst_te", faceUp: true);
            res.TemporaryEffects.Add(new TemporaryEffect
            {
                EffectType = EffectTypes.BuffTP,
                Value = 200,
                Duration = "this_turn",
                SourceID = "src_1",
                Mode = "",
            });
            state.Player1Field.Frontend[0] = res;

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            var mapped = result.MyView.Field.Frontend[0]!;
            mapped.TemporaryEffects.Should().ContainSingle();
            mapped.TemporaryEffects[0].EffectType.Should().Be(EffectTypes.BuffTP);
            mapped.TemporaryEffects[0].Value.Should().Be(200);
            mapped.TemporaryEffects[0].Duration.Should().Be("this_turn");
            mapped.TemporaryEffects[0].SourceID.Should().Be("src_1");
        }
    }

    [Trait("対象", "選択待ちの表示")]
    public class PendingSelectionView : Base
    {
        [Fact(DisplayName = "スロット選択待ちのプレイヤーのビューに、対象リソースと配置可能ゾーンが載る")]
        public void SlotSelectPending_ShowsResourceAndValidZones()
        {
            var state = TestFactory.MakeGameState();
            state.PendingSlotSelects =
            [
                new AwaitingSlotSelect
                {
                    PlayerNum = 1,
                    Resource = new DeployedResource { InstanceID = "inst_1", CardID = "TST-0001" },
                    ValidZones = ["frontend_0", "frontend_1"],
                },
            ];

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            result.MyView.PendingSlotSelect.Should().NotBeNull();
            result.MyView.PendingSlotSelect!.Resource.CardID.Should().Be("TST-0001");
            result.MyView.PendingSlotSelect.ValidZones.Should().Equal("frontend_0", "frontend_1");
        }

        [Fact(DisplayName = "スロット選択待ちでない相手のビューには、スロット選択待ちが載らない")]
        public void SlotSelectPending_OpponentViewHasNoPendingSelect()
        {
            var state = TestFactory.MakeGameState();
            state.PendingSlotSelects =
            [
                new AwaitingSlotSelect
                {
                    PlayerNum = 1,
                    Resource = new DeployedResource { InstanceID = "inst_1", CardID = "TST-0001" },
                    ValidZones = ["frontend_0"],
                },
            ];

            var result = GameStateView.Build(state, _game, 2, _cc, new EffectRegistry());

            result.MyView.PendingSlotSelect.Should().BeNull();
        }

        [Fact(DisplayName = "効果中選択の待ちがあるとき、ビューに選択者・効果カード・選択種別が載る")]
        public void EffectChoicePending_ShowsChooserCardAndKind()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "c_1", CardID = "TST-0001" });
            state.PendingEffectChoice = new PendingEffectChoice
            {
                ChooserPlayerNum = 1,
                OwnerPlayerNum = 1,
                EffectCardId = "TST-0002",
                EffectInstanceId = "inst_2",
                Trigger = TriggerType.Ignition,
                ChoiceKey = "deckTop",
                ChoiceKind = ChoiceKinds.DeckTop,
                Candidates = ["c_1"],
            };

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            result.PendingEffectChoice.Should().NotBeNull();
            result.PendingEffectChoice!.ChooserPlayerNum.Should().Be(1);
            result.PendingEffectChoice.EffectCardId.Should().Be("TST-0002");
            result.PendingEffectChoice.ChoiceKind.Should().Be(ChoiceKinds.DeckTop);
        }

        [Fact(DisplayName = "選択待ちが無いとき、ビューの選択待ちは null になる")]
        public void NoPending_BothViewsAreNull()
        {
            var state = TestFactory.MakeGameState();

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            result.MyView.PendingSlotSelect.Should().BeNull();
            result.PendingEffectChoice.Should().BeNull();
        }
    }

    [Trait("対象", "相手のビュー")]
    public class OpponentView : Base
    {
        [Fact(DisplayName = "相手のビューは手札の枚数だけを見せ中身は見せない")]
        public void ShowsHandCountNotCards()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Hand.Add(new UndeployedCard { InstanceID = "h_1", CardID = "TST-0001" });
            state.Player2Hand.Add(new UndeployedCard { InstanceID = "h_2", CardID = "TST-0002" });

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            result.OppView.HandCount.Should().Be(2);
            result.OppView.PlayerNum.Should().Be(2);
        }

        [Fact(DisplayName = "相手のビューは裏向きリソースのカード ID とステータスを隠す")]
        public void HidesFaceDownResourceDetails()
        {
            var state = TestFactory.MakeGameState();
            var faceDownRes = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "inst_opp", faceUp: false,
                deployLeft: 1, maxAV: 1400, currentAV: 1400, maxTP: 600, currentTP: 600);
            state.Player2Field.Frontend[0] = faceDownRes;

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

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

        [Fact(DisplayName = "相手のビューは表向きリソースを完全に見せる")]
        public void ShowsFaceUpResourceFully()
        {
            var state = TestFactory.MakeGameState();
            var faceUpRes = TestFactory.MakeResource(
                cardId: "TST-0001", instanceId: "inst_opp_up", faceUp: true,
                maxAV: 1400, currentAV: 1400, maxTP: 600, currentTP: 600);
            state.Player2Field.Frontend[1] = faceUpRes;

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            var oppSlot = result.OppView.Field.Frontend[1];
            oppSlot.Should().NotBeNull();
            oppSlot!.FaceUp.Should().BeTrue();
            oppSlot.CardID.Should().Be("TST-0001");
            oppSlot.MaxAV.Should().Be(1400);
        }

        [Fact(DisplayName = "相手のビューは裏向きサポートのカード ID とアート番号を隠す")]
        public void HidesCardIDForFaceDownSupport()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_1",
                CardID = "TEST-0200",
                ArtNo = 3,
                FaceUp = false,
            };

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            var oppSup = result.OppView.Field.Support[0];
            oppSup.Should().NotBeNull();
            oppSup!.InstanceID.Should().Be("sup_1");
            oppSup.FaceDown.Should().BeTrue();
            oppSup.CardID.Should().BeNull("face-down support should hide CardID");
            oppSup.ArtNo.Should().Be(0, "face-down support should hide ArtNo");
        }

        [Fact(DisplayName = "相手のビューはのぞき見済みサポートのカード ID とアート番号をそのプレイヤーに見せる")]
        public void RevealsPeekedSupportCardID()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[0] = new DeployedSupport
            {
                InstanceID = "sup_peek",
                CardID = "TEST-0200",
                ArtNo = 2,
                FaceUp = false,
                PeekedBy = [1],
            };

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            var oppSup = result.OppView.Field.Support[0];
            oppSup.Should().NotBeNull();
            oppSup!.FaceDown.Should().BeTrue("card should stay face-down");
            oppSup.Peeked.Should().BeTrue("player 1 has peeked at this card");
            oppSup.CardID.Should().Be("TEST-0200", "peeked card reveals CardID to the peeking player");
            oppSup.ArtNo.Should().Be(2, "peeked card reveals ArtNo to the peeking player");
        }

        [Fact(DisplayName = "相手のビューは表向きサポートのカード ID とアート番号を見せる")]
        public void ShowsFaceUpSupportCardID()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Field.Support[1] = new DeployedSupport
            {
                InstanceID = "sup_2",
                CardID = "TEST-0200",
                ArtNo = 5,
                FaceUp = true,
            };

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            var oppSup = result.OppView.Field.Support[1];
            oppSup.Should().NotBeNull();
            oppSup!.FaceDown.Should().BeFalse();
            oppSup.CardID.Should().Be("TEST-0200");
            oppSup.ArtNo.Should().Be(5);
        }
    }

    [Trait("対象", "自分のターン判定")]
    public class IsMyTurn : Base
    {
        [Theory(DisplayName = "ビューの自分のターン判定がアクティブプレイヤーと閲覧プレイヤーの一致を反映する")]
        [InlineData(1, 1, true)]
        [InlineData(1, 2, false)]
        [InlineData(2, 2, true)]
        [InlineData(2, 1, false)]
        public void ReflectsActivePlayer(long activePlayer, long viewingPlayer, bool expected)
        {
            var state = TestFactory.MakeGameState(activePlayer: activePlayer);

            var result = GameStateView.Build(state, _game, viewingPlayer, _cc, new EffectRegistry());

            result.IsMyTurn.Should().Be(expected);
            result.ActivePlayer.Should().Be(activePlayer);
        }
    }

    [Trait("対象", "deck_top 選択の公開")]
    public class DeckTopChoiceReveal : Base
    {
        /// <summary>選択するプレイヤー (Player1) のデッキ上端 2 枚を候補にした deck_top 選択待ちを作る。</summary>
        /// <returns>選択待ち情報。</returns>
        private static PendingEffectChoice MakeDeckPending() => new()
        {
            ChooserPlayerNum = 1,
            OwnerPlayerNum = 1,
            EffectCardId = "TST-0500",
            EffectInstanceId = "trash_x",
            Trigger = TriggerType.Ignition,
            ChoiceKey = "deckTop",
            ChoiceKind = ChoiceKinds.DeckTop,
            Candidates = ["r_1", "r_2"],
        };

        [Fact(DisplayName = "deck_top 選択待ちで選択するプレイヤーのビューに公開デッキ上端付きの resolve アクションが現れる")]
        public void Chooser_GetsResolveActionWithRevealedDeckTop()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" });
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_2", CardID = "TST-0002" });
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_3", CardID = "TST-0001" });
            state.PendingEffectChoice = MakeDeckPending();

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            var resolve = result.MyView.AvailableActions!.OfType<ResolvePendingChoiceAction>().Single();
            resolve.ChoiceKind.Should().Be(ChoiceKinds.DeckTop);
            resolve.ChoiceOptions.Select(o => o.Key).Should().Equal("r_1", "r_2");
            resolve.RevealedDeckTop!.Select(c => c.InstanceID).Should().Equal("r_1", "r_2");
        }

        [Fact(DisplayName = "deck_top 選択待ちで選択しない相手のビューには resolve アクションが現れない")]
        public void Opponent_GetsNoResolveAction()
        {
            var state = TestFactory.MakeGameState();
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" });
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "r_2", CardID = "TST-0002" });
            state.PendingEffectChoice = MakeDeckPending();

            var result = GameStateView.Build(state, _game, 2, _cc, new EffectRegistry());

            result.MyView.AvailableActions.Should().BeNull();
        }
    }

    [Trait("対象", "バジェットとインサイトプールの表示")]
    public class BudgetAndInsightPool : Base
    {
        [Fact(DisplayName = "両プレイヤーのバジェットとインサイトプールが表示される")]
        public void ReportedForBothPlayers()
        {
            var state = TestFactory.MakeGameState(p1Budget: 3500, p2Budget: 4200);
            state.Player1InsightPool = 150;
            state.Player2InsightPool = 300;

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            result.MyView.Budget.Should().Be(3500);
            result.MyView.InsightPool.Should().Be(150);
            result.OppView.Budget.Should().Be(4200);
            result.OppView.InsightPool.Should().Be(300);
        }

        [Fact(DisplayName = "プレイヤー 2 視点では自分ビューと相手ビューが入れ替わる")]
        public void AsPlayer2_SwapsMyViewAndOppView()
        {
            var state = TestFactory.MakeGameState(p1Budget: 1000, p2Budget: 2000);
            state.Player1Hand.Add(new UndeployedCard { InstanceID = "p1_h", CardID = "TST-0001" });
            state.Player2Hand.Add(new UndeployedCard { InstanceID = "p2_h", CardID = "TST-0002" });

            var result = GameStateView.Build(state, _game, 2, _cc, new EffectRegistry());

            result.MyView.PlayerNum.Should().Be(2);
            result.MyView.Budget.Should().Be(2000);
            result.MyView.Hand.Should().ContainSingle(h => h.InstanceID == "p2_h");
            result.OppView.PlayerNum.Should().Be(1);
            result.OppView.Budget.Should().Be(1000);
            result.OppView.HandCount.Should().Be(1);
        }
    }

    [Trait("対象", "ゲームメタデータの表示")]
    public class GameMetadata : Base
    {
        [Fact(DisplayName = "ビューにゲーム ID・現在ターン・現在フェーズ・アクティブプレイヤーが設定される")]
        public void SetsGameMetadata()
        {
            var state = TestFactory.MakeGameState(turn: 5, phase: Phase.Battle, activePlayer: 2);

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            result.GameID.Should().Be("test-game");
            result.CurrentTurn.Should().Be(5);
            result.CurrentPhase.Should().Be("battle");
            result.ActivePlayer.Should().Be(2);
        }
    }

    [Trait("対象", "相手のデッキ・トラッシュ枚数")]
    public class OpponentCounts : Base
    {
        [Fact(DisplayName = "相手のデッキとトラッシュの枚数が表示される")]
        public void ReportsRepoAndTrashCounts()
        {
            var state = TestFactory.MakeGameState();
            state.Player2Repository.Add(new UndeployedCard { InstanceID = "r_1", CardID = "TST-0001" });
            state.Player2Repository.Add(new UndeployedCard { InstanceID = "r_2", CardID = "TST-0001" });
            state.Player2Trash.Add(new UndeployedCard { InstanceID = "t_1", CardID = "TST-0001" });

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            result.OppView.RepoCount.Should().Be(2);
            result.OppView.TrashCount.Should().Be(1);
        }
    }

    [Trait("対象", "利用可能アクションの公開")]
    public class AvailableActions : Base
    {
        [Theory(DisplayName = "利用可能アクションはビューの閲覧プレイヤーがアクティブなときだけ載る")]
        [InlineData(1, true)]
        [InlineData(2, false)]
        public void DependsOnActivePlayer(long activePlayer, bool expectActions)
        {
            var state = TestFactory.MakeGameState(phase: Phase.Main, activePlayer: activePlayer);

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            if (expectActions)
            {
                result.MyView.AvailableActions.Should().NotBeNull();
            }
            else
            {
                result.MyView.AvailableActions.Should().BeNull();
            }
        }

        [Fact(DisplayName = "終了済みゲームでは利用可能アクションが載らない")]
        public void FinishedGame_DoesNotGetAvailableActions()
        {
            var game = TestFactory.MakeGame();
            game.Status = GameStatus.Finished;
            var state = TestFactory.MakeGameState(phase: Phase.Main, activePlayer: 1);

            var result = GameStateView.Build(state, game, 1, _cc, new EffectRegistry());

            result.MyView.AvailableActions.Should().BeNull();
        }
    }

    [Trait("対象", "タイムバンクの表示")]
    public class TimeBank : Base
    {
        [Fact(DisplayName = "両プレイヤーのタイムバンクが表示される")]
        public void ReportedForBothPlayers()
        {
            var state = TestFactory.MakeGameState();
            state.Player1TimeBank = 300;
            state.Player2TimeBank = 420;

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            result.MyView.TimeBank.Should().Be(300);
            result.OppView.TimeBank.Should().Be(420);
        }
    }

    [Trait("対象", "空スロットの表示")]
    public class EmptySlots : Base
    {
        [Fact(DisplayName = "相手のビューの空フィールドスロットは全て null になる")]
        public void OpponentView_AreNull()
        {
            var state = TestFactory.MakeGameState();
            // Player 2 field is completely empty

            var result = GameStateView.Build(state, _game, 1, _cc, new EffectRegistry());

            result.OppView.Field.Frontend.Should().AllSatisfy(slot => slot.Should().BeNull());
            result.OppView.Field.Backend.Should().AllSatisfy(slot => slot.Should().BeNull());
            result.OppView.Field.Support.Should().AllSatisfy(slot => slot.Should().BeNull());
        }
    }
}
