package engine

import (
	"context"
	"encoding/json"
	"fmt"
	"math/rand"

	"time"

	"github.com/oklog/ulid/v2"

	"github.com/kenyamaneko/overload-party-common/model"
)

// CreateNewGame creates a fully initialized Game and GameState.
// The game starts in "playing" status with Phase=Draw, Turn=1.
// Both players' decks are shuffled, 5 cards drawn to hand, remainder in repository.
func (e *GameEngine) CreateNewGame(ctx context.Context, player1ID, player2ID string, deck1, deck2 model.DeckSnapshot, firstPlayer int64) (string, error) {
	gameID := ulid.Make().String()

	snap1, err := json.Marshal(deck1)
	if err != nil {
		return "", fmt.Errorf("marshal deck1: %w", err)
	}
	snap2, err := json.Marshal(deck2)
	if err != nil {
		return "", fmt.Errorf("marshal deck2: %w", err)
	}

	game := &model.Game{
		GameID:              gameID,
		Player1ID:           player1ID,
		Player2ID:           player2ID,
		Player1DeckSnapshot: snap1,
		Player2DeckSnapshot: snap2,
		Status:              model.GameStatusPlaying,
		CreatedAt:           time.Time{},
		UpdatedAt:           time.Time{},
	}

	emptyField, _ := json.Marshal(&model.Field{})
	emptyHand, _ := json.Marshal([]model.HandCard{})
	emptyRepo, _ := json.Marshal([]int64{})
	emptyTrash, _ := json.Marshal([]int64{})

	state := &model.GameState{
		GameID:             gameID,
		Version:            1,
		CurrentTurn:        1,
		CurrentPhase:       model.PhaseDraw,
		ActivePlayer:       firstPlayer,
		Player1Budget:      model.InitialBudget,
		Player1InsightPool: model.InitialInsightPool,
		Player1Field:       emptyField,
		Player1Hand:        emptyHand,
		Player1Repository:  emptyRepo,
		Player1Trash:       emptyTrash,
		Player1TimeBank:    model.InitialTimeBank,
		Player2Budget:      model.InitialBudget,
		Player2InsightPool: model.InitialInsightPool,
		Player2Field:       emptyField,
		Player2Hand:        emptyHand,
		Player2Repository:  emptyRepo,
		Player2Trash:       emptyTrash,
		Player2TimeBank:    model.InitialTimeBank,
		UpdatedAt:          time.Time{},
	}

	// Initialize hands and repositories from shuffled decks
	for pNum, deckSnap := range map[int64]model.DeckSnapshot{1: deck1, 2: deck2} {
		cards := make([]int64, len(deckSnap.Cards))
		copy(cards, deckSnap.Cards)

		// Shuffle
		rand.Shuffle(len(cards), func(i, j int) {
			cards[i], cards[j] = cards[j], cards[i]
		})

		// Draw initial hand
		drawCount := model.InitialHand
		if drawCount > len(cards) {
			drawCount = len(cards)
		}
		hand := make([]model.HandCard, 0, drawCount)
		for i := 0; i < drawCount; i++ {
			hand = append(hand, model.HandCard{
				InstanceID: state.NextInstanceID(),
				CardID:     cards[i],
			})
		}
		repo := cards[drawCount:]

		if err := state.SetHand(pNum, hand); err != nil {
			return "", fmt.Errorf("set hand %d: %w", pNum, err)
		}
		if err := state.SetRepository(pNum, repo); err != nil {
			return "", fmt.Errorf("set repository %d: %w", pNum, err)
		}
	}

	if err := e.repo.CreateGame(ctx, game, state); err != nil {
		return "", fmt.Errorf("create game: %w", err)
	}

	return gameID, nil
}
