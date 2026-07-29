using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Effects.Ops;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Engine;

/// <summary>
/// パッシブ効果 (フィールド状態から常時再計算される継続効果) が、同一 source からは高々 1 つ・
/// 条件喪失で即消失・全フィールド変化イベントで再評価されることを検証する。
/// 実カード ID は使わず、ダミー効果定義 (on_field_change / on_deploy+while_on_field) で書く。
/// </summary>
public class PassiveRecalculationTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>自分の ObjectStorage が 1 体以上いる間、tp+200 するダミーの on_field_change カードを作る。</summary>
    private static CardDefinition MakeWatcherCard(string cardId)
    {
        var card = TestFactory.ComputeCard(cardId: cardId, name: "Watcher", deployTurns: 0);
        card.Effects =
        [
            new EffectDef
            {
                Trigger = TriggerTypes.OnFieldChange,
                Guard = [Parse("""{"count":{"selector":{"owner":"myself","subtype":"ObjectStorage"},"min":1}}""")],
                Ops = [Parse("""{"apply_buff":{"selector":"source","buff":"tp","amount":200,"duration":"this_turn"}}""")],
            },
        ];
        return card;
    }

    private static EffectRegistry LoadRegistry(params CardDefinition[] cards)
    {
        var registry = new EffectRegistry();
        EffectYamlLoader.LoadEffectSources(cards, registry, new CustomEffectRegistry());
        return registry;
    }

    private static AttackRequest Atk(string attacker, string target) =>
        new() { AttackerInstanceID = attacker, TargetInstanceID = target };

    private static UseIgnitionRequest Use(string instanceId, string? targetInstanceId = null) =>
        new() { InstanceID = instanceId, TargetInstanceID = targetInstanceId };

    [Trait("対象", "パッシブ効果の再計算 (スタック防止・条件追従)")]
    public class StackPreventionAndConditionTracking
    {
        [Fact(DisplayName = "条件成立中にフィールド変化が同一ターンに2回起きても、実効TPは+200に留まる")]
        public void FieldChangesTwiceWhileConditionHolds_EffectiveTPStaysAt200Bonus()
        {
            var watcherCard = MakeWatcherCard("TST-9001");
            var cc = new TestCardCache();
            cc.Add(watcherCard);
            cc.Add(TestFactory.DataCard(cardId: "TST-9002", subtype: "ObjectStorage"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-9003", name: "Filler", deployTurns: 0));
            var registry = LoadRegistry(watcherCard);
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var watcher = TestFactory.MakeResource(cardId: "TST-9001", instanceId: "watcher", faceUp: true);
            state.Player1Field.Frontend[0] = watcher;
            state.Player1Field.Backend[0] = TestFactory.MakeResource(cardId: "TST-9002", instanceId: "os1", faceUp: true);
            state.Player1Hand =
            [
                new UndeployedCard { InstanceID = "h1", CardID = "TST-9003" },
                new UndeployedCard { InstanceID = "h2", CardID = "TST-9003" },
            ];

            PlayCardProcessor.Process(state, game, 1, new PlayCardRequest { CardInstanceID = "h1", Zone = Zones.Backend, Index = 1 }, cc, registry);
            PlayCardProcessor.Process(state, game, 1, new PlayCardRequest { CardInstanceID = "h2", Zone = Zones.Backend, Index = 2 }, cc, registry);

            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                800, "同一 source からのバフは洗い替えのため何度再計算されても1つに留まる");
        }

        [Fact(DisplayName = "条件対象(オブジェクトストレージ)が0体のとき、実効TPに加算はない")]
        public void ZeroMatchingResources_NoBonusApplied()
        {
            var watcherCard = MakeWatcherCard("TST-9001");
            var cc = new TestCardCache();
            cc.Add(watcherCard);
            cc.Add(TestFactory.ComputeCard(cardId: "TST-9003", name: "Filler", deployTurns: 0));
            var registry = LoadRegistry(watcherCard);
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var watcher = TestFactory.MakeResource(cardId: "TST-9001", instanceId: "watcher", faceUp: true);
            state.Player1Field.Frontend[0] = watcher;
            state.Player1Hand = [new UndeployedCard { InstanceID = "h1", CardID = "TST-9003" }];

            PlayCardProcessor.Process(state, game, 1, new PlayCardRequest { CardInstanceID = "h1", Zone = Zones.Backend, Index = 0 }, cc, registry);

            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                600, "条件対象がいなければ加算されない");
        }

        [Fact(DisplayName = "条件対象(オブジェクトストレージ)が1体のとき、実効TPは+200になる")]
        public void OneMatchingResource_EffectiveTPGets200Bonus()
        {
            var watcherCard = MakeWatcherCard("TST-9001");
            var cc = new TestCardCache();
            cc.Add(watcherCard);
            cc.Add(TestFactory.DataCard(cardId: "TST-9002", subtype: "ObjectStorage"));
            var registry = LoadRegistry(watcherCard);
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var watcher = TestFactory.MakeResource(cardId: "TST-9001", instanceId: "watcher", faceUp: true);
            state.Player1Field.Frontend[0] = watcher;
            state.Player1Hand = [new UndeployedCard { InstanceID = "h1", CardID = "TST-9002" }];

            PlayCardProcessor.Process(state, game, 1, new PlayCardRequest { CardInstanceID = "h1", Zone = Zones.Backend, Index = 0 }, cc, registry);

            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(800);
        }

        [Fact(DisplayName = "条件対象(オブジェクトストレージ)が2体のとき、実効TPは+200のまま (min1条件は件数に比例しない)")]
        public void TwoMatchingResources_EffectiveTPStaysAt200Bonus()
        {
            var watcherCard = MakeWatcherCard("TST-9001");
            var cc = new TestCardCache();
            cc.Add(watcherCard);
            cc.Add(TestFactory.DataCard(cardId: "TST-9002", subtype: "ObjectStorage"));
            var registry = LoadRegistry(watcherCard);
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var watcher = TestFactory.MakeResource(cardId: "TST-9001", instanceId: "watcher", faceUp: true);
            state.Player1Field.Frontend[0] = watcher;
            state.Player1Hand =
            [
                new UndeployedCard { InstanceID = "h1", CardID = "TST-9002" },
                new UndeployedCard { InstanceID = "h2", CardID = "TST-9002" },
            ];

            PlayCardProcessor.Process(state, game, 1, new PlayCardRequest { CardInstanceID = "h1", Zone = Zones.Backend, Index = 0 }, cc, registry);
            PlayCardProcessor.Process(state, game, 1, new PlayCardRequest { CardInstanceID = "h2", Zone = Zones.Backend, Index = 1 }, cc, registry);

            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                800, "min 1 の条件は対象の件数に比例しない");
        }

        [Fact(DisplayName = "条件対象が攻撃で破壊され場を離れたとき、実効TPが基礎値に戻る")]
        public void ConditionResourceDestroyedByAttack_RevertsToBaseValue()
        {
            var watcherCard = MakeWatcherCard("TST-9001");
            var cc = new TestCardCache();
            cc.Add(watcherCard);
            cc.Add(TestFactory.DataCard(cardId: "TST-9002", subtype: "ObjectStorage", av: 400));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-9004", name: "Attacker", tp: 1500));
            var registry = LoadRegistry(watcherCard);
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
            var watcher = TestFactory.MakeResource(cardId: "TST-9001", instanceId: "watcher", faceUp: true);
            state.Player2Field.Frontend[0] = watcher;
            state.Player2Field.Frontend[1] = TestFactory.MakeResource(
                cardId: "TST-9002", instanceId: "os1", faceUp: true, maxAV: 400);
            state.Player1Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-9004", instanceId: "attacker", faceUp: true, maxTP: 1500);
            PassiveRecalculator.Recalculate(state, game, cc, registry);
            StatCalculator.CalculateEffectiveTP(watcher, state.Player2Field, cc).Should().Be(800);

            AttackProcessor.Process(state, game, 1, Atk("attacker", "os1"), cc, registry);

            StatCalculator.CalculateEffectiveTP(watcher, state.Player2Field, cc).Should().Be(
                600, "条件対象の破壊で発動条件を失い基礎値に戻る");
        }
    }

    [Trait("対象", "パッシブ効果の再計算 (発火漏れ経路)")]
    public class MissedRecalculationTriggers
    {
        [Fact(DisplayName = "裏向きでデプロイしたリソースがドローフェーズで表向きになったとき、条件成立バフが実効TPに反映される")]
        public void FaceDownResourceFlipsFaceUpInDrawPhase_ReflectsInEffectiveTP()
        {
            var watcherCard = MakeWatcherCard("TST-9001");
            var cc = new TestCardCache();
            cc.Add(watcherCard);
            cc.Add(TestFactory.DataCard(cardId: "TST-9002", subtype: "ObjectStorage"));
            var registry = LoadRegistry(watcherCard);
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Draw, activePlayer: 1);
            state.Player1Repository.Add(new UndeployedCard { InstanceID = "repo_1", CardID = "TST-9001" });
            var watcher = TestFactory.MakeResource(cardId: "TST-9001", instanceId: "watcher", faceUp: true);
            state.Player1Field.Frontend[0] = watcher;
            var objectStorage = TestFactory.MakeResource(cardId: "TST-9002", instanceId: "os1", faceUp: false, deployLeft: 1);
            state.Player1Field.Backend[0] = objectStorage;
            PassiveRecalculator.Recalculate(state, game, cc, registry);
            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                600, "裏向きの間は条件対象として数えない");

            DrawPhaseProcessor.Process(state, game, cc, registry);

            objectStorage.FaceUp.Should().BeTrue();
            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                800, "表向きに反転した稼働完了で条件成立バフが反映される");
        }

        [Fact(DisplayName = "効果デプロイのスロット選択を解決したとき、条件成立バフが実効TPに反映される")]
        public void EffectDeploySlotSelectionResolved_ReflectsInEffectiveTP()
        {
            var watcherCard = MakeWatcherCard("TST-9001");
            var cc = new TestCardCache();
            cc.Add(watcherCard);
            cc.Add(TestFactory.DataCard(cardId: "TST-9002", subtype: "ObjectStorage"));
            var registry = LoadRegistry(watcherCard);
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(phase: Phase.Main);
            var watcher = TestFactory.MakeResource(cardId: "TST-9001", instanceId: "watcher", faceUp: true);
            state.Player1Field.Frontend[0] = watcher;
            PassiveRecalculator.Recalculate(state, game, cc, registry);
            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(600);

            state.PendingSlotSelects.Add(new AwaitingSlotSelect
            {
                PlayerNum = 1,
                Resource = TestFactory.MakeResource(cardId: "TST-9002", instanceId: "os1", faceUp: true),
                ValidZones = ["backend_0"],
            });

            SelectSlotProcessor.Process(state, game, 1, new SelectSlotRequest { Zone = "backend", Index = 0 }, cc, registry);

            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                800, "効果デプロイの配置確定で条件成立バフが反映される");
        }

        [Fact(DisplayName = "サポート破壊 (destroy_platform) で条件 (サポート枚数) を失ったとき、実効TPが基礎値に戻る")]
        public void SupportDestroyedByDestroyPlatform_ConditionLost_RevertsToBaseValue()
        {
            var watcherCard = TestFactory.ComputeCard(cardId: "TST-9005", name: "Watcher", deployTurns: 0);
            watcherCard.Effects =
            [
                new EffectDef
                {
                    Trigger = TriggerTypes.OnFieldChange,
                    Guard = [Parse("""{"count":{"selector":{"owner":"myself","zone":"support","card_id":"TST-9010"},"min":1}}""")],
                    Ops = [Parse("""{"apply_buff":{"selector":"source","buff":"tp","amount":200,"duration":"this_turn"}}""")],
                },
            ];
            var cc = new TestCardCache();
            cc.Add(watcherCard);
            cc.Add(TestFactory.PlatformCard(cardId: "TST-9010"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-9020", name: "Destroyer"));
            var registry = LoadRegistry(watcherCard);
            registry.RegisterComposed("TST-9020", TriggerType.Ignition, new DestroyPlatformOp());
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var watcher = TestFactory.MakeResource(cardId: "TST-9005", instanceId: "watcher", faceUp: true);
            state.Player1Field.Frontend[0] = watcher;
            state.Player1Field.Support[0] = new DeployedSupport { InstanceID = "plat_1", CardID = "TST-9010", FaceUp = true };
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-9020", instanceId: "destroyer", faceUp: true);
            PassiveRecalculator.Recalculate(state, game, cc, registry);
            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(800);

            UseIgnitionProcessor.Process(state, game, 2, Use("destroyer"), cc, registry);

            state.Player1Field.Support.Select(s => s.InstanceID).Should().NotContain("plat_1");
            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                600, "サポート破壊で発動条件を失い基礎値に戻る");
        }

        [Fact(DisplayName = "destroy_check による破壊で条件を失ったとき、実効TPが基礎値に戻る")]
        public void DestroyCheckDestruction_ConditionLost_RevertsToBaseValue()
        {
            var watcherCard = MakeWatcherCard("TST-9001");
            var cc = new TestCardCache();
            cc.Add(watcherCard);
            cc.Add(TestFactory.DataCard(cardId: "TST-9002", subtype: "ObjectStorage", av: 800));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-9021", name: "Destroyer"));
            var registry = LoadRegistry(watcherCard);
            registry.RegisterComposed("TST-9021", TriggerType.Ignition, new DestroyCheckOp());
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var watcher = TestFactory.MakeResource(cardId: "TST-9001", instanceId: "watcher", faceUp: true);
            state.Player1Field.Frontend[0] = watcher;
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(
                cardId: "TST-9021", instanceId: "destroyer", faceUp: true);
            state.Player1Field.Backend[0] = TestFactory.MakeResource(
                cardId: "TST-9002", instanceId: "os1", faceUp: true, maxAV: 800, damage: 800);
            PassiveRecalculator.Recalculate(state, game, cc, registry);
            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(800);

            UseIgnitionProcessor.Process(state, game, 1, Use("destroyer"), cc, registry);

            FieldHelpers.FindResourceByID(state.Player1Field, "os1").Should().BeNull();
            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                600, "destroy_check による破壊で発動条件を失い基礎値に戻る");
        }

        /// <summary>サポートゾーンに 1 枚でも存在すれば成立する述語 (裏向きも数える、テスト専用)。</summary>
        private sealed class AnySupportPresentGuard : IEffectGuard
        {
            public bool Check(EffectContext ctx) => ctx.State.GetField(ctx.PlayerNum).Support.Any();
        }

        [Fact(DisplayName = "リアクティブ消費で条件 (サポート枚数) を失ったとき、実効TPが基礎値に戻る")]
        public void ReactiveConsumed_ConditionLost_RevertsToBaseValue()
        {
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-9006", name: "Watcher"));
            cc.Add(TestFactory.ReactiveCard(cardId: "TST-9022"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-9030", name: "OpponentFiller", deployTurns: 0));
            var registry = new TestEffectRegistry();
            registry.RegisterPassive("TST-9006", new PassiveEffectDef
            {
                Guards = [new AnySupportPresentGuard()],
                Applications =
                [
                    new PassiveBuffApplication
                    {
                        Selector = SourceSelector.Instance,
                        EffectType = EffectTypes.BuffTP,
                        Amount = new StaticAmount(200),
                        Mode = "",
                    },
                ],
            });
            // 発動条件を持たないリアクティブ。発火すれば必ず消費される。
            registry.Register("TST-9022", TriggerType.OnDeploy, _ => new EffectResult());
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var watcher = TestFactory.MakeResource(cardId: "TST-9006", instanceId: "watcher", faceUp: true);
            state.Player1Field.Frontend[0] = watcher;
            state.Player1Field.Support[0] = new DeployedSupport { InstanceID = "reactive_1", CardID = "TST-9022", FaceUp = false };
            state.Player2Hand = [new UndeployedCard { InstanceID = "h1", CardID = "TST-9030" }];
            PassiveRecalculator.Recalculate(state, game, cc, registry);
            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                800, "裏向きでもサポートゾーンに存在すれば条件を満たす");

            PlayCardProcessor.Process(state, game, 2, new PlayCardRequest { CardInstanceID = "h1", Zone = Zones.Frontend, Index = 0 }, cc, registry);

            state.Player1Field.Support[0].Should().BeNull("発動したリアクティブはトラッシュへ送られる");
            StatCalculator.CalculateEffectiveTP(watcher, state.Player1Field, cc).Should().Be(
                600, "リアクティブ消費で発動条件を失い基礎値に戻る");
        }
    }

    [Trait("対象", "パッシブ効果の再計算 (プラットフォーム型 while_on_field)")]
    public class PlatformTypePassiveEffect
    {
        private static CardDefinition MakePlatformCard(string cardId)
        {
            var card = TestFactory.PlatformCard(cardId: cardId);
            card.Effects =
            [
                new EffectDef
                {
                    Trigger = TriggerTypes.OnDeploy,
                    Ops = [Parse("""{"apply_buff":{"selector":{"owner":"myself"},"buff":"tp","amount":200,"duration":"while_on_field"}}""")],
                },
            ];
            return card;
        }

        [Fact(DisplayName = "プラットフォーム稼働中は自リソースの実効TPが+200になり、相手が2回デプロイしても+200のまま")]
        public void PlatformActive_EffectiveTPStaysAt200Bonus_EvenAfterTwoOpponentDeploys()
        {
            var platformCard = MakePlatformCard("TST-9011");
            var cc = new TestCardCache();
            cc.Add(platformCard);
            cc.Add(TestFactory.ComputeCard(cardId: "TST-9012", name: "MyResource"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-9013", name: "OpponentFiller", deployTurns: 0));
            var registry = LoadRegistry(platformCard);
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Support[0] = new DeployedSupport { InstanceID = "plat_1", CardID = "TST-9011", FaceUp = true };
            var myResource = TestFactory.MakeResource(cardId: "TST-9012", instanceId: "my_1", faceUp: true);
            state.Player1Field.Frontend[0] = myResource;
            state.Player2Hand =
            [
                new UndeployedCard { InstanceID = "h1", CardID = "TST-9013" },
                new UndeployedCard { InstanceID = "h2", CardID = "TST-9013" },
            ];
            PassiveRecalculator.Recalculate(state, game, cc, registry);
            StatCalculator.CalculateEffectiveTP(myResource, state.Player1Field, cc).Should().Be(800);

            PlayCardProcessor.Process(state, game, 2, new PlayCardRequest { CardInstanceID = "h1", Zone = Zones.Frontend, Index = 0 }, cc, registry);
            StatCalculator.CalculateEffectiveTP(myResource, state.Player1Field, cc).Should().Be(
                800, "相手の1回目のデプロイでも+200のまま (旧モデルのスタック根絶)");

            PlayCardProcessor.Process(state, game, 2, new PlayCardRequest { CardInstanceID = "h2", Zone = Zones.Frontend, Index = 1 }, cc, registry);
            StatCalculator.CalculateEffectiveTP(myResource, state.Player1Field, cc).Should().Be(
                800, "相手の2回目のデプロイでも+200のまま (旧モデルのスタック根絶)");
        }

        [Fact(DisplayName = "プラットフォームが破壊された瞬間、実効TPが基礎値に戻る")]
        public void PlatformDestroyed_RevertsToBaseValue()
        {
            var platformCard = MakePlatformCard("TST-9011");
            var cc = new TestCardCache();
            cc.Add(platformCard);
            cc.Add(TestFactory.ComputeCard(cardId: "TST-9012", name: "MyResource"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-9014", name: "Destroyer"));
            var registry = LoadRegistry(platformCard);
            registry.RegisterComposed("TST-9014", TriggerType.Ignition, new DestroyPlatformOp());
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Support[0] = new DeployedSupport { InstanceID = "plat_1", CardID = "TST-9011", FaceUp = true };
            var myResource = TestFactory.MakeResource(cardId: "TST-9012", instanceId: "my_1", faceUp: true);
            state.Player1Field.Frontend[0] = myResource;
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(cardId: "TST-9014", instanceId: "destroyer", faceUp: true);
            PassiveRecalculator.Recalculate(state, game, cc, registry);
            StatCalculator.CalculateEffectiveTP(myResource, state.Player1Field, cc).Should().Be(800);

            UseIgnitionProcessor.Process(state, game, 2, Use("destroyer"), cc, registry);

            state.Player1Field.Support.Select(s => s.InstanceID).Should().NotContain("plat_1");
            StatCalculator.CalculateEffectiveTP(myResource, state.Player1Field, cc).Should().Be(
                600, "Platform破壊で発動条件を失い基礎値に戻る");
        }

        [Fact(DisplayName = "ソース ID に使われうる相手リソースが破壊されても、プラットフォーム稼働中は+200が維持される")]
        public void UnrelatedOpponentResourceDestroyed_PlatformBonusPersists()
        {
            var platformCard = MakePlatformCard("TST-9011");
            var cc = new TestCardCache();
            cc.Add(platformCard);
            cc.Add(TestFactory.ComputeCard(cardId: "TST-9012", name: "MyResource"));
            cc.Add(TestFactory.ComputeCard(cardId: "TST-9015", name: "Destroyer"));
            var registry = LoadRegistry(platformCard);
            registry.RegisterComposed("TST-9015", TriggerType.Ignition, new DestroyCheckOp());
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            state.Player1Field.Support[0] = new DeployedSupport { InstanceID = "plat_1", CardID = "TST-9011", FaceUp = true };
            var myResource = TestFactory.MakeResource(cardId: "TST-9012", instanceId: "my_1", faceUp: true);
            state.Player1Field.Frontend[0] = myResource;
            state.Player1Field.Frontend[1] = TestFactory.MakeResource(cardId: "TST-9015", instanceId: "destroyer", faceUp: true);
            state.Player2Field.Frontend[0] = TestFactory.MakeResource(
                cardId: "TST-9012", instanceId: "opp_victim", faceUp: true, maxAV: 800, damage: 800);
            PassiveRecalculator.Recalculate(state, game, cc, registry);
            StatCalculator.CalculateEffectiveTP(myResource, state.Player1Field, cc).Should().Be(800);

            UseIgnitionProcessor.Process(state, game, 1, Use("destroyer"), cc, registry);

            FieldHelpers.FindResourceByID(state.Player2Field, "opp_victim").Should().BeNull();
            StatCalculator.CalculateEffectiveTP(myResource, state.Player1Field, cc).Should().Be(
                800, "無関係な相手リソースの破壊ではPlatform由来のバフは失われない");
        }
    }

    [Trait("対象", "アタッチメント由来のパッシブ効果")]
    public class AttachmentDerivedPassiveEffect
    {
        /// <summary>自身を装備したリソースの実効 TP を +200 する while_on_field のダミーアタッチメントカードを作る。</summary>
        private static CardDefinition MakeAttachmentCard(string cardId)
        {
            var card = TestFactory.AttachmentCard(cardId: cardId, name: "Booster");
            card.Effects =
            [
                new EffectDef
                {
                    Trigger = TriggerTypes.OnFieldChange,
                    Ops = [Parse("""{"apply_buff":{"selector":"source","buff":"tp","amount":200,"duration":"while_on_field"}}""")],
                },
            ];
            return card;
        }

        [Fact(DisplayName = "アタッチメントの常時強化が、装備先のリソースに適用される")]
        public void Attach_AppliesContinuousBuffToHost()
        {
            var attachmentCard = MakeAttachmentCard("TST-0910");
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));
            cc.Add(attachmentCard);
            var registry = LoadRegistry(attachmentCard);

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var host = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "host", faceUp: true, maxTP: 600, currentTP: 600);
            state.Player1Field.Frontend[0] = host;
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_att", CardID = "TST-0910" }];

            PlayCardProcessor.Process(state, TestFactory.MakeGame(), 1,
                new PlayCardRequest { CardInstanceID = "h_att", Zone = Zones.Support, Index = 0, TargetInstanceID = "host" },
                cc, registry);

            StatCalculator.CalculateEffectiveTP(host, state.Player1Field, cc).Should().Be(800);
        }

        [Fact(DisplayName = "アタッチメントが離れると、装備先の強化が消える")]
        public void Detach_RemovesContinuousBuffFromHost()
        {
            var attachmentCard = MakeAttachmentCard("TST-0911");
            var cc = new TestCardCache();
            cc.Add(TestFactory.ComputeCard(cardId: "TST-0001", tp: 600));
            cc.Add(attachmentCard);
            var registry = LoadRegistry(attachmentCard);
            var game = TestFactory.MakeGame();

            var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
            var host = TestFactory.MakeResource(cardId: "TST-0001", instanceId: "host", faceUp: true, maxTP: 600, currentTP: 600);
            state.Player1Field.Frontend[0] = host;
            state.Player1Hand = [new UndeployedCard { InstanceID = "h_att", CardID = "TST-0911" }];

            PlayCardProcessor.Process(state, game, 1,
                new PlayCardRequest { CardInstanceID = "h_att", Zone = Zones.Support, Index = 0, TargetInstanceID = "host" },
                cc, registry);
            var attachmentInstanceId = state.Player1Field.Support[0]!.InstanceID;

            FieldHelpers.DestroySupport(state, game, 1, state.Player1Field, attachmentInstanceId, cc, registry);

            StatCalculator.CalculateEffectiveTP(host, state.Player1Field, cc).Should().Be(600);
        }
    }
}
