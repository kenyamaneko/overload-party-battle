package npc_test

import (
	"context"
	"encoding/json"
	"fmt"
	"math/rand"
	"sort"
	"strings"
	"testing"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-common/model"
	"github.com/kenyamaneko/overload-party-battle/internal/npc"
	"github.com/kenyamaneko/overload-party-battle/internal/repository"
)

type matchResult struct {
	f1, f2   string
	ai1, ai2 string // AI type names
	winner   string
	reason   string
	turns    int64
	p1Budget int64
	p2Budget int64
}

// TestNPCvsNPC_AllMatchups runs all faction matchups using StandardAI.
func TestNPCvsNPC_AllMatchups(t *testing.T) {
	cc, reg := loadTestDeps(t)
	factions := []string{"SD", "Tenki", "Sugar", "Tuners"}

	var results []matchResult
	for _, f1 := range factions {
		for _, f2 := range factions {
			if f1 == f2 {
				continue
			}
			ai1 := npc.NewStandardAI(cc, reg)
			ai2 := npc.NewStandardAI(cc, reg)
			r := runMatchWithStrategies(t, cc, reg, f1, f2, ai1, ai2, "Standard", "Standard")
			results = append(results, r)
		}
	}

	printResults(t, "StandardAI vs StandardAI", results)
}

// TestNPCvsNPC_FactionStrategies runs all faction matchups using faction-specific AIs.
func TestNPCvsNPC_FactionStrategies(t *testing.T) {
	cc, reg := loadTestDeps(t)
	factions := []string{"SD", "Tenki", "Sugar", "Tuners"}

	var results []matchResult
	for _, f1 := range factions {
		for _, f2 := range factions {
			if f1 == f2 {
				continue
			}
			ai1 := npc.GetFactionAI(f1, cc, reg)
			ai2 := npc.GetFactionAI(f2, cc, reg)
			r := runMatchWithStrategies(t, cc, reg, f1, f2, ai1, ai2, f1+"AI", f2+"AI")
			results = append(results, r)
		}
	}

	printResults(t, "FactionAI vs FactionAI", results)
	printWinRates(t, factions, results)
}

// TestNPCvsNPC_FactionVsStandard compares faction AIs against StandardAI.
func TestNPCvsNPC_FactionVsStandard(t *testing.T) {
	cc, reg := loadTestDeps(t)
	factions := []string{"SD", "Tenki", "Sugar", "Tuners"}

	var results []matchResult
	for _, f := range factions {
		// Faction AI as P1 vs Standard as P2
		ai1 := npc.GetFactionAI(f, cc, reg)
		ai2 := npc.NewStandardAI(cc, reg)
		r := runMatchWithStrategies(t, cc, reg, f, f, ai1, ai2, f+"AI", "Standard")
		results = append(results, r)

		// Standard as P1 vs Faction AI as P2
		ai1std := npc.NewStandardAI(cc, reg)
		ai2fac := npc.GetFactionAI(f, cc, reg)
		r2 := runMatchWithStrategies(t, cc, reg, f, f, ai1std, ai2fac, "Standard", f+"AI")
		results = append(results, r2)
	}

	printResults(t, "FactionAI vs StandardAI (mirror matchups)", results)
}

