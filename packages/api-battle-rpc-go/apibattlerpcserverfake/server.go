// Package apibattlerpcserverfake は battle の HTTP 契約を実装する httptest.Server ラッパー。
// 各 endpoint は Fn field (func callback) で応答を制御し、Fn=nil なら既定値を返す。
//
// 用途: gateway やその他 consumer が battle へ HTTP RPC する経路を、テスト時に
// httptest.Server で差し替えるためのドライバ。
package apibattlerpcserverfake

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"sync"

	apibattle "github.com/kenyamaneko/overload-party-battle/packages/api-battle-rpc-go"
)

// NpcModelsResponse は GET /api/v1/npc/models の JSON envelope。
type NpcModelsResponse = apibattle.NpcModelsResponse

// Server は battle HTTP 契約を実装する httptest.Server wrapper。
type Server struct {
	mu  sync.Mutex
	srv *httptest.Server

	// HealthFn は GET /health の応答を決定する (nil は 200 {"status":"ok"})。
	HealthFn func() (int, any)

	// ListNpcModelsFn は GET /api/v1/npc/models の応答を決定する (nil は 200 + 空 models)。
	ListNpcModelsFn func() (int, any)

	// CreateNpcGameFn は POST /api/v1/games/npc の応答を決定する (nil は 200 + 空 GameID)。
	CreateNpcGameFn func(req apibattle.NpcBattleRequest) (int, any)

	// CreatePvpGameFn は POST /api/v1/games/pvp の応答を決定する (nil は 200 + 空 GameID)。
	CreatePvpGameFn func(req apibattle.PvpBattleRequest) (int, any)

	// ProcessActionFn は POST /api/v1/games/{gameID}/actions の応答を決定する (nil は 200 + 空 ActionResult)。
	ProcessActionFn func(gameID string, req apibattle.GameActionRequest) (int, any)

	// AdvanceNpcTurnFn は POST /api/v1/games/{gameID}/advance-npc の応答を決定する (nil は 200 + 空 ActionResult)。
	AdvanceNpcTurnFn func(gameID string) (int, any)

	// GetGameStateFn は GET /api/v1/games/{gameID}/state/{playerNum} の応答を決定する (nil は 200 + 空 ClientGameState)。
	GetGameStateFn func(gameID string, playerNum int) (int, any)

	// GetTurnControlsFn は GET /api/v1/games/{gameID}/controls/{playerNum} の応答を決定する (nil は 200 + 空 TurnControlsMessage)。
	GetTurnControlsFn func(gameID string, playerNum int) (int, any)
}

// NewServer は起動済み Server を返す。テスト終了時に Close() すること。
func NewServer() *Server {
	s := &Server{}
	mux := http.NewServeMux()
	mux.HandleFunc("GET /health", s.handleHealth)
	mux.HandleFunc("GET /api/v1/npc/models", s.handleListNpcModels)
	mux.HandleFunc("POST /api/v1/games/npc", s.handleCreateNpcGame)
	mux.HandleFunc("POST /api/v1/games/pvp", s.handleCreatePvpGame)
	mux.HandleFunc("POST /api/v1/games/{gameID}/actions", s.handleProcessAction)
	mux.HandleFunc("POST /api/v1/games/{gameID}/advance-npc", s.handleAdvanceNpcTurn)
	mux.HandleFunc("GET /api/v1/games/{gameID}/state/{playerNum}", s.handleGetGameState)
	mux.HandleFunc("GET /api/v1/games/{gameID}/controls/{playerNum}", s.handleGetTurnControls)
	s.srv = httptest.NewServer(mux)
	return s
}

// URL は httptest.Server のベース URL を返す。
func (s *Server) URL() string { return s.srv.URL }

// Close は内部 httptest.Server を閉じる。
func (s *Server) Close() { s.srv.Close() }

func (s *Server) handleHealth(w http.ResponseWriter, _ *http.Request) {
	s.mu.Lock()
	fn := s.HealthFn
	s.mu.Unlock()

	if fn == nil {
		writeJSON(w, http.StatusOK, apibattle.HealthResponse{Status: "ok"})
		return
	}
	status, body := fn()
	writeJSON(w, status, body)
}

