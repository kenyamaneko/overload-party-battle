package ws

import (
	"context"
	"fmt"
	"log"
	"net/http"
	"strings"
	"time"

	"cloud.google.com/go/civil"
	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/gorilla/websocket"

	"github.com/kenyamaneko/overload-party-common/model"
	"github.com/kenyamaneko/overload-party-battle/internal/repository"
)

// DevPlayerSetup is called after a new dev player is auto-created via WS.
type DevPlayerSetup func(ctx context.Context, playerID string) error

// DevHandler handles WebSocket upgrades without Firebase authentication.
// Used for local development mode. Auto-creates players on first connect.
type DevHandler struct {
	manager    *Manager
	playerRepo repository.PlayerRepo
	onCreated  []DevPlayerSetup
	upgrader   websocket.Upgrader
}

func NewDevHandler(manager *Manager, playerRepo repository.PlayerRepo, onCreated ...DevPlayerSetup) *DevHandler {
	return &DevHandler{
		manager:    manager,
		playerRepo: playerRepo,
		onCreated:  onCreated,
		upgrader: websocket.Upgrader{
			ReadBufferSize:  1024,
			WriteBufferSize: 1024,
			CheckOrigin: func(r *http.Request) bool {
				return true // local dev: allow all origins
			},
		},
	}
}

// HandleUpgrade handles GET /ws?token=<dev-token>
// Token format: "dev-token-{uid}" → uid = "{uid}"
// If the player does not exist, it is auto-created for local development convenience.
func (h *DevHandler) HandleUpgrade(c *gin.Context) {
	token := c.Query("token")

	uid := "dev-anonymous"
	if strings.HasPrefix(token, "dev-token-") {
		uid = strings.TrimPrefix(token, "dev-token-")
	}

	// Resolve FirebaseUID → PlayerID, auto-creating if needed
	playerID, created, err := h.resolveOrCreatePlayer(c.Request.Context(), uid)
	if err != nil {
		log.Printf("ws dev handler: resolve player for uid %s: %v", uid, err)
		c.JSON(500, gin.H{"error": "failed to resolve player"})
		return
	}

	if created {
		for _, fn := range h.onCreated {
			if err := fn(c.Request.Context(), playerID); err != nil {
				log.Printf("ws dev player setup failed: %v", err)
			}
		}
	}

	wsConn, err := h.upgrader.Upgrade(c.Writer, c.Request, nil)
	if err != nil {
		log.Printf("ws upgrade error: %v", err)
		return
	}

	conn := NewConnection(wsConn, playerID)
	h.manager.Register(conn)

	go conn.WritePump()
	go conn.ReadPump(h.manager)
}

// resolveOrCreatePlayer looks up a player by FirebaseUID.
// If not found, auto-creates one for local development convenience.
func (h *DevHandler) resolveOrCreatePlayer(ctx context.Context, firebaseUID string) (string, bool, error) {
	player, err := h.playerRepo.FindByFirebaseUID(ctx, firebaseUID)
	if err != nil {
		return "", false, fmt.Errorf("find by firebase uid: %w", err)
	}
	if player != nil {
		return player.PlayerID, false, nil
	}

	// Auto-create player for local dev
	now := time.Now()
	newPlayer := &model.Player{
		PlayerID:    uuid.New().String(),
		FirebaseUID: firebaseUID,
		Username:    "Dev_" + firebaseUID,
		Level:       1,
		Exp:         0,
		CreatedAt:   now,
		UpdatedAt:   now,
	}
	dailyBattle := &model.PlayerDailyBattle{
		PlayerID:         newPlayer.PlayerID,
		DailyBattleCount: 0,
		LastResetDate:    civil.DateOf(now.UTC()),
	}

	if err := h.playerRepo.Create(ctx, newPlayer, dailyBattle); err != nil {
		return "", false, fmt.Errorf("auto-create player: %w", err)
	}
	log.Printf("auto-created dev player: uid=%s playerID=%s username=%s", firebaseUID, newPlayer.PlayerID, newPlayer.Username)

	return newPlayer.PlayerID, true, nil
}
