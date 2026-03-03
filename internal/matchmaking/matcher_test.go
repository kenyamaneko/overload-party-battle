package matchmaking

import (
	"context"
	"sync"
	"testing"
	"time"
)

// collectMatches is a test helper that returns a MatchHandler and a function
// to retrieve all collected results. Thread-safe.
func collectMatches() (MatchHandler, func() []MatchResult) {
	var mu sync.Mutex
	var results []MatchResult

	handler := func(ctx context.Context, result MatchResult) {
		mu.Lock()
		defer mu.Unlock()
		results = append(results, result)
	}

	getter := func() []MatchResult {
		mu.Lock()
		defer mu.Unlock()
		cp := make([]MatchResult, len(results))
		copy(cp, results)
		return cp
	}

	return handler, getter
}

// joinWithTime adds a player to the queue and manually sets the JoinedAt time
// so test ordering is deterministic regardless of execution speed.
func joinWithTime(q *Queue, playerID string, deckID int64, joinedAt time.Time) {
	q.mu.Lock()
	defer q.mu.Unlock()
	q.entries[playerID] = &QueueEntry{
		PlayerID: playerID,
		DeckID:   deckID,
		JoinedAt: joinedAt,
	}
}

func TestMatchPlayers_TwoPlayers(t *testing.T) {
	q := NewQueue()
	handler, getResults := collectMatches()
	m := NewMatcher(q, handler)

	base := time.Now()
	joinWithTime(q, "alice", 10, base)
	joinWithTime(q, "bob", 20, base.Add(1*time.Millisecond))

	m.matchPlayers(context.Background())

	// Handler is called in a goroutine; give it a moment to complete.
	time.Sleep(50 * time.Millisecond)

	results := getResults()
	if len(results) != 1 {
		t.Fatalf("got %d matches, want 1", len(results))
	}

	r := results[0]
	// FIFO: alice joined first, so she is Player1.
	if r.Player1ID != "alice" {
		t.Errorf("Player1ID = %q, want %q", r.Player1ID, "alice")
	}
	if r.Player1Deck != 10 {
		t.Errorf("Player1Deck = %d, want 10", r.Player1Deck)
	}
	if r.Player2ID != "bob" {
		t.Errorf("Player2ID = %q, want %q", r.Player2ID, "bob")
	}
	if r.Player2Deck != 20 {
		t.Errorf("Player2Deck = %d, want 20", r.Player2Deck)
	}

	// Queue should be empty after matching.
	if q.Count() != 0 {
		t.Errorf("queue count = %d, want 0", q.Count())
	}
}

func TestMatchPlayers_SinglePlayer_NoMatch(t *testing.T) {
	q := NewQueue()
	handler, getResults := collectMatches()
	m := NewMatcher(q, handler)

	_ = q.Join("lonely", 1)

	m.matchPlayers(context.Background())
	time.Sleep(50 * time.Millisecond)

	results := getResults()
	if len(results) != 0 {
		t.Fatalf("got %d matches, want 0", len(results))
	}

	// Player should still be in the queue.
	if q.Count() != 1 {
		t.Errorf("queue count = %d, want 1", q.Count())
	}
}

func TestMatchPlayers_EmptyQueue(t *testing.T) {
	q := NewQueue()
	handler, getResults := collectMatches()
	m := NewMatcher(q, handler)

	m.matchPlayers(context.Background())
	time.Sleep(50 * time.Millisecond)

	results := getResults()
	if len(results) != 0 {
		t.Fatalf("got %d matches, want 0", len(results))
	}
}

func TestMatchPlayers_FourPlayers_TwoMatches(t *testing.T) {
	q := NewQueue()
	handler, getResults := collectMatches()
	m := NewMatcher(q, handler)

	base := time.Now()
	joinWithTime(q, "p1", 1, base)
	joinWithTime(q, "p2", 2, base.Add(1*time.Millisecond))
	joinWithTime(q, "p3", 3, base.Add(2*time.Millisecond))
	joinWithTime(q, "p4", 4, base.Add(3*time.Millisecond))

	m.matchPlayers(context.Background())
	time.Sleep(50 * time.Millisecond)

	results := getResults()
	if len(results) != 2 {
		t.Fatalf("got %d matches, want 2", len(results))
	}

	// Handler goroutines run concurrently so result order is non-deterministic.
	// Build a set keyed by Player1ID for order-independent assertions.
	byP1 := make(map[string]MatchResult)
	for _, r := range results {
		byP1[r.Player1ID] = r
	}

	// FIFO pairing: p1+p2 and p3+p4.
	if r, ok := byP1["p1"]; !ok || r.Player2ID != "p2" {
		t.Errorf("expected p1 vs p2 match, got %+v", results)
	}
	if r, ok := byP1["p3"]; !ok || r.Player2ID != "p4" {
		t.Errorf("expected p3 vs p4 match, got %+v", results)
	}

	if q.Count() != 0 {
		t.Errorf("queue count = %d, want 0", q.Count())
	}
}