// TestNPCvsNPC_RandomTournament runs a large number of randomized matchups
// with all combinations of faction decks × AI models, and aggregates statistics.
func TestNPCvsNPC_RandomTournament(t *testing.T) {
	cc, reg := loadTestDeps(t)
	factions := []string{"SD", "Tenki", "Sugar", "Tuners"}
	aiNames := []string{"Standard", "SDAI", "TenkiAI", "SugarAI", "TunersAI"}
	totalGames := 200

	rng := rand.New(rand.NewSource(42))

	makeAI := func(name string) npc.Strategy {
		switch name {
		case "SDAI":
			return npc.NewSDAI(cc, reg)
		case "TenkiAI":
			return npc.NewTenkiAI(cc, reg)
		case "SugarAI":
			return npc.NewSugarAI(cc, reg)
		case "TunersAI":
			return npc.NewTunersAI(cc, reg)
		default:
			return npc.NewStandardAI(cc, reg)
		}
	}

	type stats struct {
		wins, losses int
		totalTurns   int64
	}

	factionStats := make(map[string]*stats)
	aiStats := make(map[string]*stats)
	comboStats := make(map[string]*stats) // "SD+SDAI"
	matchupStats := make(map[string]*stats)
	for _, f := range factions {
		factionStats[f] = &stats{}
		for _, f2 := range factions {
			if f != f2 {
				matchupStats[f+" vs "+f2] = &stats{}
			}
		}
		for _, a := range aiNames {
			comboStats[f+"+"+a] = &stats{}
		}
	}
	for _, a := range aiNames {
		aiStats[a] = &stats{}
	}

	var allResults []matchResult

	for i := 0; i < totalGames; i++ {
		// Random faction pair (no mirror)
		idx1 := rng.Intn(len(factions))
		idx2 := rng.Intn(len(factions) - 1)
		if idx2 >= idx1 {
			idx2++
		}
		f1, f2 := factions[idx1], factions[idx2]

		// Random AI model for each player (any AI can play any faction)
		a1 := aiNames[rng.Intn(len(aiNames))]
		a2 := aiNames[rng.Intn(len(aiNames))]

		ai1 := makeAI(a1)
		ai2 := makeAI(a2)

		r := runMatchSilent(t, cc, reg, f1, f2, ai1, ai2, a1, a2)
		allResults = append(allResults, r)

		// Aggregate stats
		combo1 := f1 + "+" + a1
		combo2 := f2 + "+" + a2

		if r.winner == f1 {
			factionStats[f1].wins++
			factionStats[f2].losses++
			aiStats[a1].wins++
			aiStats[a2].losses++
			comboStats[combo1].wins++
			comboStats[combo2].losses++
			matchupStats[f1+" vs "+f2].wins++
		} else if r.winner == f2 {
			factionStats[f2].wins++
			factionStats[f1].losses++
			aiStats[a2].wins++
			aiStats[a1].losses++
			comboStats[combo2].wins++
			comboStats[combo1].losses++
			matchupStats[f1+" vs "+f2].losses++
		}
		factionStats[f1].totalTurns += r.turns
		factionStats[f2].totalTurns += r.turns
		aiStats[a1].totalTurns += r.turns
		aiStats[a2].totalTurns += r.turns
		comboStats[combo1].totalTurns += r.turns
		comboStats[combo2].totalTurns += r.turns
	}

	// --- Print results ---

	t.Logf("")
	t.Logf("================================================================")
	t.Logf("  Random Tournament: %d games (seed=42)", totalGames)
	t.Logf("  %d factions × %d AI models = %d possible combos",
		len(factions), len(aiNames), len(factions)*len(aiNames))
	t.Logf("================================================================")

	// Faction win rates
	t.Logf("")
	t.Logf("=== Faction Win Rates (deck) ===")
	t.Logf("%-10s | %4s | %4s | %5s | %8s", "Faction", "Win", "Loss", "Rate", "AvgTurns")
	t.Logf("-----------+------+------+-------+---------")
	for _, f := range factions {
		s := factionStats[f]
		total := s.wins + s.losses
		rate, avgT := 0.0, 0.0
		if total > 0 {
			rate = float64(s.wins) / float64(total) * 100
			avgT = float64(s.totalTurns) / float64(total)
		}
		t.Logf("%-10s | %4d | %4d | %4.0f%% | %7.1f", f, s.wins, s.losses, rate, avgT)
	}

	// AI model win rates
	t.Logf("")
	t.Logf("=== AI Model Win Rates ===")
	t.Logf("%-12s | %4s | %4s | %5s", "AI Model", "Win", "Loss", "Rate")
	t.Logf("-------------+------+------+------")
	for _, a := range aiNames {
		s := aiStats[a]
		total := s.wins + s.losses
		rate := 0.0
		if total > 0 {
			rate = float64(s.wins) / float64(total) * 100
		}
		t.Logf("%-12s | %4d | %4d | %4.0f%%", a, s.wins, s.losses, rate)
	}

	// Best combos (faction + AI)
	t.Logf("")
	t.Logf("=== Faction × AI Model Combos (sorted by win rate) ===")
	t.Logf("%-20s | %4s | %4s | %5s", "Combo", "Win", "Loss", "Rate")
	t.Logf("---------------------+------+------+------")

	type comboEntry struct {
		name string
		s    *stats
		rate float64
	}
	var entries []comboEntry
	for name, s := range comboStats {
		total := s.wins + s.losses
		if total == 0 {
			continue
		}
		entries = append(entries, comboEntry{name, s, float64(s.wins) / float64(total) * 100})
	}
	sort.Slice(entries, func(i, j int) bool {
		return entries[i].rate > entries[j].rate
	})
	for _, e := range entries {
		total := e.s.wins + e.s.losses
		if total >= 2 { // only show combos with enough games
			t.Logf("%-20s | %4d | %4d | %4.0f%%", e.name, e.s.wins, e.s.losses, e.rate)
		}
	}

	// Matchup matrix
	t.Logf("")
	t.Logf("=== Matchup Matrix (row=P1 deck, col=P2 deck, value=P1 wins/total) ===")
	t.Logf("%-10s | %8s | %8s | %8s | %8s", "", "SD", "Tenki", "Sugar", "Tuners")
	t.Logf("-----------+----------+----------+----------+---------")
	for _, f1 := range factions {
		parts := make([]string, len(factions))
		for i, f2 := range factions {
			if f1 == f2 {
				parts[i] = fmt.Sprintf("%8s", "-")
			} else {
				s := matchupStats[f1+" vs "+f2]
				total := s.wins + s.losses
				if total > 0 {
					parts[i] = fmt.Sprintf("%3d/%-3d", s.wins, total)
				} else {
					parts[i] = fmt.Sprintf("%8s", "0/0")
				}
			}
		}
		t.Logf("%-10s | %8s | %8s | %8s | %8s", f1, parts[0], parts[1], parts[2], parts[3])
	}

	// Win reason distribution
	t.Logf("")
	t.Logf("=== Win Reasons ===")
	reasonCounts := make(map[string]int)
	for _, r := range allResults {
		reasonCounts[r.reason]++
	}
	reasons := make([]string, 0, len(reasonCounts))
	for r := range reasonCounts {
		reasons = append(reasons, r)
	}
	sort.Strings(reasons)
	for _, r := range reasons {
		t.Logf("  %-20s: %3d (%2.0f%%)", r, reasonCounts[r], float64(reasonCounts[r])/float64(totalGames)*100)
	}

	// Turn limit details
	if reasonCounts["turn_limit"] > 0 {
		t.Logf("")
		t.Logf("=== Turn Limit Details ===")

		// By matchup
		turnLimitByFaction := make(map[string]int) // "SD vs Tenki"
		for _, r := range allResults {
			if r.reason == "turn_limit" {
				key := r.f1 + " vs " + r.f2
				turnLimitByFaction[key]++
			}
		}
		matchups := make([]string, 0, len(turnLimitByFaction))
		for k := range turnLimitByFaction {
			matchups = append(matchups, k)
		}
		sort.Slice(matchups, func(i, j int) bool {
			if turnLimitByFaction[matchups[i]] != turnLimitByFaction[matchups[j]] {
				return turnLimitByFaction[matchups[i]] > turnLimitByFaction[matchups[j]]
			}
			return matchups[i] < matchups[j]
		})
		t.Logf("Matchup              | Count")
		t.Logf("---------------------+------")
		for _, m := range matchups {
			t.Logf("%-20s | %5d", m, turnLimitByFaction[m])
		}

		// By faction (count how many times each faction was involved)
		t.Logf("")
		t.Logf("Faction Involvement in Turn Limit Games:")
		factionInvolvement := make(map[string]int)
		for _, r := range allResults {
			if r.reason == "turn_limit" {
				factionInvolvement[r.f1]++
				factionInvolvement[r.f2]++
			}
		}
		factionList := make([]string, 0, len(factionInvolvement))
		for f := range factionInvolvement {
			factionList = append(factionList, f)
		}
		sort.Slice(factionList, func(i, j int) bool {
			return factionInvolvement[factionList[i]] > factionInvolvement[factionList[j]]
		})
		for _, f := range factionList {
			t.Logf("  %-10s: %3d times", f, factionInvolvement[f])
		}
	}

	// Turn distribution
	turnBuckets := map[string]int{"1-3": 0, "4-6": 0, "7-10": 0, "11+": 0}
	totalTurns := int64(0)
	for _, r := range allResults {
		totalTurns += r.turns
		switch {
		case r.turns <= 3:
			turnBuckets["1-3"]++
		case r.turns <= 6:
			turnBuckets["4-6"]++
		case r.turns <= 10:
			turnBuckets["7-10"]++
		default:
			turnBuckets["11+"]++
		}
	}
	t.Logf("")
	t.Logf("=== Turn Distribution ===")
	for _, bucket := range []string{"1-3", "4-6", "7-10", "11+"} {
		t.Logf("  %-6s turns: %3d (%2.0f%%)", bucket, turnBuckets[bucket], float64(turnBuckets[bucket])/float64(totalGames)*100)
	}
	t.Logf("  Average: %.1f turns", float64(totalTurns)/float64(totalGames))
}

