package main

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"math/rand"
	"net/http"
	"os/signal"
	"syscall"
	"time"

	"github.com/gin-gonic/gin"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/engine"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-battle/internal/handler/ws"
	"github.com/kenyamaneko/overload-party-battle/internal/matchmaking"
	"github.com/kenyamaneko/overload-party-battle/internal/middleware"
	"github.com/kenyamaneko/overload-party-battle/internal/npc"
	"github.com/kenyamaneko/overload-party-battle/internal/repository"
	"github.com/kenyamaneko/overload-party-battle/internal/service"
	"github.com/kenyamaneko/overload-party-common/model"
)

func main() {
	log.Println("=== Overload Party Battle (LOCAL MODE) ===")

	// 1. Card cache from JSON
	cardCache := cache.NewCardCache()
	if err := cardCache.LoadFromJSON("internal/cache/cards_gen.json"); err != nil {
		log.Fatalf("failed to load cards from JSON: %v", err)
	}
	log.Printf("loaded %d cards from internal/cache/cards_gen.json", cardCache.Count())

	// 2. Mock repositories
	gameRepo := repository.NewMockGameRepository()
	playerRepo := repository.NewMockPlayerRepository()
	deckRepo := repository.NewMockDeckRepository()
	gameConfigRepo := repository.NewMockGameConfigRepository()

	// 3. Game engine + effects
	gameEngine := engine.NewGameEngine(gameRepo, cardCache)
	effectRegistry := effect.NewEffectRegistry()
	effect.RegisterAllEffects(effectRegistry)
	gameEngine.SetEffectRegistry(effectRegistry)

	// 4. Matchmaking
	matchQueue := matchmaking.NewQueue()

	// 5. Services
	playerService := service.NewPlayerService(playerRepo, gameConfigRepo)
	gameService := service.NewGameService(gameEngine, gameRepo, deckRepo, cardCache, matchQueue, playerService)

	// 6. Dev player setup: give all active cards + starter deck on first connect
	devPlayerSetup := func(ctx context.Context, playerID string) error {
		var playerCards []*model.PlayerCard
		for _, card := range cardCache.All() {
			if !card.IsActive {
				continue
			}
			copies := model.RestrictionCopyCount(card.Restriction)
			playerCards = append(playerCards, &model.PlayerCard{
				PlayerID:            playerID,
				CardNo:              card.CardNo,
				IllustrationVariant: 0,
				Count:               copies,
			})
		}
		deckRepo.SeedPlayerCards(playerID, playerCards)

		// Create a starter deck (first 30 cards by expanding counts)
		var deckCards []model.DeckCard
		remaining := model.DeckSize
		for _, pc := range playerCards {
			if remaining <= 0 {
				break
			}
			use := pc.Count
			if use > remaining {
				use = remaining
			}
			deckCards = append(deckCards, model.DeckCard{
				PlayerID:            playerID,
				CardNo:              pc.CardNo,
				IllustrationVariant: pc.IllustrationVariant,
				Count:               use,
			})
			remaining -= use
		}
		totalCards := model.DeckSize - remaining
		deck := &model.Deck{
			PlayerID:  playerID,
			DeckName:  "Starter Deck",
			IsValid:   totalCards == model.DeckSize,
			CreatedAt: time.Now(),
			UpdatedAt: time.Now(),
		}
		if err := deckRepo.Create(ctx, deck, deckCards); err != nil {
			return err
		}
		log.Printf("auto-created starter deck for %s: %d cards, deckID=%d", playerID, totalCards, deck.DeckID)
		return nil
	}

	// 7. WebSocket manager
	wsManager := ws.NewManager(gameService)
	gameService.SetActionObserver(func(gameID, actionType string, actionData json.RawMessage) {
		wsManager.BroadcastActionPerformed(gameID, actionType, actionData)
	})
	gameService.SetBattleEventObserver(func(gameID string, event service.BattleEvent) {
		wsManager.BroadcastBattleEvent(gameID, event)
	})
	wsDevHandler := ws.NewDevHandler(wsManager, playerRepo, devPlayerSetup)

	// 8. Matchmaking handler
	matcher := matchmaking.NewMatcher(matchQueue, func(matchCtx context.Context, result matchmaking.MatchResult) {
		game, err := gameService.CreateGameFromMatch(matchCtx, result)
		if err != nil {
			log.Printf("failed to create game from match: %v", err)
			return
		}
		wsManager.JoinGame(result.Player1ID, game.GameID)
		wsManager.JoinGame(result.Player2ID, game.GameID)
		matchMsg, _ := json.Marshal(ws.MatchFoundMessage{
			GameID:    game.GameID,
			Player1ID: result.Player1ID,
			Player2ID: result.Player2ID,
		})
		wsManager.BroadcastToGame(game.GameID, &ws.WSMessage{
			Type: model.WSMsgMatchFound,
			Data: matchMsg,
		})
	})

	// 9. Router
	r := gin.Default()
	r.Use(middleware.CORS())

	r.GET("/health", func(c *gin.Context) {
		c.JSON(http.StatusOK, gin.H{"status": "ok", "mode": "local"})
	})

	// Dev REST endpoints (no auth, for balance testing)
	dev := r.Group("/api/dev")
	{
		dev.POST("/games", devCreateGame(gameEngine, gameRepo, cardCache))
		dev.POST("/games/:gameId/action", devProcessAction(gameEngine))
		dev.GET("/games/:gameId/state", devGetState(gameEngine, gameRepo))
		dev.GET("/cards", func(c *gin.Context) {
			c.JSON(http.StatusOK, cardCache.All())
		})
	}

	// WebSocket endpoint (dev auth at connection time)
	r.GET("/ws", wsDevHandler.HandleUpgrade)

	srv := &http.Server{
		Addr:    ":9002",
		Handler: r,
	}

	srvCtx, stop := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
	defer stop()

	go matcher.Run(srvCtx)

	go func() {
		log.Println("battle local server starting on :9002")
		log.Println("  WS:   ws://localhost:9002/ws?token=dev-token-{uid}")
		log.Println("  Dev:  http://localhost:9002/api/dev/")
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Fatalf("listen: %v", err)
		}
	}()

	<-srvCtx.Done()
	log.Println("shutting down...")

	shutdownCtx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	if err := srv.Shutdown(shutdownCtx); err != nil {
		log.Fatalf("server forced to shutdown: %v", err)
	}
	log.Println("battle local server exited")
}