func TestMatchPlayers_OddNumber_OneLeftOver(t *testing.T) {
	q := NewQueue()
	handler, getResults := collectMatches()
	m := NewMatcher(q, handler)

	base := time.Now()
	joinWithTime(q, "p1", 1, base)
	joinWithTime(q, "p2", 2, base.Add(1*time.Millisecond))
	joinWithTime(q, "p3", 3, base.Add(2*time.Millisecond))

	m.matchPlayers(context.Background())
	time.Sleep(50 * time.Millisecond)

	results := getResults()
	if len(results) != 1 {
		t.Fatalf("got %d matches, want 1", len(results))
	}

	// p1 and p2 should be matched; p3 left over.
	if results[0].Player1ID != "p1" || results[0].Player2ID != "p2" {
		t.Errorf("match: got %s vs %s, want p1 vs p2", results[0].Player1ID, results[0].Player2ID)
	}

	if q.Count() != 1 {
		t.Errorf("queue count = %d, want 1", q.Count())
	}
	if !q.IsQueued("p3") {
		t.Error("p3 should still be in queue")
	}
}

func TestMatchPlayers_PlayerLeavesBeforeMatching(t *testing.T) {
	q := NewQueue()
	handler, getResults := collectMatches()
	m := NewMatcher(q, handler)

	base := time.Now()
	joinWithTime(q, "stays", 1, base)
	joinWithTime(q, "leaves", 2, base.Add(1*time.Millisecond))

	// Player leaves before the matcher ticks.
	q.Leave("leaves")

	m.matchPlayers(context.Background())
	time.Sleep(50 * time.Millisecond)

	results := getResults()
	if len(results) != 0 {
		t.Fatalf("got %d matches, want 0 (one player left)", len(results))
	}

	if q.Count() != 1 {
		t.Errorf("queue count = %d, want 1", q.Count())
	}
	if !q.IsQueued("stays") {
		t.Error("'stays' should still be in queue")
	}
}

func TestMatchPlayers_DeckIDsPassedThrough(t *testing.T) {
	q := NewQueue()
	handler, getResults := collectMatches()
	m := NewMatcher(q, handler)

	base := time.Now()
	joinWithTime(q, "a", 42, base)
	joinWithTime(q, "b", 99, base.Add(1*time.Millisecond))

	m.matchPlayers(context.Background())
	time.Sleep(50 * time.Millisecond)

	results := getResults()
	if len(results) != 1 {
		t.Fatalf("got %d matches, want 1", len(results))
	}
	if results[0].Player1Deck != 42 {
		t.Errorf("Player1Deck = %d, want 42", results[0].Player1Deck)
	}
	if results[0].Player2Deck != 99 {
		t.Errorf("Player2Deck = %d, want 99", results[0].Player2Deck)
	}
}

func TestMatchPlayers_FIFOOrder(t *testing.T) {
	q := NewQueue()
	handler, getResults := collectMatches()
	m := NewMatcher(q, handler)

	// Deliberately add players in non-alphabetical order with explicit times
	// to verify FIFO ordering by join time.
	base := time.Now()
	joinWithTime(q, "charlie", 3, base)
	joinWithTime(q, "alice", 1, base.Add(1*time.Millisecond))
	joinWithTime(q, "bob", 2, base.Add(2*time.Millisecond))
	joinWithTime(q, "diana", 4, base.Add(3*time.Millisecond))

	m.matchPlayers(context.Background())
	time.Sleep(50 * time.Millisecond)

	results := getResults()
	if len(results) != 2 {
		t.Fatalf("got %d matches, want 2", len(results))
	}

	// Handler goroutines run concurrently so result order is non-deterministic.
	byP1 := make(map[string]MatchResult)
	for _, r := range results {
		byP1[r.Player1ID] = r
	}

	// charlie (earliest) paired with alice (second).
	if r, ok := byP1["charlie"]; !ok || r.Player2ID != "alice" {
		t.Errorf("expected charlie vs alice match, got %+v", results)
	}
	// bob (third) paired with diana (fourth).
	if r, ok := byP1["bob"]; !ok || r.Player2ID != "diana" {
		t.Errorf("expected bob vs diana match, got %+v", results)
	}
}

