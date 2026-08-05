using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects.Ops;
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
            await EndTurn(_repo, _engine, gameID, 1);
            await EndTurn(_repo, _engine, gameID, 2);
            deployed.DeployingTurnsLeft.Should().Be(1, "所有者の 1 回目のドローフェーズではデプロイ完了に届かず稼働しない");
            deployed.FaceUp.Should().BeFalse();
            _onDeployFired.Should().BeFalse();

            // もう 1 ラウンド進める。手番が P1 に戻る際のドローフェーズでデプロイが完了し稼働する。
            await EndTurn(_repo, _engine, gameID, 1);
            await EndTurn(_repo, _engine, gameID, 2);
            deployed.DeployingTurnsLeft.Should().Be(0);
            deployed.FaceUp.Should().BeTrue("デプロイターン 2 のリソースは所有者の 2 回目のドローフェーズで稼働する");
            _onDeployFired.Should().BeTrue("デプロイ完了時にデプロイ時効果が発動する");
        }
    }

    /// <summary>指定プレイヤーのアクションをエンジン経由で実行する。</summary>
    /// <param name="repo">ゲームとゲーム状態を保持するリポジトリ。</param>
    /// <param name="engine">アクションを実行するエンジン。</param>
    /// <param name="gameID">対象ゲームの ID。</param>
    /// <param name="playerNum">アクションするプレイヤー番号。</param>
    /// <param name="actionType">アクション種別。</param>
    /// <param name="actionData">アクション固有のリクエスト。</param>
    /// <returns>アクション結果。</returns>
    private static async Task<ActionResult> Act(
        FakeGameRepository repo, GameEngine engine, string gameID,
        long playerNum, ActionType actionType, object actionData)
    {
        var game = await repo.GetGame(gameID);
        return await engine.ProcessAction(game!, playerNum, actionType, actionData);
    }

    /// <summary>指定プレイヤーのターンを実ルールどおり終了させ、手札が上限を超える場合は超過分を破棄する。</summary>
    /// <param name="repo">ゲームとゲーム状態を保持するリポジトリ。</param>
    /// <param name="engine">アクションを実行するエンジン。</param>
    /// <param name="gameID">対象ゲームの ID。</param>
    /// <param name="player">ターンを終了するプレイヤー番号。</param>
    private static async Task EndTurn(
        FakeGameRepository repo, GameEngine engine, string gameID, long player)
    {
        while (true)
        {
            var game = await repo.GetGame(gameID);
            var result = await engine.ProcessAction(game!, player, ActionType.EndPhase, new object());

            if (result.ShouldDiscard)
            {
                var state = await repo.GetGameState(gameID);
                var hand = state!.GetHand(player);
                var discardIds = hand.Take(hand.Count - BattleConstants.HandLimit)
                    .Select(c => c.InstanceID).ToArray();
                game = await repo.GetGame(gameID);
                await engine.ProcessAction(game!, player, ActionType.DiscardHand,
                    new DiscardHandRequest { CardInstanceIDs = [.. discardIds] });
                return;
            }

            var current = await repo.GetGameState(gameID);
            if (current!.ActivePlayer != player)
            {
                return;
            }
        }
    }

    [Trait("対象", "配置時のデプロイターン短縮")]
    public class OnSetDeployShortening
    {
        private const string ShorteningCardId = "TST-0718";
        private const string FillerCardId = "TST-0001";

        private readonly FakeGameRepository _repo = new();
        private readonly TestCardCache _cc = new();
        private readonly GameEngine _engine;
        private bool _onDeployFired;

        /// <summary>デプロイターン 2 で、配置時に残デプロイターンを 1 減らすリソースを登録したエンジンを用意する。</summary>
        public OnSetDeployShortening()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: ShorteningCardId, deployTurns: 2));
            _cc.Add(TestFactory.ComputeCard(cardId: FillerCardId, deployTurns: 0));
            var effects = new TestEffectRegistry();
            effects.Register(
                ShorteningCardId, TriggerType.OnSet,
                EffectComposer.Compose(new ReduceDeployTurnsOp(new StaticAmount(1))));
            effects.Register(ShorteningCardId, TriggerType.OnDeploy, _ => { _onDeployFired = true; return new EffectResult(); });
            _engine = new GameEngine(_repo, _cc, effects, new InitiativeCatalog([]), new FakeClock());
        }

        [Fact(DisplayName = "デプロイターン 2 のカードが配置時に 1 短縮されたとき、次の自分のドローフェーズで稼働する")]
        public async Task ShortenedAtPlacement_BecomesOperationalAtNextOwnDrawPhase()
        {
            var gameID = await _engine.CreateNewGame(
                TestFactory.MakeDeck(_cc, ShorteningCardId), TestFactory.MakeDeck(_cc, FillerCardId), firstPlayer: 1);

            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);
            var state = (await _repo.GetGameState(gameID))!;
            var cardInstanceId = TestFactory.ReplaceFirstHandCard(state, 1, ShorteningCardId);
            game = await _repo.GetGame(gameID);
            await _engine.ProcessAction(game!, 1, ActionType.PlayCard,
                new PlayCardRequest { CardInstanceID = cardInstanceId, Zone = Zones.Frontend, Index = 0 });

            // FakeGameRepository は状態をその場で更新するため、この参照は以降の進行でも生き続ける。
            var deployed = state.Player1Field.Frontend[0]!;
            deployed.DeployingTurnsLeft.Should().Be(1, "配置時効果がデプロイターン 2 を 1 短縮する");
            deployed.FaceUp.Should().BeFalse();
            _onDeployFired.Should().BeFalse();

            await EndTurn(_repo, _engine, gameID, 1);
            await EndTurn(_repo, _engine, gameID, 2);

            deployed.DeployingTurnsLeft.Should().Be(0);
            deployed.FaceUp.Should().BeTrue("短縮により所有者の 1 回目のドローフェーズで稼働する");
            _onDeployFired.Should().BeTrue("稼働時効果はデプロイ完了時に発動する");
        }
    }

    /// <summary>任意の Action を IEffectOp として実行するテスト専用 op。</summary>
    private sealed class InlineOp(Action<OpContext> action) : IEffectOp
    {
        public void Execute(OpContext ctx) => action(ctx);
    }

    [Trait("対象", "配置時のデプロイターン短縮による即時稼働")]
    public class OnSetImmediateDeploy
    {
        private const string ImmediateCardId = "TST-0719";
        private const string FillerCardId = "TST-0001";

        private readonly FakeGameRepository _repo = new();
        private readonly TestCardCache _cc = new();
        private readonly GameEngine _engine;
        private int _onDeployFireCount;

        /// <summary>デプロイターン 2 で、配置時に残デプロイターンを 2 減らすリソースを登録したエンジンを用意する。</summary>
        public OnSetImmediateDeploy()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: ImmediateCardId, mc: 0, deployTurns: 2));
            _cc.Add(TestFactory.ComputeCard(cardId: FillerCardId, mc: 0, deployTurns: 0));
            var effects = new TestEffectRegistry();
            effects.Register(
                ImmediateCardId, TriggerType.OnSet,
                EffectComposer.Compose(new ReduceDeployTurnsOp(new StaticAmount(2))));
            effects.Register(
                ImmediateCardId, TriggerType.OnDeploy,
                _ => { _onDeployFireCount++; return new EffectResult(); });
            _engine = new GameEngine(_repo, _cc, effects, new InitiativeCatalog([]), new FakeClock());
        }

        [Fact(DisplayName = "デプロイターン 2 のカードが配置時に 2 短縮されたとき、その場で表向きになり稼働時効果が 1 回だけ発動する")]
        public async Task ShortenedToZeroAtPlacement_BecomesOperationalImmediately()
        {
            var gameID = await _engine.CreateNewGame(
                TestFactory.MakeDeck(_cc, ImmediateCardId), TestFactory.MakeDeck(_cc, FillerCardId), firstPlayer: 1);

            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);
            var state = (await _repo.GetGameState(gameID))!;
            var cardInstanceId = TestFactory.ReplaceFirstHandCard(state, 1, ImmediateCardId);
            game = await _repo.GetGame(gameID);
            await _engine.ProcessAction(game!, 1, ActionType.PlayCard,
                new PlayCardRequest { CardInstanceID = cardInstanceId, Zone = Zones.Frontend, Index = 0 });

            var deployed = state.Player1Field.Frontend[0]!;
            deployed.DeployingTurnsLeft.Should().Be(0);
            deployed.FaceUp.Should().BeTrue("配置時に残デプロイターンが 0 になったカードはその場で稼働する");
            _onDeployFireCount.Should().Be(1);
            state.Player1HasOperated.Should().BeTrue();
        }
    }

    [Trait("対象", "稼働時の選択待ちを挟むターン進行")]
    public class DeployChoiceOnCountdownCompletion
    {
        private const string ChoiceCardId = "TST-0720";
        private const string FillerCardId = "TST-0001";
        private const string AutopilotOption = "autopilot";
        private const string StandardOption = "standard";
        private const long AutopilotInsight = 111;
        private const long StandardInsight = 222;

        private readonly FakeGameRepository _repo = new();
        private readonly TestCardCache _cc = new();
        private readonly TestEffectRegistry _effects = new();
        private readonly GameEngine _engine;

        /// <summary>稼働時に 2 択の分岐を要求するデプロイターン 2 のリソースを登録したエンジンを用意する。</summary>
        public DeployChoiceOnCountdownCompletion()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: ChoiceCardId, mc: 0, deployTurns: 2));
            _cc.Add(TestFactory.ComputeCard(cardId: FillerCardId, mc: 0, deployTurns: 0));
            var branches = new Dictionary<string, List<IEffectOp>>
            {
                [AutopilotOption] = [new InlineOp(o => o.State.SetInsightPool(o.PlayerNum, AutopilotInsight))],
                [StandardOption] = [new InlineOp(o => o.State.SetInsightPool(o.PlayerNum, StandardInsight))],
            };
            _effects.Register(
                ChoiceCardId, TriggerType.OnDeploy,
                EffectComposer.Compose(new BranchOnChoiceOp(branches)));
            _engine = new GameEngine(_repo, _cc, _effects, new InitiativeCatalog([]), new FakeClock());
        }

        /// <summary>
        /// プレイヤー 2 のバトルフェーズから始まり、プレイヤー 1 の場に残り 1 ターンの
        /// 選択付きカードが伏せてある盤面を用意する。
        /// </summary>
        /// <returns>用意したゲームの ID と、進行に伴って更新され続けるゲーム状態。</returns>
        private async Task<(string GameID, BattleGameState State)> StartBeforeOpponentsDrawPhase()
        {
            var gameID = await _engine.CreateNewGame(
                TestFactory.MakeDeck(_cc, ChoiceCardId), TestFactory.MakeDeck(_cc, FillerCardId), firstPlayer: 1);

            var state = (await _repo.GetGameState(gameID))!;
            state.CurrentTurn = 4;
            state.ActivePlayer = 2;
            state.CurrentPhase = Phase.Battle;
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: ChoiceCardId, instanceId: "r_choice", faceUp: false, deployLeft: 1);

            return (gameID, state);
        }

        [Fact(DisplayName = "残り 1 ターンの選択付きカードがあるとき、前のターンプレイヤーがエンドフェーズを終了すると、アクションが完了し相手に選択待ちが積まれる")]
        public async Task EndPhase_CompletesAndQueuesChoiceForTheNextActivePlayer()
        {
            var (gameID, state) = await StartBeforeOpponentsDrawPhase();

            var result = await Act(_repo, _engine, gameID, 2, ActionType.EndPhase, new object());

            result.GameOver.Should().BeNull();
            state.ActivePlayer.Should().Be(1, "エンドフェーズの完了でターンが交代する");
            state.PendingEffectChoice.Should().NotBeNull();
            state.PendingEffectChoice!.ChooserPlayerNum.Should().Be(1);
            state.PendingEffectChoice.Candidates.Should().BeEquivalentTo(AutopilotOption, StandardOption);
            state.CurrentPhase.Should().Be(Phase.Draw, "選択が解決されるまでドローフェーズのまま止まる");
        }

        [Fact(DisplayName = "ドローフェーズで積まれた選択待ちは、選択するプレイヤーに選択を解決するアクションとして提示される")]
        public async Task QueuedChoice_IsOfferedToTheChooserAsAnAvailableAction()
        {
            var (gameID, state) = await StartBeforeOpponentsDrawPhase();
            await Act(_repo, _engine, gameID, 2, ActionType.EndPhase, new object());

            var actions = AvailableActions.GetAllAvailableActions(
                state, 1, state.Player1Field, state.Player2Field, state.Player1Hand,
                state.Player1Budget, state.Player1InsightPool, _cc, _effects);

            var choiceAction = actions.Should()
                .ContainSingle(a => a.Type == ActionTypes.ResolvePendingChoice).Subject;
            choiceAction.ChoiceOptions!.Select(o => o.Key)
                .Should().BeEquivalentTo(AutopilotOption, StandardOption);
        }

        [Fact(DisplayName = "積まれた選択を解決すると、選んだ分岐の効果が適用されメインフェーズへ進む")]
        public async Task ResolvingChoice_AppliesChosenBranchAndContinuesTheTurn()
        {
            var (gameID, state) = await StartBeforeOpponentsDrawPhase();
            await Act(_repo, _engine, gameID, 2, ActionType.EndPhase, new object());

            var result = await Act(_repo, _engine, gameID, 1, ActionType.ResolvePendingChoice,
                new ResolvePendingChoiceRequest { ChosenId = StandardOption });

            result.GameOver.Should().BeNull();
            state.PendingEffectChoice.Should().BeNull();
            state.Player1InsightPool.Should().Be(StandardInsight);
            state.Player1Field.Frontend[0]!.FaceUp.Should().BeTrue();
            state.Player1HasOperated.Should().BeTrue();
            state.CurrentPhase.Should().Be(Phase.Main);
            state.ActivePlayer.Should().Be(1);
        }

        [Fact(DisplayName = "前のターンプレイヤーの手札が上限を超えているとき、破棄を解決するとアクションが完了し相手に選択待ちが積まれる")]
        public async Task DiscardHand_CompletesAndQueuesChoiceForTheNextActivePlayer()
        {
            var (gameID, state) = await StartBeforeOpponentsDrawPhase();
            foreach (var i in Enumerable.Range(0, 2))
            {
                state.Player2Hand.Add(new UndeployedCard { InstanceID = $"extra_{i}", CardID = FillerCardId });
            }

            var endPhaseResult = await Act(_repo, _engine, gameID, 2, ActionType.EndPhase, new object());
            endPhaseResult.ShouldDiscard.Should().BeTrue();
            state.PendingEffectChoice.Should().BeNull("破棄が済むまでドローフェーズに入らない");

            var discardResult = await Act(_repo, _engine, gameID, 2, ActionType.DiscardHand,
                new DiscardHandRequest { CardInstanceIDs = ["extra_0"] });

            discardResult.GameOver.Should().BeNull();
            state.ActivePlayer.Should().Be(1);
            state.PendingEffectChoice.Should().NotBeNull();
            state.PendingEffectChoice!.ChooserPlayerNum.Should().Be(1);
            state.CurrentPhase.Should().Be(Phase.Draw);
        }
    }

    [Trait("対象", "効果でデプロイしたカードの配置時の選択待ち")]
    public class EffectDeployWithOnSetChoice
    {
        private const string DeployerCardId = "TST-0721";
        private const string ChoiceCardId = "TST-0722";
        private const string AutopilotOption = "autopilot";
        private const string StandardOption = "standard";

        private readonly FakeGameRepository _repo = new();
        private readonly TestCardCache _cc = new();
        private readonly GameEngine _engine;

        /// <summary>
        /// デッキから 2 枚のデプロイを要求する起動効果と、配置時に 2 択を要求する
        /// デプロイターン 2 のリソースを登録したエンジンを用意する。
        /// </summary>
        public EffectDeployWithOnSetChoice()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: DeployerCardId, mc: 0, deployTurns: 0));
            _cc.Add(TestFactory.ComputeCard(cardId: ChoiceCardId, mc: 0, deployTurns: 2));
            var effects = new TestEffectRegistry();

            var deployRequest = new RequestSlotFromRepoOp { Filter = card => card.CardId == ChoiceCardId };
            effects.Register(
                DeployerCardId, TriggerType.Ignition,
                EffectComposer.Compose(deployRequest, deployRequest));

            var branches = new Dictionary<string, List<IEffectOp>>
            {
                [AutopilotOption] = [new ReduceDeployTurnsOp(new StaticAmount(1))],
                [StandardOption] = [],
            };
            effects.Register(
                ChoiceCardId, TriggerType.OnSet,
                EffectComposer.Compose(new BranchOnChoiceOp(branches)));

            _engine = new GameEngine(_repo, _cc, effects, new InitiativeCatalog([]), new FakeClock());
        }

        [Fact(DisplayName = "効果で 2 枚のデプロイを要求し 1 枚目のスロットを選ぶと、配置時の選択待ちが積まれ、残りのスロット選択も残る")]
        public async Task SelectingFirstSlot_QueuesOnSetChoiceWhileSlotSelectionRemains()
        {
            var (gameID, state) = await StartWithTwoQueuedDeploys();

            await Act(_repo, _engine, gameID, 1, ActionType.SelectSlot,
                new SelectSlotRequest { Zone = Zones.Frontend, Index = 1 });

            var placed = state.Player1Field.Frontend[1]!;
            placed.CardID.Should().Be(ChoiceCardId);
            placed.DeployingTurnsLeft.Should().Be(2, "選択が解決されるまで配置時効果は適用されない");
            placed.FaceUp.Should().BeFalse();
            state.PendingEffectChoice.Should().NotBeNull();
            state.PendingEffectChoice!.Trigger.Should().Be(TriggerType.OnSet);
            state.PendingEffectChoice.Candidates.Should().BeEquivalentTo(AutopilotOption, StandardOption);
            state.PendingSlotSelects.Should().ContainSingle("2 枚目のスロット選択は残ったままになる");
        }

        [Fact(DisplayName = "積まれた配置時の選択を短縮する側で解決すると、残デプロイターンが縮み 2 枚目のスロット選択へ進める")]
        public async Task ResolvingOnSetChoice_ShortensDeployAndAllowsTheNextSlotSelection()
        {
            var (gameID, state) = await StartWithTwoQueuedDeploys();
            await Act(_repo, _engine, gameID, 1, ActionType.SelectSlot,
                new SelectSlotRequest { Zone = Zones.Frontend, Index = 1 });

            await Act(_repo, _engine, gameID, 1, ActionType.ResolvePendingChoice,
                new ResolvePendingChoiceRequest { ChosenId = AutopilotOption });

            state.PendingEffectChoice.Should().BeNull();
            state.Player1Field.Frontend[1]!.DeployingTurnsLeft.Should().Be(1);

            await Act(_repo, _engine, gameID, 1, ActionType.SelectSlot,
                new SelectSlotRequest { Zone = Zones.Frontend, Index = 2 });

            state.Player1Field.Frontend[2]!.CardID.Should().Be(ChoiceCardId);
            state.PendingSlotSelects.Should().BeEmpty();
        }

        /// <summary>
        /// 起動効果でデッキから 2 枚のデプロイを要求し、スロット選択が 2 件積まれた盤面を用意する。
        /// </summary>
        /// <returns>用意したゲームの ID と、進行に伴って更新され続けるゲーム状態。</returns>
        private async Task<(string GameID, BattleGameState State)> StartWithTwoQueuedDeploys()
        {
            var gameID = await _engine.CreateNewGame(
                TestFactory.MakeDeck(_cc, DeployerCardId, ChoiceCardId),
                TestFactory.MakeDeck(_cc, DeployerCardId), firstPlayer: 1);

            var state = (await _repo.GetGameState(gameID))!;
            state.CurrentTurn = 3;
            state.ActivePlayer = 1;
            state.CurrentPhase = Phase.Main;
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: DeployerCardId, instanceId: "r_deployer", faceUp: true);
            state.Player1Repository =
            [
                new UndeployedCard { InstanceID = "repo_c1", CardID = ChoiceCardId },
                new UndeployedCard { InstanceID = "repo_c2", CardID = ChoiceCardId },
            ];

            await Act(_repo, _engine, gameID, 1, ActionType.UseIgnition, new UseIgnitionRequest { InstanceID = "r_deployer" });
            state.PendingSlotSelects.Should().HaveCount(2);

            return (gameID, state);
        }
    }

    [Trait("対象", "収益化のターンをまたぐ再使用")]
    public class MonetizeAcrossTurns
    {
        private const string ComputeCardId = "TST-0001";

        private readonly FakeGameRepository _repo = new();
        private readonly TestCardCache _cc = new();
        private readonly GameEngine _engine;

        /// <summary>維持コストのかからない即時稼働のコンピュートを登録したエンジンを用意する。</summary>
        public MonetizeAcrossTurns()
        {
            _cc.Add(TestFactory.ComputeCard(cardId: ComputeCardId, mc: 0, deployTurns: 0));
            _engine = new GameEngine(_repo, _cc, new EffectRegistry(), new InitiativeCatalog([]), new FakeClock());
        }

        [Fact(DisplayName = "前のターンに収益化へ使用したリソースは、次の自分のターンに再度収益化できる")]
        public async Task MonetizedResource_CanMonetizeAgainOnNextOwnTurn()
        {
            var gameID = await _engine.CreateNewGame(
                TestFactory.MakeDeck(_cc, ComputeCardId), TestFactory.MakeDeck(_cc, ComputeCardId), firstPlayer: 1);

            var game = await _repo.GetGame(gameID);
            await _engine.RunAutoAdvance(game!);
            var state = (await _repo.GetGameState(gameID))!;
            var cardInstanceId = TestFactory.ReplaceFirstHandCard(state, 1, ComputeCardId);
            game = await _repo.GetGame(gameID);
            await _engine.ProcessAction(game!, 1, ActionType.PlayCard,
                new PlayCardRequest { CardInstanceID = cardInstanceId, Zone = Zones.Backend, Index = 0 });

            // FakeGameRepository は状態をその場で更新するため、この参照は以降の進行でも生き続ける。
            var resource = state.Player1Field.Backend[0]!;

            // ターン 1 は収益化できないため、1 ラウンド進めて P1 の次のターンで収益化する。
            await EndTurn(_repo, _engine, gameID, 1);
            await EndTurn(_repo, _engine, gameID, 2);
            state.SetInsightPool(1, 2000);

            long budgetBeforeFirstMonetize = state.GetBudget(1);
            await Monetize(gameID, resource.InstanceID, 400);

            state.GetBudget(1).Should().Be(budgetBeforeFirstMonetize + 400);
            resource.MonetizedThisTurn.Should().BeTrue();

            await EndTurn(_repo, _engine, gameID, 1);
            await EndTurn(_repo, _engine, gameID, 2);

            resource.MonetizedThisTurn.Should().BeFalse("自分のターンの終了時に収益化の使用済みが解除される");

            long budgetBeforeSecondMonetize = state.GetBudget(1);
            await Monetize(gameID, resource.InstanceID, 400);

            state.GetBudget(1).Should().Be(budgetBeforeSecondMonetize + 400);
        }

        /// <summary>プレイヤー 1 が指定リソースへインサイトを割り当てて収益化する。</summary>
        /// <param name="gameID">対象ゲームの ID。</param>
        /// <param name="instanceID">収益化に使うリソースのインスタンス ID。</param>
        /// <param name="amount">割り当てるインサイト量。</param>
        private async Task Monetize(string gameID, string instanceID, long amount)
        {
            var game = await _repo.GetGame(gameID);
            await _engine.ProcessAction(game!, 1, ActionType.Monetize, new MonetizeRequest
            {
                Distributions = [new MonetizeDistribution { InstanceID = instanceID, Amount = amount }],
            });
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

        [Fact(DisplayName = "ターン 30 のメインフェーズでカードをプレイしたとき、アクション解決後もゲームが続行する")]
        public async Task PlayCardOnFinalTurn_ContinuesGame()
        {
            var (gameID, state) = await StartBattlePhaseAt(
                turn: 30, activePlayer: 2, p1Budget: 3000, p2Budget: 2000);
            state.CurrentPhase = Phase.Main;
            var cardInstanceId = TestFactory.ReplaceFirstHandCard(state, 2, FreeCardId);

            var result = await Act(_repo, _engine, gameID, 2, ActionType.PlayCard,
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

            var result = await Act(_repo, _engine, gameID, 2, ActionType.EndPhase, new object());

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

            var result = await Act(_repo, _engine, gameID, 2, ActionType.EndPhase, new object());

            result.GameOver.Should().NotBeNull();
            result.GameOver!.WinnerNum.Should().Be(0);
            result.GameOver.Reason.Should().Be(WinReasons.Draw);
        }

        [Fact(DisplayName = "ターン 29 のエンドフェーズが完了したとき、ターン 30 に交代しゲームが続行する")]
        public async Task EndPhaseBeforeFinalTurn_AdvancesToFinalTurn()
        {
            var (gameID, state) = await StartBattlePhaseAt(
                turn: 29, activePlayer: 1, p1Budget: 3000, p2Budget: 2000);

            var result = await Act(_repo, _engine, gameID, 1, ActionType.EndPhase, new object());

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

            var result = await Act(_repo, _engine, gameID, 2, ActionType.EndPhase, new object());

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

            var endPhaseResult = await Act(_repo, _engine, gameID, 2, ActionType.EndPhase, new object());

            endPhaseResult.ShouldDiscard.Should().BeTrue();
            endPhaseResult.GameOver.Should().BeNull();

            var discardResult = await Act(_repo, _engine, gameID, 2, ActionType.DiscardHand,
                new DiscardHandRequest { CardInstanceIDs = ["extra_0", "extra_1"] });

            discardResult.GameOver.Should().NotBeNull();
            discardResult.GameOver!.WinnerNum.Should().Be(1);
            discardResult.GameOver.Reason.Should().Be(WinReasons.TurnLimit);
            state.CurrentTurn.Should().Be(30);
        }
    }
}
