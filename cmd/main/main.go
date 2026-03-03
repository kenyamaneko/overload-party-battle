package main

import (
	"context"
	"encoding/json"
	"log"
	"net/http"
	"os/signal"
	"syscall"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/jackc/pgx/v5/pgxpool"

	"github.com/kenyamaneko/overload-party-battle/internal/cache"
	"github.com/kenyamaneko/overload-party-battle/internal/config"
	"github.com/kenyamaneko/overload-party-battle/internal/engine"
	"github.com/kenyamaneko/overload-party-battle/internal/engine/effect"
	"github.com/kenyamaneko/overload-party-battle/internal/handler/ws"
	"github.com/kenyamaneko/overload-party-battle/internal/matchmaking"
	"github.com/kenyamaneko/overload-party-battle/internal/middleware"
	"github.com/kenyamaneko/overload-party-common/model"
	"github.com/kenyamaneko/overload-party-battle/internal/repository"
	"github.com/kenyamaneko/overload-party-battle/internal/service"
)

func main() {
	ctx := context.Background()
	cfg := config.Load()

	if cfg.Env == "prod" && len(cfg.AllowedOrigins) == 0 {
		log.Fatal("ALLOWED_ORIGINS must be set in production")
	}
	if cfg.DatabaseURL == "" {
		log.Fatal("DATABASE_URL must be set")
	}

	if cfg.Env == "prod" {
		gin.SetMode(gin.ReleaseMode)
	}

	// PostgreSQL connection pool
	pool, err := pgxpool.New(ctx, cfg.DatabaseURL)
	if err != nil {
		log.Fatalf("failed to create pg pool: %v", err)
	}
	defer pool.Close()

	// Firebase Auth client (for WS token verification)
	authClient, err := middleware.NewFirebaseAuthClient(ctx)
	if err != nil {
		log.Fatalf("failed to create firebase auth client: %v", err)
	}

	// Repositories (only what game/matchmaking operations need)
	playerRepo := repository.NewPgPlayerRepository(pool)
	cardRepo := repository.NewPgCardRepository(pool)
	deckRepo := repository.NewPgDeckRepository(pool)
	gameRepo := repository.NewPgGameRepository(pool)
	gameConfigRepo := repository.NewPgGameConfigRepository(pool)

	// Card cache (load at startup)
	cardCache := cache.NewCardCache()
	if err := cardCache.Load(ctx, cardRepo); err != nil {
		log.Fatalf("failed to load card cache: %v", err)
	}
	log.Printf("loaded %d cards into cache", cardCache.Count())

	// Game engine
	gameEngine := engine.NewGameEngine(gameRepo, cardCache)

	// Effect registry
	effectRegistry := effect.NewEffectRegistry()
	effect.RegisterAllEffects(effectRegistry)
	gameEngine.SetEffectRegistry(effectRegistry)

	// Matchmaking (in-memory)
	matchQueue := matchmaking.NewQueue()

	// Services
	playerService := service.NewPlayerService(playerRepo, gameConfigRepo)
	gameService := service.NewGameService(gameEngine, gameRepo, deckRepo, cardCache, matchQueue, playerService)

	// WebSocket manager (must be created before matcher so the callback can reference it)
	wsManager := ws.NewManager(gameService)
	gameService.SetActionObserver(func(gameID, actionType string, actionData json.RawMessage) {
		wsManager.BroadcastActionPerformed(gameID, actionType, actionData)
	})
	gameService.SetBattleEventObserver(func(gameID string, event service.BattleEvent) {
		wsManager.BroadcastBattleEvent(gameID, event)
	})
	wsHandler := ws.NewHandler(wsManager, authClient, playerRepo, cfg.AllowedOrigins)

	// Matchmaking handler: when a match is found, create the game and notify players
	matcher := matchmaking.NewMatcher(matchQueue, func(matchCtx context.Context, result matchmaking.MatchResult) {
		game, err := gameService.CreateGameFromMatch(matchCtx, result)
		if err != nil {
			log.Printf("failed to create game from match: %v", err)
			return
		}

		// Join both players to the game room
		wsManager.JoinGame(result.Player1ID, game.GameID)
		wsManager.JoinGame(result.Player2ID, game.GameID)

		// Notify both players
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

	// Router
	r := gin.Default()
	r.Use(middleware.CORS(cfg.AllowedOrigins...))

	r.GET("/health", func(c *gin.Context) {
		c.JSON(http.StatusOK, gin.H{"status": "ok"})
	})

	// WebSocket endpoint (token verified at connection time)
	r.GET("/ws", wsHandler.HandleUpgrade)

	srv := &http.Server{
		Addr:    ":" + cfg.Port,
		Handler: r,
	}

	srvCtx, stop := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
	defer stop()

	// Start background goroutines
	go matcher.Run(srvCtx)
	go refreshCardCache(srvCtx, cardCache, cardRepo)

	go func() {
		log.Printf("ws server starting on :%s (env=%s)", cfg.Port, cfg.Env)
		if err := srv.ListenAndServe(); err != nil && err != http.ErrServerClosed {
			log.Fatalf("listen: %v", err)
		}
	}()

	<-srvCtx.Done()
	log.Println("shutting down gracefully...")

	shutdownCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	if err := srv.Shutdown(shutdownCtx); err != nil {
		log.Fatalf("server forced to shutdown: %v", err)
	}

	log.Println("ws server exited")
}

// refreshCardCache periodically refreshes the card definition cache.
func refreshCardCache(ctx context.Context, cc *cache.CardCache, repo repository.CardRepo) {
	ticker := time.NewTicker(5 * time.Minute)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			if err := cc.Refresh(ctx, repo); err != nil {
				log.Printf("card cache refresh error: %v", err)
			} else {
				log.Printf("card cache refreshed: %d cards", cc.Count())
			}
		}
	}
}
