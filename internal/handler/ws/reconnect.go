package ws

import (
	"context"
	"log"

	"github.com/kenyamaneko/overload-party-common/model"
	"github.com/kenyamaneko/overload-party-battle/internal/service"
)

// RestoreGameState sends the current game state to a reconnecting player.
func RestoreGameState(ctx context.Context, gameService *service.GameService, conn *Connection, gameID string) error {
	clientState, err := gameService.GetGameStateForPlayer(ctx, gameID, conn.PlayerID())
	if err != nil {
		return err
	}

	conn.SendMessage(&WSMessage{
		Type: model.WSMsgGameStateRestore,
		Data: service.MarshalClientGameState(clientState),
	})

	// Send turn controls alongside restored state
	tc, err := gameService.GetTurnControlsForPlayer(ctx, gameID, conn.PlayerID())
	if err != nil {
		log.Printf("get turn controls on restore for %s: %v", conn.PlayerID(), err)
	} else if tc != nil {
		conn.SendMessage(&WSMessage{
			Type: model.WSMsgTurnControls,
			Data: mustMarshal(TurnControlsMessage{
				CanEndPhase:     tc.CanEndPhase,
				DiscardRequired: tc.DiscardRequired,
			}),
		})
	}

	log.Printf("restored game state for player %s in game %s", conn.PlayerID(), gameID)
	return nil
}