func (s *Server) handleListNpcModels(w http.ResponseWriter, _ *http.Request) {
	s.mu.Lock()
	fn := s.ListNpcModelsFn
	s.mu.Unlock()

	if fn == nil {
		writeJSON(w, http.StatusOK, apibattle.NpcModelsResponse{Models: []apibattle.NpcModelEntry{}})
		return
	}
	status, body := fn()
	writeJSON(w, status, body)
}

func (s *Server) handleCreateNpcGame(w http.ResponseWriter, r *http.Request) {
	var req apibattle.NpcBattleRequest
	_ = json.NewDecoder(r.Body).Decode(&req)

	s.mu.Lock()
	fn := s.CreateNpcGameFn
	s.mu.Unlock()

	if fn == nil {
		writeJSON(w, http.StatusOK, apibattle.GameCreatedResult{})
		return
	}
	status, body := fn(req)
	writeJSON(w, status, body)
}

func (s *Server) handleCreatePvpGame(w http.ResponseWriter, r *http.Request) {
	var req apibattle.PvpBattleRequest
	_ = json.NewDecoder(r.Body).Decode(&req)

	s.mu.Lock()
	fn := s.CreatePvpGameFn
	s.mu.Unlock()

	if fn == nil {
		writeJSON(w, http.StatusOK, apibattle.GameCreatedResult{})
		return
	}
	status, body := fn(req)
	writeJSON(w, status, body)
}

func (s *Server) handleProcessAction(w http.ResponseWriter, r *http.Request) {
	var req apibattle.GameActionRequest
	_ = json.NewDecoder(r.Body).Decode(&req)

	s.mu.Lock()
	fn := s.ProcessActionFn
	s.mu.Unlock()

	gameID := r.PathValue("gameID")
	if fn == nil {
		writeJSON(w, http.StatusOK, apibattle.ActionResult{Events: []apibattle.ActionEvent{}})
		return
	}
	status, body := fn(gameID, req)
	writeJSON(w, status, body)
}

func (s *Server) handleAdvanceNpcTurn(w http.ResponseWriter, r *http.Request) {
	s.mu.Lock()
	fn := s.AdvanceNpcTurnFn
	s.mu.Unlock()

	gameID := r.PathValue("gameID")
	if fn == nil {
		writeJSON(w, http.StatusOK, apibattle.ActionResult{Events: []apibattle.ActionEvent{}})
		return
	}
	status, body := fn(gameID)
	writeJSON(w, status, body)
}

func (s *Server) handleGetGameState(w http.ResponseWriter, r *http.Request) {
	s.mu.Lock()
	fn := s.GetGameStateFn
	s.mu.Unlock()

	gameID := r.PathValue("gameID")
	playerNum := atoiOrZero(r.PathValue("playerNum"))
	if fn == nil {
		writeJSON(w, http.StatusOK, apibattle.ClientGameState{})
		return
	}
	status, body := fn(gameID, playerNum)
	writeJSON(w, status, body)
}

func (s *Server) handleGetTurnControls(w http.ResponseWriter, r *http.Request) {
	s.mu.Lock()
	fn := s.GetTurnControlsFn
	s.mu.Unlock()

	gameID := r.PathValue("gameID")
	playerNum := atoiOrZero(r.PathValue("playerNum"))
	if fn == nil {
		writeJSON(w, http.StatusOK, apibattle.TurnControlsMessage{})
		return
	}
	status, body := fn(gameID, playerNum)
	writeJSON(w, status, body)
}

func writeJSON(w http.ResponseWriter, status int, body any) {
	if body == nil {
		w.WriteHeader(status)
		return
	}
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(body)
}

func atoiOrZero(s string) int {
	n := 0
	for _, ch := range s {
		if ch < '0' || ch > '9' {
			return 0
		}
		n = n*10 + int(ch-'0')
	}
	return n
}