// runMatchSilent is like runMatchWithStrategies but with minimal logging (no per-turn logs).
func runMatchSilent(t *testing.T, cc *cache.CardCache, reg *effect.EffectRegistry, faction1, faction2 string, ai1, ai2 npc.Strategy, ai1Name, ai2Name string) matchResult {
	t.Helper()
	ctx := context.Background()

	gameRepo := repository.NewMockGameRepository()
	gameEngine := engine.NewGameEngine(gameRepo, cc)
	gameEngine.SetEffectRegistry(reg)

	deck1 := npc.GetNPCDeck(faction1)
	deck2 := npc.GetNPCDeck(faction2)

	npc1ID := "npc-player-1"
	npc2ID := "npc-player-2"

	snap1 := model.DeckSnapshot{DeckID: "npc-" + faction1, Cards: deck1.Cards}
	snap2 := model.DeckSnapshot{DeckID: "npc-" + faction2, Cards: deck2.Cards}
	gameID, err := gameEngine.CreateNewGame(ctx, npc1ID, npc2ID, snap1, snap2, 1)
	if err != nil {
		t.Fatalf("CreateNewGame failed: %v", err)
	}

	// Game is now created fully initialized (status=playing, phase=draw, hands dealt).
	// Proceed directly to auto-advance.
	gameOver, winReason, err := gameEngine.RunAutoAdvance(ctx, gameID)
	if err != nil {
		t.Fatalf("initial auto-advance failed: %v", err)
	}
	if gameOver {
		t.Fatalf("game ended immediately: %s", winReason)
	}

	const maxIterations = 500

	for i := 0; i < maxIterations; i++ {
		game, err := gameRepo.GetGame(ctx, gameID)
		if err != nil {
			t.Fatalf("get game: %v", err)
		}
		if game.Status == model.GameStatusFinished {
			state, _ := gameRepo.GetGameState(ctx, gameID)
			winnerFaction := faction1
			if game.WinnerID != nil && *game.WinnerID == npc2ID {
				winnerFaction = faction2
			}
			return matchResult{
				f1: faction1, f2: faction2, ai1: ai1Name, ai2: ai2Name,
				winner: winnerFaction, reason: "finished",
				turns: state.CurrentTurn, p1Budget: state.GetBudget(1), p2Budget: state.GetBudget(2),
			}
		}

		state, err := gameRepo.GetGameState(ctx, gameID)
		if err != nil {
			t.Fatalf("get state: %v", err)
		}

		activeNum := state.ActivePlayer
		var activeAI npc.Strategy = ai1
		activeID := npc1ID
		if activeNum == 2 {
			activeAI = ai2
			activeID = npc2ID
		}

		phase := state.CurrentPhase
		available := computeAvailableForTest(state, game, activeNum, cc, reg)
		var actions []npc.NPCAction
		switch phase {
		case model.PhaseMain:
			actions = activeAI.DecideMainPhaseActions(state, game, activeNum, available)
		case model.PhaseBattle:
			actions = activeAI.DecideBattlePhaseActions(state, game, activeNum, available)
		case model.PhaseEnd:
			ids := activeAI.DecideDiscard(state, activeNum)
			if len(ids) > 0 {
				data, _ := json.Marshal(map[string]interface{}{"cardInstanceIds": ids})
				actions = []npc.NPCAction{{ActionType: "discard_hand", Data: data}}
			}
		default:
			t.Fatalf("unexpected phase: %s", phase)
		}

		for _, action := range actions {
			result, err := gameEngine.ProcessAction(ctx, gameID, activeID, action.ActionType, action.Data)
			if err != nil {
				continue
			}
			if result.GameOver {
				game, _ := gameRepo.GetGame(ctx, gameID)
				winnerFaction := faction1
				if game.WinnerID != nil && *game.WinnerID == npc2ID {
					winnerFaction = faction2
				}
				return matchResult{
					f1: faction1, f2: faction2, ai1: ai1Name, ai2: ai2Name,
					winner: winnerFaction, reason: result.WinReason,
					turns: state.CurrentTurn, p1Budget: state.GetBudget(1), p2Budget: state.GetBudget(2),
				}
			}
		}

		gameOver, winReason, err = gameEngine.RunAutoAdvance(ctx, gameID)
		if err != nil {
			t.Fatalf("auto-advance failed: %v", err)
		}
		if gameOver {
			game, _ := gameRepo.GetGame(ctx, gameID)
			winnerFaction := faction1
			if game.WinnerID != nil && *game.WinnerID == npc2ID {
				winnerFaction = faction2
			}
			return matchResult{
				f1: faction1, f2: faction2, ai1: ai1Name, ai2: ai2Name,
				winner: winnerFaction, reason: winReason,
				turns: state.CurrentTurn, p1Budget: state.GetBudget(1), p2Budget: state.GetBudget(2),
			}
		}
	}

	state, _ := gameRepo.GetGameState(ctx, gameID)
	return matchResult{
		f1: faction1, f2: faction2, ai1: ai1Name, ai2: ai2Name,
		winner: "TIMEOUT", reason: "max_iterations",
		turns: state.CurrentTurn, p1Budget: state.GetBudget(1), p2Budget: state.GetBudget(2),
	}
}