func TestMatchPlayers_ConsecutiveTicks(t *testing.T) {
	q := NewQueue()
	handler, getResults := collectMatches()
	m := NewMatcher(q, handler)

	// First tick: 1 player, no match.
	base := time.Now()
	joinWithTime(q, "p1", 1, base)
	m.matchPlayers(context.Background())
	time.Sleep(50 * time.Millisecond)

	if len(getResults()) != 0 {
		t.Fatal("expected no matches after first tick")
	}
	if q.Count() != 1 {
		t.Fatalf("queue count = %d, want 1", q.Count())
	}

	// Second tick: another player joins, now they should match.
	joinWithTime(q, "p2", 2, base.Add(1*time.Millisecond))
	m.matchPlayers(context.Background())
	time.Sleep(50 * time.Millisecond)

	results := getResults()
	if len(results) != 1 {
		t.Fatalf("got %d matches, want 1", len(results))
	}
	if results[0].Player1ID != "p1" || results[0].Player2ID != "p2" {
		t.Errorf("match: got %s vs %s, want p1 vs p2", results[0].Player1ID, results[0].Player2ID)
	}
	if q.Count() != 0 {
		t.Errorf("queue count = %d, want 0", q.Count())
	}
}

func TestMatcher_Run_CancelStopsLoop(t *testing.T) {
	q := NewQueue()
	handler, _ := collectMatches()
	m := NewMatcher(q, handler)
	m.interval = 10 * time.Millisecond // speed up for test

	ctx, cancel := context.WithCancel(context.Background())

	done := make(chan struct{})
	go func() {
		m.Run(ctx)
		close(done)
	}()

	// Let the loop tick a few times.
	time.Sleep(50 * time.Millisecond)
	cancel()

	select {
	case <-done:
		// Run returned successfully.
	case <-time.After(1 * time.Second):
		t.Fatal("Run did not return after context cancellation")
	}
}

func TestMatcher_Run_MatchesDuringLoop(t *testing.T) {
	q := NewQueue()
	handler, getResults := collectMatches()
	m := NewMatcher(q, handler)
	m.interval = 10 * time.Millisecond

	base := time.Now()
	joinWithTime(q, "p1", 1, base)
	joinWithTime(q, "p2", 2, base.Add(1*time.Millisecond))

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	go m.Run(ctx)

	// Wait for at least one tick to process the match.
	time.Sleep(100 * time.Millisecond)

	results := getResults()
	if len(results) != 1 {
		t.Fatalf("got %d matches, want 1", len(results))
	}
	if results[0].Player1ID != "p1" || results[0].Player2ID != "p2" {
		t.Errorf("match: got %s vs %s, want p1 vs p2", results[0].Player1ID, results[0].Player2ID)
	}
	if q.Count() != 0 {
		t.Errorf("queue count = %d, want 0", q.Count())
	}
}

func TestMatchPlayers_HandlerPanicDoesNotCrash(t *testing.T) {
	q := NewQueue()
	panicHandler := func(ctx context.Context, result MatchResult) {
		panic("test panic")
	}
	m := NewMatcher(q, panicHandler)

	base := time.Now()
	joinWithTime(q, "p1", 1, base)
	joinWithTime(q, "p2", 2, base.Add(1*time.Millisecond))

	// This should not panic the test process; the matcher recovers from handler panics.
	m.matchPlayers(context.Background())
	time.Sleep(50 * time.Millisecond)

	// Players should still be removed from the queue even if handler panics.
	if q.Count() != 0 {
		t.Errorf("queue count = %d, want 0", q.Count())
	}
}

func TestMatchPlayers_SixPlayers_ThreeMatches(t *testing.T) {
	q := NewQueue()
	handler, getResults := collectMatches()
	m := NewMatcher(q, handler)

	base := time.Now()
	for i := 0; i < 6; i++ {
		joinWithTime(q, string(rune('a'+i)), int64(i+1), base.Add(time.Duration(i)*time.Millisecond))
	}

	m.matchPlayers(context.Background())
	time.Sleep(50 * time.Millisecond)

	results := getResults()
	if len(results) != 3 {
		t.Fatalf("got %d matches, want 3", len(results))
	}
	if q.Count() != 0 {
		t.Errorf("queue count = %d, want 0", q.Count())
	}
}

func TestNewMatcher_DefaultInterval(t *testing.T) {
	q := NewQueue()
	m := NewMatcher(q, nil)

	if m.interval != 1*time.Second {
		t.Errorf("default interval = %v, want 1s", m.interval)
	}
	if m.queue != q {
		t.Error("queue not set correctly")
	}
}
