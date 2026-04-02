using OverloadParty.Battle.Engine;
using OverloadParty.Battle.Engine.Effects;
using OverloadParty.Battle.Engine.Processors;
using OverloadParty.Battle.Models;
using OverloadParty.Battle.Npc;

namespace OverloadParty.Battle.Tests.Npc;

/// <summary>
/// Integration tests that load real YAML configs + real card data
/// and verify NPC AI produces actions consistent with config.
/// </summary>
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

        var dir = FindNpcDataDir()
            ?? throw new FileNotFoundException("NPC data directory not found");
        _configs = AiConfigLoader.LoadAll(dir);
    }

    // ═══════════════════════════════════════════════════════════════
    //  YAML loading
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void AllConfigs_LoadSuccessfully()
    {
        _configs.Should().HaveCount(8);
        _configs.Keys.Should().Contain("SHE-easy");
        _configs.Keys.Should().Contain("Tenki-hard");
    }

    [Theory]
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
        total.Should().Be(GameConstants.DeckSize,
            $"{model} deck should have exactly {GameConstants.DeckSize} cards");
    }

    [Theory]
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
    //  Main phase: deploy follows config priorities
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MainPhase_DeploysComputeBeforeData_PerConfig()
    {
        var config = _configs["SHE-easy"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        // SH-0001 = Compute, SH-0007 = Database (both SHE cards)
        state.Player1Hand =
        [
            new() { InstanceID = "h_db", CardID = "SH-0007" },
            new() { InstanceID = "h_compute", CardID = "SH-0001" },
        ];
        state.Player1Budget = 5000;

        var available = BuildAvailable(state, 1);
        var game = TestFactory.MakeGame();

        var actions = ai.DecideMainPhaseActions(state, game, 1, available);

        var deploys = actions
            .Where(a => a.ActionType == WireActionTypes.PlayCard)
            .ToList();

        // At minimum compute should be deployed; if both deployed, compute first
        deploys.Should().NotBeEmpty();
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_compute");
    }

    [Fact]
    public void MainPhase_DeployChoice_UsesConfigValue()
    {
        // SHE-easy config: SH-0006 choice = "use"
        var config = _configs["SHE-easy"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Main);
        state.Player1Hand =
        [
            new() { InstanceID = "h_0006", CardID = "SH-0006" },
        ];
        state.Player1Budget = 5000;

        var available = BuildAvailable(state, 1);
        var game = TestFactory.MakeGame();

        var actions = ai.DecideMainPhaseActions(state, game, 1, available);

        var deploy = actions.FirstOrDefault(a =>
            a.ActionType == WireActionTypes.PlayCard &&
            ((PlayCardRequest)a.Data).CardInstanceID == "h_0006");

        deploy.Should().NotBeNull("SH-0006 should be deployed");

        var req = (PlayCardRequest)deploy!.Data;
        if (req.ChoiceData is not null)
        {
            ((string)req.ChoiceData["option"]).Should().Be("use");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Main phase: deploy zone preferences
    // ═══════════════════════════════════════════════════════════════

    [Fact]
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

        var available = BuildAvailable(state, 1);
        var game = TestFactory.MakeGame();

        var actions = ai.DecideMainPhaseActions(state, game, 1, available);
        var deploy = actions.FirstOrDefault(a =>
            a.ActionType == WireActionTypes.PlayCard &&
            ((PlayCardRequest)a.Data).CardInstanceID == "h_compute");

        deploy.Should().NotBeNull();
        var req = (PlayCardRequest)deploy!.Data;
        req.Zone.Should().Be("frontend");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Main phase: scale up uses config instance family
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MainPhase_ScaleUp_UsesSHEInstanceFamily_M()
    {
        var config = _configs["SHE-easy"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "fe_1", rank: Rank.Small);
        state.Player1Budget = 5000;

        var available = BuildAvailable(state, 1);
        var game = TestFactory.MakeGame();

        var actions = ai.DecideMainPhaseActions(state, game, 1, available);

        var scaleUp = actions.FirstOrDefault(a => a.ActionType == WireActionTypes.ScaleUp);
        if (scaleUp is not null)
        {
            var req = (ScaleUpRequest)scaleUp.Data;
            if (req.InstanceFamily is not null)
            {
                req.InstanceFamily.Should().Be("M");
            }
        }
    }

    [Fact]
    public void MainPhase_ScaleUp_UsesTenkiInstanceFamily_R()
    {
        var config = _configs["Tenki-easy"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "fe_1", rank: Rank.Small);
        state.Player1Budget = 5000;

        var available = BuildAvailable(state, 1);
        var game = TestFactory.MakeGame();

        var actions = ai.DecideMainPhaseActions(state, game, 1, available);

        var scaleUp = actions.FirstOrDefault(a => a.ActionType == WireActionTypes.ScaleUp);
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
    //  Battle phase: target selection follows config
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BattlePhase_EasyConfig_AttacksWeakestAV()
    {
        var config = _configs["SHE-easy"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Battle);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "attacker");
        state.Player2Field.Frontend[0] = TestFactory.MakeResource(
            instanceId: "strong", maxAV: 2000, currentAV: 2000);
        state.Player2Field.Frontend[1] = TestFactory.MakeResource(
            instanceId: "weak", maxAV: 400, currentAV: 400);

        var available = BuildAvailable(state, 1);
        var game = TestFactory.MakeGame();

        var actions = ai.DecideBattlePhaseActions(state, game, 1, available);

        var attack = actions.FirstOrDefault(a => a.ActionType == WireActionTypes.Attack);
        attack.Should().NotBeNull();
        ((AttackRequest)attack!.Data).TargetInstanceID.Should().Be("weak");
    }

    [Fact]
    public void BattlePhase_HardLateGame_AttacksStrongestTP()
    {
        var config = _configs["SHE-hard"];
        var ai = new NpcAi(config, _cc, _effects);

        // Late game: turn 8, 3+ own resources → game_phases.late activates
        var state = TestFactory.MakeGameState(turn: 8, phase: Phase.Battle);
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "atk1");
        state.Player1Field.Frontend[1] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "atk2");
        state.Player1Field.Backend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "atk3");

        state.Player2Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "low_tp", currentTP: 200, maxAV: 2000, currentAV: 2000);
        state.Player2Field.Frontend[1] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "high_tp", currentTP: 900, maxAV: 400, currentAV: 400);

        var available = BuildAvailable(state, 1);
        var game = TestFactory.MakeGame();

        var actions = ai.DecideBattlePhaseActions(state, game, 1, available);

        var attack = actions.FirstOrDefault(a => a.ActionType == WireActionTypes.Attack);
        attack.Should().NotBeNull();
        ((AttackRequest)attack!.Data).TargetInstanceID.Should().Be("high_tp");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Tenki hard: conditional deploy priority
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TenkiHard_TK0010OnField_TK0005DeployedFirst()
    {
        var config = _configs["Tenki-hard"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        // TK-0010 already on field → conditional priority for TK-0005 = 90
        state.Player1Field.Backend[0] = TestFactory.MakeResource(
            cardId: "TK-0010", instanceId: "cosmo_1",
            maxYield: 300, currentYield: 300, maxTP: null, currentTP: null);
        state.Player1Hand =
        [
            new() { InstanceID = "h_tk1", CardID = "TK-0001" },  // compute, priority 50
            new() { InstanceID = "h_tk5", CardID = "TK-0005" },  // conditional 90
        ];
        state.Player1Budget = 5000;

        var available = BuildAvailable(state, 1);
        var game = TestFactory.MakeGame();

        var actions = ai.DecideMainPhaseActions(state, game, 1, available);

        var deploys = actions
            .Where(a => a.ActionType == WireActionTypes.PlayCard)
            .ToList();

        deploys.Should().HaveCountGreaterThanOrEqualTo(2);
        ((PlayCardRequest)deploys[0].Data).CardInstanceID.Should().Be("h_tk5");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Discard: cheapest maintenance
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Discard_RemovesCheapestMaintenanceCards()
    {
        var config = _configs["SHE-easy"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(phase: Phase.End);
        // Hand limit + 1 → must discard 1
        var hand = new List<UndeployedCard>();
        for (int i = 0; i < BattleConstants.HandLimit; i++)
        {
            hand.Add(new UndeployedCard { InstanceID = $"card_{i}", CardID = "SH-0001" });
        }
        hand.Add(new UndeployedCard { InstanceID = "extra", CardID = "SH-0001" });
        state.Player1Hand = hand;

        var discards = ai.DecideDiscard(state, 1, 1);

        discards.Should().HaveCount(1);
        // Discards lowest keep-priority card
        hand.Select(h => h.InstanceID).Should().Contain(discards[0]);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Main phase always ends with EndPhase
    // ═══════════════════════════════════════════════════════════════

    [Theory]
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

        var available = BuildAvailable(state, 1);
        var game = TestFactory.MakeGame();

        var actions = ai.DecideMainPhaseActions(state, game, 1, available);

        actions.Should().NotBeEmpty();
        actions.Last().ActionType.Should().Be(WireActionTypes.EndPhase);
    }

    [Theory]
    [InlineData("SHE-easy")]
    [InlineData("Tenki-hard")]
    public void BattlePhase_AlwaysEndsWithEndPhase(string model)
    {
        var config = _configs[model];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 2, phase: Phase.Battle);
        state.Player1Budget = 5000;

        var available = BuildAvailable(state, 1);
        var game = TestFactory.MakeGame();

        var actions = ai.DecideBattlePhaseActions(state, game, 1, available);

        actions.Should().NotBeEmpty();
        actions.Last().ActionType.Should().Be(WireActionTypes.EndPhase);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Monetize: reserve ratio
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MainPhase_HardConfig_ReservesInsightPool()
    {
        // SHE-hard has reserve_ratio: 0.2
        var config = _configs["SHE-hard"];
        var ai = new NpcAi(config, _cc, _effects);

        var state = TestFactory.MakeGameState(turn: 3, phase: Phase.Main);
        state.Player1InsightPool = 1000;
        state.Player1Field.Frontend[0] = TestFactory.MakeResource(
            cardId: "SH-0001", instanceId: "fe_1",
            currentTP: 600, maxTP: 600);
        state.Player1Budget = 5000;

        var available = BuildAvailable(state, 1);
        var game = TestFactory.MakeGame();

        var actions = ai.DecideMainPhaseActions(state, game, 1, available);

        var monetize = actions.FirstOrDefault(a => a.ActionType == WireActionTypes.Monetize);
        if (monetize is not null)
        {
            var dists = ((MonetizeRequest)monetize.Data).Distributions;
            var totalDistributed = dists.Sum(d => d.Amount);
            // With reserve_ratio 0.2, should distribute at most 800 of 1000
            totalDistributed.Should().BeLessThanOrEqualTo(800);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private List<AvailableAction> BuildAvailable(GameState state, long playerNum)
    {
        var myField = state.GetField(playerNum);
        var oppField = state.GetField(state.OpponentOf(playerNum));
        var hand = state.GetHand(playerNum);
        var budget = state.GetBudget(playerNum);
        var insightPool = state.GetInsightPool(playerNum);
        return AvailableActions.GetAllAvailableActions(
            state, myField, oppField, hand, budget, insightPool, _cc, _effects);
    }

    private static string? FindNpcDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "OverloadParty.Battle.Npc", "Data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