// computeAvailableForTest computes available actions for simulation tests.
func computeAvailableForTest(state *model.GameState, game *model.Game, playerNum int64, cc *cache.CardCache, reg *effect.EffectRegistry) []engine.AvailableAction {
	myField, _ := state.GetField(playerNum)
	oppField, _ := state.GetField(model.OpponentNum(playerNum))
	hand, _ := state.GetHand(playerNum)
	budget := state.GetBudget(playerNum)
	insightPool := state.GetInsightPool(playerNum)
	return engine.ComputeAvailableActions(state, game, playerNum, myField, oppField, hand, budget, insightPool, cc, reg)
}

func loadTestDeps(t *testing.T) (*cache.CardCache, *effect.EffectRegistry) {
	t.Helper()
	cc := cache.NewCardCache()
	if err := cc.LoadFromJSON("../cache/cards_gen.json"); err != nil {
		t.Fatalf("failed to load cards: %v", err)
	}
	reg := effect.NewEffectRegistry()
	effect.RegisterAllEffects(reg)
	return cc, reg
}

func runMatchWithStrategies(t *testing.T, cc *cache.CardCache, reg *effect.EffectRegistry, faction1, faction2 string, ai1, ai2 npc.Strategy, ai1Name, ai2Name string) matchResult {
	t.Helper()
	ctx := context.Background()

	gameRepo := repository.NewMockGameRepository()
	gameEngine := engine.NewGameEngine(gameRepo, cc)
	gameEngine.SetEffectRegistry(reg)

	deck1 := npc.GetNPCDeck(faction1)
	deck2 := npc.GetNPCDeck(faction2)

	npc1ID := "npc-player-1"
	npc2ID := "npc-player-2"

	snap1 := model.DeckSnapshot{DeckID: "npc-" + faction1, Cards: deck1.Cards}
	snap2 := model.DeckSnapshot{DeckID: "npc-" + faction2, Cards: deck2.Cards}
	gameID, err := gameEngine.CreateNewGame(ctx, npc1ID, npc2ID, snap1, snap2, 1)
	if err != nil {
		t.Fatalf("CreateNewGame failed: %v", err)
	}

	t.Logf("")
	t.Logf("========== %s[%s] (P1) vs %s[%s] (P2) ==========", faction1, ai1Name, faction2, ai2Name)

	// Game is now created fully initialized (status=playing, phase=draw, hands dealt).
	// Proceed directly to auto-advance.
	gameOver, winReason, err := gameEngine.RunAutoAdvance(ctx, gameID)
	if err != nil {
		t.Fatalf("initial auto-advance failed: %v", err)
	}
	if gameOver {
		t.Fatalf("game ended immediately: %s", winReason)
	}

	// Main game loop
	const maxIterations = 500
	errorCount := 0

	for i := 0; i < maxIterations; i++ {
		game, err := gameRepo.GetGame(ctx, gameID)
		if err != nil {
			t.Fatalf("get game: %v", err)
		}
		if game.Status == model.GameStatusFinished {
			state, _ := gameRepo.GetGameState(ctx, gameID)
			winnerFaction := faction1
			if game.WinnerID != nil && *game.WinnerID == npc2ID {
				winnerFaction = faction2
			}
			t.Logf(">>> WINNER: %s (Turn %d)", winnerFaction, state.CurrentTurn)
			return matchResult{
				f1: faction1, f2: faction2,
				ai1: ai1Name, ai2: ai2Name,
				winner: winnerFaction, reason: "finished",
				turns:    state.CurrentTurn,
				p1Budget: state.GetBudget(1), p2Budget: state.GetBudget(2),
			}
		}

		state, err := gameRepo.GetGameState(ctx, gameID)
		if err != nil {
			t.Fatalf("get state: %v", err)
		}

		activeNum := state.ActivePlayer
		activeFaction := faction1
		var activeAI npc.Strategy = ai1
		activeID := npc1ID
		activeAIName := ai1Name
		if activeNum == 2 {
			activeFaction = faction2
			activeAI = ai2
			activeID = npc2ID
			activeAIName = ai2Name
		}
		_ = activeAIName

		phase := state.CurrentPhase

		available := computeAvailableForTest(state, game, activeNum, cc, reg)

		var actions []npc.NPCAction
		switch phase {
		case model.PhaseMain:
			actions = activeAI.DecideMainPhaseActions(state, game, activeNum, available)
			logTurnSummary(t, state, game, cc, activeFaction, activeNum, actions)
		case model.PhaseBattle:
			actions = activeAI.DecideBattlePhaseActions(state, game, activeNum, available)
			logBattleSummary(t, activeFaction, actions)
		case model.PhaseEnd:
			ids := activeAI.DecideDiscard(state, activeNum)
			if len(ids) > 0 {
				data, _ := json.Marshal(map[string]interface{}{
					"cardInstanceIds": ids,
				})
				actions = []npc.NPCAction{{ActionType: "discard_hand", Data: data}}
				t.Logf("  [%s] Discarding %d cards", activeFaction, len(ids))
			} else {
				t.Logf("  WARN: %s in end phase but no discard needed", activeFaction)
				break
			}
		default:
			t.Fatalf("unexpected phase: %s (turn=%d, active=%d)", phase, state.CurrentTurn, activeNum)
		}

		// Execute actions
		for _, action := range actions {
			result, err := gameEngine.ProcessAction(ctx, gameID, activeID, action.ActionType, action.Data)
			if err != nil {
				errorCount++
				if errorCount <= 5 {
					t.Logf("  [%s] WARN: %s failed: %v", activeFaction, action.ActionType, err)
				}
				continue
			}
			if result.GameOver {
				game, _ := gameRepo.GetGame(ctx, gameID)
				winnerFaction := faction1
				if game.WinnerID != nil && *game.WinnerID == npc2ID {
					winnerFaction = faction2
				}
				t.Logf(">>> WINNER: %s (reason=%s, Turn %d)", winnerFaction, result.WinReason, state.CurrentTurn)
				logFinalState(t, state, game, cc, faction1, faction2)
				return matchResult{
					f1: faction1, f2: faction2,
					ai1: ai1Name, ai2: ai2Name,
					winner: winnerFaction, reason: result.WinReason,
					turns:    state.CurrentTurn,
					p1Budget: state.GetBudget(1), p2Budget: state.GetBudget(2),
				}
			}
		}

		// Auto-advance
		gameOver, winReason, err = gameEngine.RunAutoAdvance(ctx, gameID)
		if err != nil {
			t.Fatalf("auto-advance failed: %v", err)
		}
		if gameOver {
			game, _ := gameRepo.GetGame(ctx, gameID)
			winnerFaction := faction1
			if game.WinnerID != nil && *game.WinnerID == npc2ID {
				winnerFaction = faction2
			}
			t.Logf(">>> WINNER: %s (reason=%s, Turn %d)", winnerFaction, winReason, state.CurrentTurn)
			logFinalState(t, state, game, cc, faction1, faction2)
			return matchResult{
				f1: faction1, f2: faction2,
				ai1: ai1Name, ai2: ai2Name,
				winner: winnerFaction, reason: winReason,
				turns:    state.CurrentTurn,
				p1Budget: state.GetBudget(1), p2Budget: state.GetBudget(2),
			}
		}
	}

	state, _ := gameRepo.GetGameState(ctx, gameID)
	t.Logf("TIMEOUT: game did not finish within %d iterations (turn=%d)", maxIterations, state.CurrentTurn)
	return matchResult{
		f1: faction1, f2: faction2,
		ai1: ai1Name, ai2: ai2Name,
		winner: "TIMEOUT", reason: "max_iterations",
		turns:    state.CurrentTurn,
		p1Budget: state.GetBudget(1), p2Budget: state.GetBudget(2),
	}
}

