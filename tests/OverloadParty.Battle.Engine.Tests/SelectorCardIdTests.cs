using System.Text.Json;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;

namespace OverloadParty.Battle.Tests.Effects;

public class SelectorCardIdTests
{
    private const string CardIdA = "TST-0412";
    private const string CardIdB = "TST-0413";
    private const string InstanceOfCardA = "res_a";
    private const string InstanceOfCardB = "res_b";

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    /// <summary>カード ID の異なる 2 体のデータ系リソースを自分のバックエンドに置いた盤面を作る。</summary>
    /// <param name="cc">2 体のカード定義を登録するカードキャッシュ。</param>
    /// <returns>ゲーム状態。</returns>
    private static BattleGameState MakeStateWithTwoDataResources(TestCardCache cc)
    {
        cc.Add(TestFactory.DataCard(cardId: CardIdA, name: "TestDataA"));
        cc.Add(TestFactory.DataCard(cardId: CardIdB, name: "TestDataB"));

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Field.Backend[0] = TestFactory.MakeResource(cardId: CardIdA, instanceId: InstanceOfCardA);
        state.Player1Field.Backend[1] = TestFactory.MakeResource(cardId: CardIdB, instanceId: InstanceOfCardB);
        return state;
    }

    [Trait("対象", "カード ID による効果対象の絞り込み")]
    public class BuffTargets
    {
        private const string IgnitionCardId = "TST-0410";
        private const string IgniterInstanceId = "igniter";

        /// <summary>指定の対象条件でイールドを 200 上げる起動効果を使い、2 体のリソースの実効イールドを返す。</summary>
        /// <param name="selectorJson">効果の対象条件として渡す JSON。</param>
        /// <returns>カード A のリソースとカード B のリソースの実効イールド。</returns>
        private static (long OfCardA, long OfCardB) UseYieldBuffIgnition(string selectorJson)
        {
            var igniter = TestFactory.ComputeCard(cardId: IgnitionCardId, deployTurns: 0, name: "TestIgniter");
            igniter.Effects =
            [
                new EffectDef
                {
                    Trigger = TriggerTypes.Ignition,
                    Ops =
                    [
                        Parse($$$"""
                            {"apply_buff":{"selector":{{{selectorJson}}},"buff":"yield","amount":200,"duration":"this_turn"}}
                            """),
                    ],
                },
            ];

            var cc = new TestCardCache();
            cc.Add(igniter);
            var state = MakeStateWithTwoDataResources(cc);
            state.Player1Field.Frontend[0] =
                TestFactory.MakeResource(cardId: IgnitionCardId, instanceId: IgniterInstanceId);

            var registry = new EffectRegistry();
            EffectYamlLoader.LoadEffectSources([igniter], registry, new CustomEffectRegistry());

            UseIgnitionProcessor.Process(state, TestFactory.MakeGame(), 1,
                new UseIgnitionRequest { InstanceID = IgniterInstanceId }, cc, registry);

            var field = state.Player1Field;
            return (
                StatCalculator.CalculateEffectiveInsight(field.Backend[0]!, field, cc),
                StatCalculator.CalculateEffectiveInsight(field.Backend[1]!, field, cc));
        }

        [Fact(DisplayName = "効果の対象にカード ID を 1 つ指定したとき、その ID のリソースの実効イールドが 400 から 600 に上がる")]
        public void SingleCardId_MatchingResource_GetsBuff()
        {
            var (ofCardA, _) = UseYieldBuffIgnition($$"""{"owner":"myself","card_id":"{{CardIdA}}"}""");

            ofCardA.Should().Be(600);
        }

        [Fact(DisplayName = "効果の対象にカード ID を 1 つ指定したとき、指定していない ID のリソースの実効イールドは 400 のまま変わらない")]
        public void SingleCardId_OtherResource_KeepsBaseYield()
        {
            var (_, ofCardB) = UseYieldBuffIgnition($$"""{"owner":"myself","card_id":"{{CardIdA}}"}""");

            ofCardB.Should().Be(400);
        }

        [Fact(DisplayName = "効果の対象にカード ID を 2 つ指定したとき、どちらの ID のリソースも実効イールドが 600 になる")]
        public void MultipleCardIds_AllListedResources_GetBuff()
        {
            var (ofCardA, ofCardB) =
                UseYieldBuffIgnition($$"""{"owner":"myself","card_id":["{{CardIdA}}","{{CardIdB}}"]}""");

            ofCardA.Should().Be(600);
            ofCardB.Should().Be(600);
        }

        [Fact(DisplayName = "効果の対象にカード ID を指定しないとき、ID によらずどのリソースも実効イールドが 600 になる")]
        public void NoCardId_AllResources_GetBuff()
        {
            var (ofCardA, ofCardB) = UseYieldBuffIgnition("""{"owner":"myself"}""");

            ofCardA.Should().Be(600);
            ofCardB.Should().Be(600);
        }
    }

    [Trait("対象", "カード ID による選択候補の絞り込み")]
    public class ChoiceCandidates
    {
        private const string ChoiceCardId = "TST-0411";

        [Fact(DisplayName = "対象を選ぶ効果でカード ID を指定したとき、その ID のリソースだけが選択候補になる")]
        public void SingleCardId_OnlyMatchingResource_IsListedAsCandidate()
        {
            var chooser = new CardDefinition
            {
                CardId = ChoiceCardId,
                CardName = "TestChoiceStrategy",
                CardType = CardTypes.Strategy,
                DeployTurns = 0,
                Effects =
                [
                    new EffectDef
                    {
                        Trigger = TriggerTypes.Ignition,
                        Ops =
                        [
                            Parse($$$"""
                                {"heal_damage":{"selector":{"owner":"myself","pick":"choice","card_id":"{{{CardIdA}}}"},"amount":300}}
                                """),
                        ],
                    },
                ],
            };

            var cc = new TestCardCache();
            cc.Add(chooser);
            var state = MakeStateWithTwoDataResources(cc);

            var registry = new EffectRegistry();
            EffectYamlLoader.LoadEffectSources([chooser], registry, new CustomEffectRegistry());

            var hand = new List<UndeployedCard> { new() { InstanceID = "h_1", CardID = ChoiceCardId } };
            var actions = AvailableActions.GetAllAvailableActions(
                state, 1, state.Player1Field, state.Player2Field, hand, 5000, 0, cc, registry);

            var play = actions.Should().ContainSingle(a => a.Type == ActionTypes.PlayCard).Subject;
            play.ValidTargets.Should().Equal(InstanceOfCardA);
        }
    }
}
