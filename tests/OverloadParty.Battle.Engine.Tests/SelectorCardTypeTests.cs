using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class SelectorCardTypeTests
{
    private const string ComputeResourceCardId = "TST-0430";
    private const string DataResourceCardId = "TST-0431";
    private const string ComputeInstanceId = "res_compute";
    private const string DataInstanceId = "res_data";

    private const string ComputeAndDataResource = """
        { "owner": "myself", "pick": "choice", "card_type": ["Compute", "DataResource"] }
        """;

    private const string ComputeAndPlatform = """
        { "owner": "myself", "pick": "choice", "card_type": ["Compute", "Platform"] }
        """;

    private const string ComputeOnly = """
        { "owner": "myself", "pick": "choice", "card_type": "Compute" }
        """;

    /// <summary>指定の適用条件で対象を選ばせる、ダメージを 300 回復する起動効果を作る。</summary>
    /// <param name="targetCondition">起動効果の適用条件 (カード定義の selector と同じ形式)。</param>
    /// <returns>起動効果の定義。</returns>
    private static EffectDef MakeChoiceHealEffect(string targetCondition) =>
        new()
        {
            Trigger = TriggerTypes.Ignition,
            Ops =
            [
                JsonDocument.Parse($$$"""
                    {"heal_damage":{"selector":{{{targetCondition}}},"amount":300}}
                    """).RootElement,
            ],
        };

    /// <summary>コンピュート系リソースとデータ系リソースを 1 体ずつ自分のフロントエンドに置いた盤面を作る。</summary>
    /// <param name="cc">2 体のカード定義を登録するカードキャッシュ。</param>
    /// <param name="damage">2 体が負っているダメージ。</param>
    /// <returns>ゲーム状態と、置いた 2 体のリソース。</returns>
    private static (BattleGameState State, DeployedResource Compute, DeployedResource Data)
        MakeStateWithBothResourceTypes(TestCardCache cc, long damage = 0)
    {
        cc.Add(TestFactory.ComputeCard(cardId: ComputeResourceCardId, name: "TestComputeResource"));
        cc.Add(TestFactory.DataCard(
            cardId: DataResourceCardId, subtype: "ObjectStorage", name: "TestObjectStorage"));

        var compute = TestFactory.MakeResource(
            cardId: ComputeResourceCardId, instanceId: ComputeInstanceId, damage: damage);
        var data = TestFactory.MakeResource(
            cardId: DataResourceCardId, instanceId: DataInstanceId, damage: damage);

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Field.Frontend[0] = compute;
        state.Player1Field.Frontend[1] = data;
        return (state, compute, data);
    }

    [Trait("対象", "カードタイプによる選択候補の絞り込み")]
    public class ChoiceCandidates
    {
        private const string ChooserCardId = "TST-0432";

        /// <summary>指定のカードタイプ条件で対象を選ばせる即時カードを手札に持ち、提示される選択候補を返す。</summary>
        /// <param name="targetCondition">起動効果の適用条件 (カード定義の selector と同じ形式)。</param>
        /// <returns>選択候補として提示されたリソースのインスタンス ID 一覧。</returns>
        private static List<string>? ListCandidates(string targetCondition)
        {
            var chooser = new CardDefinition
            {
                CardId = ChooserCardId,
                CardName = "TestChoiceHeal",
                CardType = CardTypes.Strategy,
                DeployTurns = 0,
                Effects = [MakeChoiceHealEffect(targetCondition)],
            };

            var cc = new TestCardCache();
            cc.Add(chooser);
            var (state, _, _) = MakeStateWithBothResourceTypes(cc);

            var registry = new EffectRegistry();
            EffectYamlLoader.LoadEffectSources([chooser], registry, new CustomEffectRegistry());

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = ChooserCardId } };
            var actions = AvailableActions.GetAllAvailableActions(
                state, 1, state.Player1Field, state.Player2Field, hand, 5000, 0, cc, registry);

            return actions.Should().ContainSingle(a => a.Type == ActionTypes.PlayCard).Subject.ValidTargets;
        }

        [Fact(DisplayName = "コンピュート系とデータ系の 2 つのカードタイプを指定した選択効果では、両方のリソースが選択候補になる")]
        public void MultipleCardTypes_ResourcesOfEitherType_AreListedAsCandidates()
        {
            var candidates = ListCandidates(ComputeAndDataResource);

            candidates.Should().BeEquivalentTo([ComputeInstanceId, DataInstanceId]);
        }

        [Fact(DisplayName = "カードタイプを 2 つ指定した選択効果では、そのどちらにも当てはまらないリソースが選択候補にならない")]
        public void MultipleCardTypes_ResourceOfNeitherType_IsNotListedAsCandidate()
        {
            var candidates = ListCandidates(ComputeAndPlatform);

            candidates.Should().Equal(ComputeInstanceId);
        }

        [Fact(DisplayName = "カードタイプをコンピュート系 1 つだけ指定した選択効果では、コンピュート系リソースだけが選択候補になる")]
        public void SingleCardType_OnlyMatchingResource_IsListedAsCandidate()
        {
            var candidates = ListCandidates(ComputeOnly);

            candidates.Should().Equal(ComputeInstanceId);
        }
    }

    [Trait("対象", "カードタイプによる選択対象への効果の適用")]
    public class ChoiceTargets
    {
        private const string IgniterCardId = "TST-0433";
        private const string IgniterInstanceId = "igniter";
        private const long InitialDamage = 500;

        /// <summary>指定のカードタイプ条件で対象を選ばせる回復の起動効果を発動し、盤上の 2 体に残るダメージを返す。</summary>
        /// <param name="targetCondition">起動効果の適用条件 (カード定義の selector と同じ形式)。</param>
        /// <param name="chosenInstanceId">選ぶリソースのインスタンス ID。</param>
        /// <returns>コンピュート系リソースとデータ系リソースに残るダメージ。</returns>
        private static (long OfComputeResource, long OfDataResource) UseHealIgnitionChoosing(
            string targetCondition, string chosenInstanceId)
        {
            var igniter = TestFactory.ComputeCard(cardId: IgniterCardId, deployTurns: 0, name: "TestIgniter");
            igniter.Effects = [MakeChoiceHealEffect(targetCondition)];

            var cc = new TestCardCache();
            cc.Add(igniter);
            var (state, compute, data) = MakeStateWithBothResourceTypes(cc, damage: InitialDamage);
            state.Player1Field.Backend[0] = TestFactory.MakeResource(
                cardId: IgniterCardId, instanceId: IgniterInstanceId);

            var registry = new EffectRegistry();
            EffectYamlLoader.LoadEffectSources([igniter], registry, new CustomEffectRegistry());

            UseIgnitionProcessor.Process(
                state,
                TestFactory.MakeGame(),
                1,
                new UseIgnitionRequest
                {
                    InstanceID = IgniterInstanceId,
                    ChoiceData = new Dictionary<string, object> { ["instanceId"] = chosenInstanceId },
                },
                cc,
                registry);

            return (compute.Damage, data.Damage);
        }

        [Fact(DisplayName = "コンピュート系とデータ系の 2 つのカードタイプを指定した選択効果で、コンピュート系リソースを選ぶと、そのダメージが 500 から 200 に減る")]
        public void MultipleCardTypes_ChoosingComputeResource_Heals()
        {
            var (ofComputeResource, _) = UseHealIgnitionChoosing(ComputeAndDataResource, ComputeInstanceId);

            ofComputeResource.Should().Be(200);
        }

        [Fact(DisplayName = "コンピュート系とデータ系の 2 つのカードタイプを指定した選択効果で、データ系リソースを選ぶと、そのダメージが 500 から 200 に減る")]
        public void MultipleCardTypes_ChoosingDataResource_Heals()
        {
            var (_, ofDataResource) = UseHealIgnitionChoosing(ComputeAndDataResource, DataInstanceId);

            ofDataResource.Should().Be(200);
        }

        [Fact(DisplayName = "カードタイプを 2 つ指定した選択効果で、そのどちらにも当てはまらないリソースを選ぶと、そのダメージが 500 のまま減らない")]
        public void MultipleCardTypes_ChoosingResourceOfNeitherType_DoesNotHeal()
        {
            var (_, ofDataResource) = UseHealIgnitionChoosing(ComputeAndPlatform, DataInstanceId);

            ofDataResource.Should().Be(500);
        }

        [Fact(DisplayName = "カードタイプをコンピュート系 1 つだけ指定した選択効果で、データ系リソースを選ぶと、そのダメージが 500 のまま減らない")]
        public void SingleCardType_ChoosingNonMatchingResource_DoesNotHeal()
        {
            var (_, ofDataResource) = UseHealIgnitionChoosing(ComputeOnly, DataInstanceId);

            ofDataResource.Should().Be(500);
        }
    }
}