// --- Output helpers ---

func printResults(t *testing.T, title string, results []matchResult) {
	t.Helper()
	t.Logf("")
	t.Logf("============================================")
	t.Logf("  %s", title)
	t.Logf("============================================")
	t.Logf("%-12s vs %-12s | %-10s vs %-10s | Winner     | Reason         | Turn | P1Budget | P2Budget",
		"P1", "P2", "AI1", "AI2")
	t.Logf("----------------------------+------------------------+------------+----------------+------+----------+---------")
	for _, r := range results {
		t.Logf("%-12s vs %-12s | %-10s vs %-10s | %-10s | %-14s | %4d | %8d | %8d",
			r.f1, r.f2, r.ai1, r.ai2, r.winner, r.reason, r.turns, r.p1Budget, r.p2Budget)
	}
}

func printWinRates(t *testing.T, factions []string, results []matchResult) {
	t.Helper()
	t.Logf("")
	t.Logf("=== Win Rates ===")
	for _, f := range factions {
		wins, total := 0, 0
		for _, r := range results {
			if r.f1 == f || r.f2 == f {
				total++
				if r.winner == f {
					wins++
				}
			}
		}
		t.Logf("  %-10s: %d/%d (%.0f%%)", f, wins, total, float64(wins)/float64(total)*100)
	}

	// Average turns
	totalTurns := int64(0)
	for _, r := range results {
		totalTurns += r.turns
	}
	t.Logf("  Avg Turns: %.1f", float64(totalTurns)/float64(len(results)))
}

