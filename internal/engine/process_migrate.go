package engine

import (
	"encoding/json"
	"fmt"
	"time"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-common/model"
)

// MigrateRequest is the client request to start a migration.
type MigrateRequest struct {
	SourceInstanceID string `json:"sourceInstanceId"`
	TargetInstanceID string `json:"targetInstanceId"`
}

func processMigrate(state *model.GameState, game *model.Game, playerNum int64, req MigrateRequest, cc *cache.CardCache) (*ActionResult, error) {
	field, err := state.GetField(playerNum)
	if err != nil {
		return nil, fmt.Errorf("get field: %w", err)
	}

	// Find source and target on the field
	source := findResource(field, req.SourceInstanceID)
	if source == nil {
		return nil, fmt.Errorf("source resource %s not found on field", req.SourceInstanceID)
	}
	target := findResource(field, req.TargetInstanceID)
	if target == nil {
		return nil, fmt.Errorf("target resource %s not found on field", req.TargetInstanceID)
	}

	// Validate: both must be face-up
	if !source.FaceUp {
		return nil, fmt.Errorf("source resource is not face-up")
	}
	if !target.FaceUp {
		return nil, fmt.Errorf("target resource is not face-up")
	}

	// Validate: source must not already have a migration target
	if source.MigrationTarget != nil {
		return nil, fmt.Errorf("source resource is already migrating")
	}
	// Validate: target must not already be a migration destination
	if target.MigratingFrom != nil {
		return nil, fmt.Errorf("target resource is already a migration destination")
	}

	// Validate: target's deploy_turns >= source's deploy_turns
	sourceCard := cc.Get(source.CardID)
	targetCard := cc.Get(target.CardID)
	if sourceCard == nil || targetCard == nil {
		return nil, fmt.Errorf("card data not found")
	}
	if targetCard.DeployTurns < sourceCard.DeployTurns {
		return nil, fmt.Errorf("target deploy_turns (%d) must be >= source deploy_turns (%d)", targetCard.DeployTurns, sourceCard.DeployTurns)
	}

	// Set migration relationship
	target.MigratingFrom = &source.InstanceID
	source.MigrationTarget = &target.InstanceID
	target.MigratingOnTurn = state.CurrentTurn

	if err := state.SetField(playerNum, field); err != nil {
		return nil, fmt.Errorf("set field: %w", err)
	}

	eventData, _ := json.Marshal(map[string]interface{}{
		"sourceInstanceId": req.SourceInstanceID,
		"targetInstanceId": req.TargetInstanceID,
		"sourceCardId":     source.CardID,
		"targetCardId":     target.CardID,
	})
	playerID := model.PlayerIDForNum(game, playerNum)
	return &ActionResult{
		Events: []model.GameEvent{{
			GameID:    game.GameID,
			EventType: model.EventMigrate,
			PlayerID:  &playerID,
			EventData: eventData,
			CreatedAt: time.Time{},
		}},
	}, nil
}

// findResource searches frontend and backend for a resource with the given instanceID.
func findResource(field *model.Field, instanceID string) *model.ResourceInstance {
	for _, res := range field.Frontend {
		if res != nil && res.InstanceID == instanceID {
			return res
		}
	}
	for _, res := range field.Backend {
		if res != nil && res.InstanceID == instanceID {
			return res
		}
	}
	return nil
}

// processMigrationCompletion checks for completed migrations at the start of a player's turn.
// A migration completes when the target's owner has had a full turn cycle since migration started
// (i.e., state.CurrentTurn - target.MigratingOnTurn >= 2).
func processMigrationCompletion(state *model.GameState, playerNum int64) ([]migrationEvent, error) {
	field, err := state.GetField(playerNum)
	if err != nil {
		return nil, err
	}

	var events []migrationEvent
	changed := false

	// Scan all resources for completed migrations
	scanZone := func(zone [3]*model.ResourceInstance) {
		for _, res := range zone {
			if res == nil || res.MigratingFrom == nil {
				continue
			}
			// Check if enough turns have passed
			if state.CurrentTurn-res.MigratingOnTurn < 2 {
				continue
			}

			// Migration complete — remove source
			sourceID := *res.MigratingFrom
			source := findResource(field, sourceID)
			if source != nil {
				// Source still alive: remove without SLA penalty
				removeResourceFromField(field, sourceID)
				// Add to trash
				_ = addToTrashByCardID(state, playerNum, source.CardID)
			}
			// Source already destroyed: nothing to do (SLA paid on destruction)

			// Clear migration state on target
			events = append(events, migrationEvent{
				SourceInstanceID: sourceID,
				TargetInstanceID: res.InstanceID,
				TargetCardID:     res.CardID,
			})
			res.MigratingFrom = nil
			res.MigratingOnTurn = 0
			changed = true
		}
	}

	scanZone(field.Frontend)
	scanZone(field.Backend)

	if changed {
		_ = state.SetField(playerNum, field)
	}

	return events, nil
}

type migrationEvent struct {
	SourceInstanceID string
	TargetInstanceID string
	TargetCardID     int64
}

// removeResourceFromField removes a resource by instanceID, clearing its slot.
func removeResourceFromField(field *model.Field, instanceID string) {
	for i, res := range field.Frontend {
		if res != nil && res.InstanceID == instanceID {
			field.Frontend[i] = nil
			return
		}
	}
	for i, res := range field.Backend {
		if res != nil && res.InstanceID == instanceID {
			field.Backend[i] = nil
			return
		}
	}
}

// addToTrashByCardID adds a card_no to the player's trash.
func addToTrashByCardID(state *model.GameState, playerNum int64, cardNo int64) error {
	trash, err := state.GetTrash(playerNum)
	if err != nil {
		return err
	}
	trash = append(trash, cardNo)
	return state.SetTrash(playerNum, trash)
}

// clearMigrationOnSourceDestroyed should be called when a resource with MigrationTarget is destroyed.
// It clears the target's MigratingFrom pointer so the target is unlocked immediately.
func clearMigrationOnSourceDestroyed(field *model.Field, destroyedResource *model.ResourceInstance) {
	if destroyedResource.MigrationTarget == nil {
		return
	}
	targetID := *destroyedResource.MigrationTarget
	target := findResource(field, targetID)
	if target != nil {
		target.MigratingFrom = nil
		target.MigratingOnTurn = 0
	}
}
