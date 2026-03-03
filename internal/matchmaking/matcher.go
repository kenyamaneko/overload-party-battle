package matchmaking

import (
	"context"
	"log"
	"time"
)

// MatchResult represents a successful match between two players.
type MatchResult struct {
	Player1ID   string
	Player1Deck int64
	Player2ID   string
	Player2Deck int64
}

// MatchHandler is called when a match is found.
type MatchHandler func(ctx context.Context, result MatchResult)

// Matcher runs a periodic loop to find player pairs using FIFO ordering.
type Matcher struct {
	queue    *Queue
	handler  MatchHandler
	interval time.Duration
}

func NewMatcher(queue *Queue, handler MatchHandler) *Matcher {
	return &Matcher{
		queue:    queue,
		handler:  handler,
		interval: 1 * time.Second,
	}
}

// Run starts the matching loop. Blocks until context is cancelled.
func (m *Matcher) Run(ctx context.Context) {
	ticker := time.NewTicker(m.interval)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			m.matchPlayers(ctx)
		}
	}
}

func (m *Matcher) matchPlayers(ctx context.Context) {
	waiting := m.queue.GetWaiting() // already sorted by JoinedAt
	if len(waiting) < 2 {
		return
	}

	// FIFO pairing: match players in order of join time
	for i := 0; i+1 < len(waiting); i += 2 {
		p1 := waiting[i]
		p2 := waiting[i+1]

		m.queue.Remove(p1.PlayerID, p2.PlayerID)

		result := MatchResult{
			Player1ID:   p1.PlayerID,
			Player1Deck: p1.DeckID,
			Player2ID:   p2.PlayerID,
			Player2Deck: p2.DeckID,
		}

		go func() {
			defer func() {
				if r := recover(); r != nil {
					log.Printf("match handler panic: %v", r)
				}
			}()
			m.handler(ctx, result)
		}()
	}
}