// --- Logging helpers ---

func logTurnSummary(t *testing.T, state *model.GameState, game *model.Game, cc *cache.CardCache, faction string, playerNum int64, actions []npc.NPCAction) {
	t.Helper()
	budget := state.GetBudget(playerNum)
	insightPool := state.GetInsightPool(playerNum)
	hand, _ := state.GetHand(playerNum)
	field, _ := state.GetField(playerNum)
	repo, _ := state.GetRepository(playerNum)

	t.Logf("--- Turn %d | %s (P%d) | Budget=%d | Insight=%d | Hand=%d | Repo=%d ---",
		state.CurrentTurn, faction, playerNum, budget, insightPool, len(hand), len(repo))

	// Field state
	var frontNames, backNames []string
	for _, r := range field.Frontend {
		if r != nil {
			card := cc.Get(r.CardID)
			name := fmt.Sprintf("#%d", r.CardID)
			if card != nil {
				name = card.CardName
			}
			av := model.CalculateEffectiveAV(r)
			frontNames = append(frontNames, fmt.Sprintf("%s[AV=%d/%d,%s]", name, av, r.MaxAV, r.Rank))
		}
	}
	for _, r := range field.Backend {
		if r != nil {
			card := cc.Get(r.CardID)
			name := fmt.Sprintf("#%d", r.CardID)
			if card != nil {
				name = card.CardName
			}
			av := model.CalculateEffectiveAV(r)
			backNames = append(backNames, fmt.Sprintf("%s[AV=%d/%d,%s]", name, av, r.MaxAV, r.Rank))
		}
	}

	// Support zone
	var supportNames []string
	for _, s := range field.Support {
		if s != nil {
			card := cc.Get(s.CardID)
			name := fmt.Sprintf("#%d", s.CardID)
			if card != nil {
				name = card.CardName
			}
			supportNames = append(supportNames, name)
		}
	}

	if len(frontNames) > 0 {
		t.Logf("  Front: %s", strings.Join(frontNames, ", "))
	}
	if len(backNames) > 0 {
		t.Logf("  Back:  %s", strings.Join(backNames, ", "))
	}
	if len(supportNames) > 0 {
		t.Logf("  Supp:  %s", strings.Join(supportNames, ", "))
	}

	// Count actions by type
	actionCounts := make(map[string]int)
	for _, a := range actions {
		actionCounts[a.ActionType]++
	}
	var parts []string
	for _, at := range []string{"play_card", "activate_effect", "scale_up", "distribute_yield", "attack", "end_phase"} {
		if c, ok := actionCounts[at]; ok {
			parts = append(parts, fmt.Sprintf("%s×%d", at, c))
		}
	}
	t.Logf("  Actions: %s", strings.Join(parts, ", "))
}

func logBattleSummary(t *testing.T, faction string, actions []npc.NPCAction) {
	t.Helper()
	attackCount := 0
	for _, a := range actions {
		if a.ActionType == "attack" {
			attackCount++
		}
	}
	if attackCount > 0 {
		t.Logf("  [%s] Battle: %d attacks", faction, attackCount)
	}
}

func logFinalState(t *testing.T, state *model.GameState, game *model.Game, cc *cache.CardCache, f1, f2 string) {
	t.Helper()
	t.Logf("--- Final State (Turn %d) ---", state.CurrentTurn)
	for _, pn := range []int64{1, 2} {
		faction := f1
		if pn == 2 {
			faction = f2
		}
		budget := state.GetBudget(pn)
		insight := state.GetInsightPool(pn)
		hand, _ := state.GetHand(pn)
		repo, _ := state.GetRepository(pn)
		field, _ := state.GetField(pn)

		frontCount, backCount, suppCount := 0, 0, 0
		for _, r := range field.Frontend {
			if r != nil {
				frontCount++
			}
		}
		for _, r := range field.Backend {
			if r != nil {
				backCount++
			}
		}
		for _, s := range field.Support {
			if s != nil {
				suppCount++
			}
		}
		t.Logf("  %s (P%d): Budget=%d Insight=%d Hand=%d Repo=%d Field=[F:%d B:%d S:%d]",
			faction, pn, budget, insight, len(hand), len(repo), frontCount, backCount, suppCount)
	}
}

