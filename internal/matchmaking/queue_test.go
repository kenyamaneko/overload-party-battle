package matchmaking

import (
	"testing"
)

func TestQueueJoinAndLeave(t *testing.T) {
	q := NewQueue()

	if err := q.Join("p1", 1); err != nil {
		t.Fatalf("Join failed: %v", err)
	}
	if q.Count() != 1 {
		t.Errorf("count = %d, want 1", q.Count())
	}

	// Duplicate join is idempotent (updates deck)
	if err := q.Join("p1", 1); err != nil {
		t.Errorf("duplicate Join should succeed: %v", err)
	}
	if q.Count() != 1 {
		t.Errorf("count after duplicate join = %d, want 1", q.Count())
	}

	q.Leave("p1")
	if q.Count() != 0 {
		t.Errorf("count after leave = %d, want 0", q.Count())
	}
}

func TestQueueGetWaiting(t *testing.T) {
	q := NewQueue()

	_ = q.Join("p1", 1)
	_ = q.Join("p2", 2)
	_ = q.Join("p3", 3)

	waiting := q.GetWaiting()
	if len(waiting) != 3 {
		t.Fatalf("waiting count = %d, want 3", len(waiting))
	}

	// Should be sorted by join time (earliest first)
	if waiting[0].PlayerID != "p1" {
		t.Errorf("first waiting player = %s, want p1", waiting[0].PlayerID)
	}
}

func TestQueueHeartbeat(t *testing.T) {
	q := NewQueue()

	if q.Heartbeat("p1") {
		t.Error("heartbeat should return false for unqueued player")
	}

	_ = q.Join("p1", 1)
	if !q.Heartbeat("p1") {
		t.Error("heartbeat should return true for queued player")
	}
}

func TestQueueRemove(t *testing.T) {
	q := NewQueue()

	_ = q.Join("p1", 1)
	_ = q.Join("p2", 2)

	q.Remove("p1", "p2")
	if q.Count() != 0 {
		t.Errorf("count after remove = %d, want 0", q.Count())
	}
}

func TestQueueIsQueued(t *testing.T) {
	q := NewQueue()

	if q.IsQueued("p1") {
		t.Error("should not be queued")
	}

	_ = q.Join("p1", 1)
	if !q.IsQueued("p1") {
		t.Error("should be queued after join")
	}
}
