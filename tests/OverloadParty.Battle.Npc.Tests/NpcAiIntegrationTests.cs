using System.Reflection;
using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;
using OverloadParty.Battle.Service;
using GD = OverloadParty.GameState;

namespace OverloadParty.Battle.Tests.Npc;

/// <summary>
/// NPC は本番同様 GameStateView 経由で生成した情報秘匿済み ClientGameState を消費する。
/// </summary>
[Trait("対象", "実設定を用いた NPC の意思決定")]
public class NpcAiIntegrationTests
{
    private readonly EffectRegistry _effects;
    private readonly ICardCache _cc;
    private readonly Dictionary<string, AiConfig> _configs;

    public NpcAiIntegrationTests()
    {
        var (effects, cc) = TestEffectSetup.Get();
        _effects = effects;
        _cc = cc;

        _configs = LoadEmbeddedConfigs();
    }

    /// <summary>
    /// NPC YAML はテストアセンブリに埋め込み済み (csproj の &lt;EmbeddedResource&gt;)。
    /// 物理パスに依存せず CI/ローカル/IDE どこでも同じ挙動にする。
    /// </summary>
    private static Dictionary<string, AiConfig> LoadEmbeddedConfigs()
    {
        var asm = Assembly.GetExecutingAssembly();
        var prefix = "NpcAi.";
        var configs = new Dictionary<string, AiConfig>();
        foreach (var resourceName in asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".yaml", StringComparison.Ordinal)))
        {
            using var stream = asm.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"embedded resource missing: {resourceName}");
            using var reader = new StreamReader(stream);
            var yaml = reader.ReadToEnd();
            var config = AiConfigLoader.LoadFromString(yaml);
            configs[config.Model] = config;
        }
        return configs;
    }

    // ═══════════════════════════════════════════════════════════════
    //  YAML loading
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "埋め込みの全設定が読み込まれ、8 件になる")]
    public void AllConfigs_LoadSuccessfully()
    {
        _configs.Should().HaveCount(8);
        _configs.Keys.Should().Contain("SHE-easy");
        _configs.Keys.Should().Contain("Tenki-hard");
    }

    [Theory(DisplayName = "各モデルのデッキが規定枚数ちょうどになる")]
    [InlineData("SHE-easy")]
    [InlineData("SHE-hard")]
    [InlineData("Tenki-easy")]
    [InlineData("Tenki-hard")]
    [InlineData("Sugar-easy")]
    [InlineData("Sugar-hard")]
    [InlineData("Tuners-easy")]
    [InlineData("Tuners-hard")]
    public void Config_DeckHas30Cards(string model)
    {
        var config = _configs[model];
        var total = config.Deck.Sum(e => e.Copies);
        total.Should().Be(InitialValues.DeckSize,
            $"{model} deck should have exactly {InitialValues.DeckSize} cards");
    }

    [Theory(DisplayName = "各モデルのデッキのカードが全てカードキャッシュに存在する")]
    [InlineData("SHE-easy")]
    [InlineData("Tenki-hard")]
    [InlineData("Sugar-easy")]
    [InlineData("Tuners-hard")]
    public void Config_AllDeckCardsExistInCardCache(string model)
    {
        var config = _configs[model];
        foreach (var entry in config.Deck)
        {
            _cc.Get(entry.CardId).Should().NotBeNull(
                $"card {entry.CardId} in {model} deck should exist in card cache");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  メインフェーズ: デプロイ優先度に従う
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "設定に従い、Compute系リソースを Data系リソースより先にデプロイする")]
    public void MainPhase_DeploysComputeBeforeData_PerConfig()
    {
        var config = _configs["SHE-easy"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand =
        [
            new() { InstanceID = "h_db", CardID = "SH-0007" },
            new() { InstanceID = "h_compute", CardID = "SH-0001" },
        ];
        state.Player1Budget = 5000;

        var clientState = BuildClientState(state, 1);

        var actions = ai.DecideMainPhaseActions(clientState);

        var deploys = actions
            .Where(a => a.ActionType == ActionTypes.PlayCard)
            .ToList();

        deploys.Should().NotBeEmpty();
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_compute");
    }

    [Fact(DisplayName = "デプロイ時の分岐選択で設定の値を使う")]
    public void MainPhase_DeployChoice_UsesConfigValue()
    {
        var config = _configs["SHE-easy"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand =
        [
            new() { InstanceID = "h_0006", CardID = "SH-0006" },
        ];
        state.Player1Budget = 5000;

        var clientState = BuildClientState(state, 1);

        var actions = ai.DecideMainPhaseActions(clientState);

        var deploy = actions.FirstOrDefault(a =>
            a.ActionType == ActionTypes.PlayCard &&
            ((PlayCardRequest)a.Data).CardInstanceID == "h_0006");

        deploy.Should().NotBeNull("SH-0006 should be deployed");

        var req = (PlayCardRequest)deploy!.Data;
        if (req.ChoiceData is not null)
        {
            ((string)req.ChoiceData["option"]).Should().Be("use");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  メインフェーズ: デプロイ ZonePreferences
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Compute系リソースをフロントエンドにデプロイする")]
    public void MainPhase_ComputeDeploysToFrontend()
    {
        var config = _configs["SHE-easy"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand =
        [
            new() { InstanceID = "h_compute", CardID = "SH-0001" },
        ];
        state.Player1Budget = 5000;

        var clientState = BuildClientState(state, 1);

        var actions = ai.DecideMainPhaseActions(clientState);
        var deploy = actions.FirstOrDefault(a =>
            a.ActionType == ActionTypes.PlayCard &&
            ((PlayCardRequest)a.Data).CardInstanceID == "h_compute");

        deploy.Should().NotBeNull();
        var req = (PlayCardRequest)deploy!.Data;
        req.Zone.Should().Be("frontend");
    }

    // ═══════════════════════════════════════════════════════════════
    //  メインフェーズ: スケールアップは config のインスタンスファミリーを使う
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "SHE 設定のスケールアップはインスタンスファミリー M を使う")]
    public void MainPhase_ScaleUp_UsesSHEInstanceFamily_M()
    {
        var config = _configs["SHE-easy"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "fe_1", rank: Rank.Small);
        state.Player1Budget = 5000;

        var clientState = BuildClientState(state, 1);

        var actions = ai.DecideMainPhaseActions(clientState);

        var scaleUp = actions.FirstOrDefault(a => a.ActionType == ActionTypes.ScaleUp);
        if (scaleUp is not null)
        {
            var req = (ScaleUpRequest)scaleUp.Data;
            if (req.InstanceFamily is not null)
            {
                req.InstanceFamily.Should().Be("M");
            }
        }
    }

    [Fact(DisplayName = "Tenki 設定のスケールアップはインスタンスファミリー R を使う")]
    public void MainPhase_ScaleUp_UsesTenkiInstanceFamily_R()
    {
        var config = _configs["Tenki-easy"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "fe_1", rank: Rank.Small);
        state.Player1Budget = 5000;

        var clientState = BuildClientState(state, 1);

        var actions = ai.DecideMainPhaseActions(clientState);

        var scaleUp = actions.FirstOrDefault(a => a.ActionType == ActionTypes.ScaleUp);
        if (scaleUp is not null)
        {
            var req = (ScaleUpRequest)scaleUp.Data;
            if (req.InstanceFamily is not null)
            {
                req.InstanceFamily.Should().Be("R");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  バトルフェーズ: ターゲット選択は config に従う
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "easy 設定は可用性が最も低い相手リソースを攻撃する")]
    public void BattlePhase_EasyConfig_AttacksWeakestAV()
    {
        var config = _configs["SHE-easy"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "attacker");
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "strong", maxAV: 2000);
        state.Player2Field.Frontend[1] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "weak", maxAV: 400);

        var clientState = BuildClientState(state, 1);

        var actions = ai.DecideBattlePhaseActions(clientState);

        var attack = actions.FirstOrDefault(a => a.ActionType == ActionTypes.Attack);
        attack.Should().NotBeNull();
        ((AttackRequest)attack!.Data).TargetInstanceID.Should().Be("weak");
    }

    [Fact(DisplayName = "hard 設定の終盤はスループットが最も高い相手リソースを攻撃する")]
    public void BattlePhase_HardLateGame_AttacksStrongestTP()
    {
        var config = _configs["SHE-hard"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 8, phase: Phase.Battle);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "atk1");
        state.Player1Field.Frontend[1] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "atk2");
        state.Player1Field.Backend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "atk3");

        state.Player2Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "low_tp", rank: Rank.Small, maxAV: 2000);
        state.Player2Field.Frontend[1] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "high_tp", rank: Rank.Medium, maxAV: 400);

        var clientState = BuildClientState(state, 1);

        var actions = ai.DecideBattlePhaseActions(clientState);

        var attack = actions.FirstOrDefault(a => a.ActionType == ActionTypes.Attack);
        attack.Should().NotBeNull();
        ((AttackRequest)attack!.Data).TargetInstanceID.Should().Be("high_tp");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Tenki hard: 条件付きデプロイ優先度
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "TK-0010 がフィールドにあるとき、TK-0005 を先にデプロイする")]
    public void TenkiHard_TK0010OnField_TK0005DeployedFirst()
    {
        var config = _configs["Tenki-hard"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        state.Player1Field.Backend[0] = TestFactory.MakeResource(
            cardId: "TK-0010", instanceId: "cosmo_1",
            maxYield: 300, maxTP: null);
        state.Player1Hand =
        [
            new() { InstanceID = "h_tk1", CardID = "TK-0001" },
            new() { InstanceID = "h_tk5", CardID = "TK-0005" },
        ];
        state.Player1Budget = 5000;

        var clientState = BuildClientState(state, 1);

        var actions = ai.DecideMainPhaseActions(clientState);

        var deploys = actions
            .Where(a => a.ActionType == ActionTypes.PlayCard)
            .ToList();

        deploys.Should().HaveCountGreaterThanOrEqualTo(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_tk5");
    }

    // ═══════════════════════════════════════════════════════════════
    //  手札調整: 維持コスト最小から捨てる
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "手札調整では維持コストが最も小さいカードを捨てる")]
    public void Discard_RemovesCheapestMaintenanceCards()
    {
        var config = _configs["SHE-easy"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(phase: Phase.End);
        var hand = new List<UndeployedCard>();
        for (int i = 0; i < BattleConstants.HandLimit; i++)
        {
            hand.Add(new UndeployedCard { InstanceID = $"card_{i}", CardID = "SH-0001" });
        }
        hand.Add(new UndeployedCard { InstanceID = "extra", CardID = "SH-0001" });
        state.Player1Hand = hand;

        var clientState = BuildClientState(state, 1);

        var discards = ai.DecideDiscard(clientState, 1);

        discards.Should().HaveCount(1);
        hand.Select(h => h.InstanceID).Should().Contain(discards[0]);
    }

    // ═══════════════════════════════════════════════════════════════
    //  メインフェーズは常に EndPhase で終わる
    // ═══════════════════════════════════════════════════════════════

    [Theory(DisplayName = "メインフェーズのアクションはエンドフェーズで終わる")]
    [InlineData("SHE-easy")]
    [InlineData("Tenki-hard")]
    [InlineData("Sugar-easy")]
    [InlineData("Tuners-hard")]
    public void MainPhase_AlwaysEndsWithEndPhase(string model)
    {
        var config = _configs[model];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Budget = 5000;

        var clientState = BuildClientState(state, 1);

        var actions = ai.DecideMainPhaseActions(clientState);

        actions.Should().NotBeEmpty();
        actions.Last().ActionType.Should().Be(ActionTypes.EndPhase);
    }

    [Theory(DisplayName = "バトルフェーズのアクションはエンドフェーズで終わる")]
    [InlineData("SHE-easy")]
    [InlineData("Tenki-hard")]
    public void BattlePhase_AlwaysEndsWithEndPhase(string model)
    {
        var config = _configs[model];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
        state.Player1Budget = 5000;

        var clientState = BuildClientState(state, 1);

        var actions = ai.DecideBattlePhaseActions(clientState);

        actions.Should().NotBeEmpty();
        actions.Last().ActionType.Should().Be(ActionTypes.EndPhase);
    }

    // ═══════════════════════════════════════════════════════════════
    //  収益化: reserve ratio
    // ═══════════════════════════════════════════════════════════════

    [Fact(DisplayName = "hard 設定は収益化でインサイトプールの一部を残す")]
    public void MainPhase_HardConfig_ReservesInsightPool()
    {
        var config = _configs["SHE-hard"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        state.Player1InsightPool = 1000;
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "fe_1", maxTP: 600);
        state.Player1Budget = 5000;

        var clientState = BuildClientState(state, 1);

        var actions = ai.DecideMainPhaseActions(clientState);

        var monetize = actions.FirstOrDefault(a => a.ActionType == ActionTypes.Monetize);
        if (monetize is not null)
        {
            var dists = ((MonetizeRequest)monetize.Data).Distributions;
            var totalDistributed = dists.Sum(d => d.Amount);
            totalDistributed.Should().BeLessThanOrEqualTo(800);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  ヘルパー
    // ═══════════════════════════════════════════════════════════════

    private GD.ClientGameState BuildClientState(BattleGameState state, long playerNum)
    {
        var game = TestFactory.MakeGame();
        return GameStateView.Build(state, game, playerNum, _cc, _effects);
    }
}