// TestNPCvsNPC_MatchedAI runs a tournament where each faction uses its matching AI.
// This represents the "correct" way to play each faction.
func TestNPCvsNPC_MatchedAI(t *testing.T) {
	cc, reg := loadTestDeps(t)

	factions := []string{"SD", "Tenki", "Sugar", "Tuners"}

	// Map faction to its matching AI
	getFactionAI := func(faction string) (npc.Strategy, string) {
		switch faction {
		case "SD":
			return npc.NewSDAI(cc, reg), "SDAI"
		case "Tenki":
			return npc.NewTenkiAI(cc, reg), "TenkiAI"
		case "Sugar":
			return npc.NewSugarAI(cc, reg), "SugarAI"
		case "Tuners":
			return npc.NewTunersAI(cc, reg), "TunersAI"
		default:
			return npc.NewStandardAI(cc, reg), "Standard"
		}
	}

	totalGames := 100
	rng := rand.New(rand.NewSource(42))

	type stats struct {
		wins, losses int
		totalTurns   int64
	}

	factionStats := make(map[string]*stats)
	matchupStats := make(map[string]*stats)
	for _, f := range factions {
		factionStats[f] = &stats{}
		for _, f2 := range factions {
			if f != f2 {
				matchupStats[f+" vs "+f2] = &stats{}
			}
		}
	}

	var allResults []matchResult

	for i := 0; i < totalGames; i++ {
		// Random faction pair (no mirror)
		idx1 := rng.Intn(len(factions))
		idx2 := rng.Intn(len(factions) - 1)
		if idx2 >= idx1 {
			idx2++
		}
		f1, f2 := factions[idx1], factions[idx2]

		// Use matching AI for each faction
		ai1, ai1Name := getFactionAI(f1)
		ai2, ai2Name := getFactionAI(f2)

		r := runMatchSilent(t, cc, reg, f1, f2, ai1, ai2, ai1Name, ai2Name)
		allResults = append(allResults, r)

		// Aggregate stats
		if r.winner == f1 {
			factionStats[f1].wins++
			factionStats[f2].losses++
			matchupStats[f1+" vs "+f2].wins++
		} else if r.winner == f2 {
			factionStats[f2].wins++
			factionStats[f1].losses++
			matchupStats[f1+" vs "+f2].losses++
		}
		factionStats[f1].totalTurns += r.turns
		factionStats[f2].totalTurns += r.turns
	}

	// --- Print results ---

	t.Logf("")
	t.Logf("================================================================")
	t.Logf("  Matched AI Tournament: %d games (seed=42)", totalGames)
	t.Logf("  Each faction uses its matching AI")
	t.Logf("================================================================")

	t.Logf("")
	t.Logf("=== Faction Win Rates (with matching AI) ===")
	t.Logf("Faction    |  Win | Loss |  Rate | AvgTurns")
	t.Logf("-----------+------+------+-------+---------")

	sortedFactions := make([]string, len(factions))
	copy(sortedFactions, factions)
	sort.Slice(sortedFactions, func(i, j int) bool {
		s1, s2 := factionStats[sortedFactions[i]], factionStats[sortedFactions[j]]
		total1, total2 := s1.wins+s1.losses, s2.wins+s2.losses
		if total1 == 0 {
			return false
		}
		if total2 == 0 {
			return true
		}
		return float64(s1.wins)/float64(total1) > float64(s2.wins)/float64(total2)
	})

	for _, f := range sortedFactions {
		s := factionStats[f]
		total := s.wins + s.losses
		rate := 0
		if total > 0 {
			rate = int(float64(s.wins) / float64(total) * 100)
		}
		avgTurns := 0.0
		if total > 0 {
			avgTurns = float64(s.totalTurns) / float64(total)
		}
		t.Logf("%-10s | %4d | %4d | %4d%% | %7.1f", f, s.wins, s.losses, rate, avgTurns)
	}

	// Matchup matrix
	t.Logf("")
	t.Logf("=== Matchup Matrix (row=P1 deck, col=P2 deck, value=P1 wins/total) ===")
	t.Logf("           |       SD |    Tenki |    Sugar |   Tuners")
	t.Logf("-----------+----------+----------+----------+---------")

	for _, f1 := range factions {
		parts := make([]string, len(factions))
		for j, f2 := range factions {
			if f1 == f2 {
				parts[j] = fmt.Sprintf("%8s", "-")
			} else {
				key := f1 + " vs " + f2
				s := matchupStats[key]
				total := s.wins + s.losses
				if total > 0 {
					parts[j] = fmt.Sprintf("%4d/%-2d", s.wins, total)
				} else {
					parts[j] = fmt.Sprintf("%8s", "0/0")
				}
			}
		}
		t.Logf("%-10s | %8s | %8s | %8s | %8s", f1, parts[0], parts[1], parts[2], parts[3])
	}

	// Win reason distribution
	t.Logf("")
	t.Logf("=== Win Reasons ===")
	reasonCounts := make(map[string]int)
	for _, r := range allResults {
		reasonCounts[r.reason]++
	}
	reasons := make([]string, 0, len(reasonCounts))
	for r := range reasonCounts {
		reasons = append(reasons, r)
	}
	sort.Strings(reasons)
	for _, r := range reasons {
		t.Logf("  %-20s: %3d (%2.0f%%)", r, reasonCounts[r], float64(reasonCounts[r])/float64(totalGames)*100)
	}

	// Turn limit details
	if reasonCounts["turn_limit"] > 0 {
		t.Logf("")
		t.Logf("=== Turn Limit Details ===")

		// By matchup
		turnLimitByFaction := make(map[string]int)
		for _, r := range allResults {
			if r.reason == "turn_limit" {
				key := r.f1 + " vs " + r.f2
				turnLimitByFaction[key]++
			}
		}
		matchups := make([]string, 0, len(turnLimitByFaction))
		for k := range turnLimitByFaction {
			matchups = append(matchups, k)
		}
		sort.Slice(matchups, func(i, j int) bool {
			if turnLimitByFaction[matchups[i]] != turnLimitByFaction[matchups[j]] {
				return turnLimitByFaction[matchups[i]] > turnLimitByFaction[matchups[j]]
			}
			return matchups[i] < matchups[j]
		})
		t.Logf("Matchup              | Count")
		t.Logf("---------------------+------")
		for _, m := range matchups {
			t.Logf("%-20s | %5d", m, turnLimitByFaction[m])
		}

		// By faction involvement
		t.Logf("")
		t.Logf("Faction Involvement in Turn Limit Games:")
		factionInvolvement := make(map[string]int)
		for _, r := range allResults {
			if r.reason == "turn_limit" {
				factionInvolvement[r.f1]++
				factionInvolvement[r.f2]++
			}
		}
		factionList := make([]string, 0, len(factionInvolvement))
		for f := range factionInvolvement {
			factionList = append(factionList, f)
		}
		sort.Slice(factionList, func(i, j int) bool {
			return factionInvolvement[factionList[i]] > factionInvolvement[factionList[j]]
		})
		for _, f := range factionList {
			t.Logf("  %-10s: %3d times", f, factionInvolvement[f])
		}
	}

	// Turn distribution
	turnBuckets := map[string]int{"1-3": 0, "4-6": 0, "7-10": 0, "11+": 0}
	totalTurns := int64(0)
	for _, r := range allResults {
		totalTurns += r.turns
		switch {
		case r.turns <= 3:
			turnBuckets["1-3"]++
		case r.turns <= 6:
			turnBuckets["4-6"]++
		case r.turns <= 10:
			turnBuckets["7-10"]++
		default:
			turnBuckets["11+"]++
		}
	}
	t.Logf("")
	t.Logf("=== Turn Distribution ===")
	for _, bucket := range []string{"1-3", "4-6", "7-10", "11+"} {
		t.Logf("  %-6s turns: %3d (%2.0f%%)", bucket, turnBuckets[bucket], float64(turnBuckets[bucket])/float64(totalGames)*100)
	}
	t.Logf("  Average: %.1f turns", float64(totalTurns)/float64(totalGames))
}