// --- Dev REST handlers (inline, no separate package needed) ---

func devCreateGame(eng *engine.GameEngine, gameRepo repository.GameRepository, cardCache *cache.CardCache) gin.HandlerFunc {
	type request struct {
		Player1ID   string `json:"player1Id" binding:"required"`
		Player2ID   string `json:"player2Id" binding:"required"`
		Deck1       string `json:"deck1" binding:"required"`
		Deck2       string `json:"deck2" binding:"required"`
		FirstPlayer *int64 `json:"firstPlayer,omitempty"`
	}
	return func(c *gin.Context) {
		var req request
		if err := c.ShouldBindJSON(&req); err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
			return
		}

		deck1 := npc.GetNPCDeck(req.Deck1)
		deck2 := npc.GetNPCDeck(req.Deck2)
		if deck1 == nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": fmt.Sprintf("unknown deck: %s", req.Deck1)})
			return
		}
		if deck2 == nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": fmt.Sprintf("unknown deck: %s", req.Deck2)})
			return
		}

		firstPlayer := int64(1)
		if req.FirstPlayer != nil {
			firstPlayer = *req.FirstPlayer
		} else if rand.Intn(2) == 1 {
			firstPlayer = 2
		}

		gameID, err := eng.CreateNewGame(c.Request.Context(),
			req.Player1ID, req.Player2ID,
			model.DeckSnapshot{DeckID: "deck1", Cards: deck1.Cards},
			model.DeckSnapshot{DeckID: "deck2", Cards: deck2.Cards},
			firstPlayer,
		)
		if err != nil {
			c.JSON(http.StatusInternalServerError, gin.H{"error": err.Error()})
			return
		}

		c.JSON(http.StatusOK, gin.H{"gameId": gameID})
	}
}

func devProcessAction(eng *engine.GameEngine) gin.HandlerFunc {
	type request struct {
		PlayerID   string          `json:"playerId" binding:"required"`
		ActionType string          `json:"actionType" binding:"required"`
		Data       json.RawMessage `json:"data"`
	}
	return func(c *gin.Context) {
		gameID := c.Param("gameId")

		var req request
		if err := c.ShouldBindJSON(&req); err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
			return
		}

		if req.Data == nil {
			req.Data = json.RawMessage(`{}`)
		}

		result, err := eng.ProcessAction(c.Request.Context(), gameID, req.PlayerID, req.ActionType, req.Data)
		if err != nil {
			c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
			return
		}

		c.JSON(http.StatusOK, gin.H{
			"gameOver":  result.GameOver,
			"winnerNum": result.WinnerNum,
			"winReason": result.WinReason,
		})
	}
}

func devGetState(eng *engine.GameEngine, gameRepo repository.GameRepository) gin.HandlerFunc {
	return func(c *gin.Context) {
		gameID := c.Param("gameId")
		ctx := c.Request.Context()

		game, err := gameRepo.GetGame(ctx, gameID)
		if err != nil {
			c.JSON(http.StatusNotFound, gin.H{"error": err.Error()})
			return
		}

		// Auto-advance if at draw phase
		if game.Status == model.GameStatusPlaying {
			state, _ := gameRepo.GetGameState(ctx, gameID)
			if state != nil && state.CurrentPhase == model.PhaseDraw {
				gameOver, winReason, err := eng.RunAutoAdvance(ctx, gameID)
				if err != nil {
					c.JSON(http.StatusInternalServerError, gin.H{"error": err.Error()})
					return
				}
				if gameOver {
					game, _ = gameRepo.GetGame(ctx, gameID)
					c.JSON(http.StatusOK, gin.H{
						"game":      game,
						"state":     state,
						"gameOver":  true,
						"winReason": winReason,
					})
					return
				}
			}
		}

		state, err := gameRepo.GetGameState(ctx, gameID)
		if err != nil {
			c.JSON(http.StatusNotFound, gin.H{"error": err.Error()})
			return
		}

		c.JSON(http.StatusOK, gin.H{
			"game":     game,
			"state":    state,
			"gameOver": game.Status == model.GameStatusFinished,
		})
	}
}
