package matchmaking

import (
	"sync"
	"time"
)

// QueueEntry represents a player waiting in the matchmaking queue.
type QueueEntry struct {
	PlayerID string
	DeckID   int64
	JoinedAt time.Time
}

// Queue is an in-memory matchmaking queue.
type Queue struct {
	mu      sync.Mutex
	entries map[string]*QueueEntry // playerID → entry
}

func NewQueue() *Queue {
	return &Queue{
		entries: make(map[string]*QueueEntry),
	}
}

// Join adds a player to the queue. Idempotent: if already queued, updates the deck.
func (q *Queue) Join(playerID string, deckID int64) error {
	q.mu.Lock()
	defer q.mu.Unlock()

	if existing, ok := q.entries[playerID]; ok {
		existing.DeckID = deckID
		return nil
	}

	q.entries[playerID] = &QueueEntry{
		PlayerID: playerID,
		DeckID:   deckID,
		JoinedAt: time.Now(),
	}
	return nil
}

// Leave removes a player from the queue.
func (q *Queue) Leave(playerID string) {
	q.mu.Lock()
	defer q.mu.Unlock()
	delete(q.entries, playerID)
}

// Heartbeat updates the player's presence. Returns false if not in queue.
func (q *Queue) Heartbeat(playerID string) bool {
	q.mu.Lock()
	defer q.mu.Unlock()
	_, exists := q.entries[playerID]
	return exists
}

// GetWaiting returns a snapshot of all waiting players sorted by join time.
func (q *Queue) GetWaiting() []QueueEntry {
	q.mu.Lock()
	defer q.mu.Unlock()

	entries := make([]QueueEntry, 0, len(q.entries))
	for _, e := range q.entries {
		entries = append(entries, *e)
	}

	// Sort by JoinedAt (earliest first)
	for i := 1; i < len(entries); i++ {
		for j := i; j > 0 && entries[j].JoinedAt.Before(entries[j-1].JoinedAt); j-- {
			entries[j], entries[j-1] = entries[j-1], entries[j]
		}
	}

	return entries
}

// Remove removes specific players from the queue (used after matching).
func (q *Queue) Remove(playerIDs ...string) {
	q.mu.Lock()
	defer q.mu.Unlock()
	for _, id := range playerIDs {
		delete(q.entries, id)
	}
}

// Count returns the number of players in the queue.
func (q *Queue) Count() int {
	q.mu.Lock()
	defer q.mu.Unlock()
	return len(q.entries)
}

// IsQueued checks if a player is in the queue.
func (q *Queue) IsQueued(playerID string) bool {
	q.mu.Lock()
	defer q.mu.Unlock()
	_, exists := q.entries[playerID]
	return exists
}