// TestNPCvsNPC_SanityCheck runs 100 matched-AI games and fails on degenerate outcomes.
// Unlike the other simulation tests that only log, this test enforces invariants:
//   - No faction wins >95% or <5% (with >=10 games played)
//   - No game hits max_iterations (TIMEOUT)
//   - Average turn count is reasonable (2..50)
func TestNPCvsNPC_SanityCheck(t *testing.T) {
	cc, reg := loadTestDeps(t)
	factions := []string{"SD", "Tenki", "Sugar", "Tuners"}
	totalGames := 100
	rng := rand.New(rand.NewSource(42))

	factionWins := make(map[string]int)
	factionGames := make(map[string]int)
	totalTurns := int64(0)
	timeouts := 0

	for i := 0; i < totalGames; i++ {
		idx1 := rng.Intn(len(factions))
		idx2 := rng.Intn(len(factions) - 1)
		if idx2 >= idx1 {
			idx2++
		}
		f1, f2 := factions[idx1], factions[idx2]

		ai1 := npc.GetFactionAI(f1, cc, reg)
		ai2 := npc.GetFactionAI(f2, cc, reg)

		r := runMatchSilent(t, cc, reg, f1, f2, ai1, ai2, f1+"AI", f2+"AI")

		factionGames[f1]++
		factionGames[f2]++
		totalTurns += r.turns

		if r.winner == "TIMEOUT" {
			timeouts++
		} else {
			factionWins[r.winner]++
		}
	}

	// Assert: no TIMEOUT games (stuck game loop)
	if timeouts > 0 {
		t.Errorf("got %d TIMEOUT games (max_iterations); game loop may be stuck", timeouts)
	}

	// Assert: per-faction win rate within 5%-95% (with sufficient sample)
	for _, f := range factions {
		games := factionGames[f]
		if games < 10 {
			continue
		}
		wins := factionWins[f]
		rate := float64(wins) / float64(games) * 100
		if rate > 95 {
			t.Errorf("faction %s win rate %.0f%% (%d/%d) exceeds 95%% — likely a bug", f, rate, wins, games)
		}
		if rate < 5 {
			t.Errorf("faction %s win rate %.0f%% (%d/%d) below 5%% — likely a bug", f, rate, wins, games)
		}
	}

	// Assert: average turn count is reasonable
	avgTurns := float64(totalTurns) / float64(totalGames)
	if avgTurns < 2 {
		t.Errorf("average turn count %.1f is suspiciously low (<2)", avgTurns)
	}
	if avgTurns > 50 {
		t.Errorf("average turn count %.1f is suspiciously high (>50)", avgTurns)
	}

	t.Logf("SanityCheck: %d games, %d timeouts, avg %.1f turns", totalGames, timeouts, avgTurns)
	for _, f := range factions {
		games := factionGames[f]
		wins := factionWins[f]
		t.Logf("  %s: %d/%d (%.0f%%)", f, wins, games, float64(wins)/float64(games)*100)
	}
}
