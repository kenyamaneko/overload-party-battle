// Package apibattlerpcclient は consumer から wire 詳細 (REST path / HTTP status code) を
// 隠蔽するため、生成 client を sentinel error 変換でラップする。
package apibattlerpcclient

import (
	"context"
	"errors"
	"fmt"
	"net/http"

	apibattle "github.com/kenyamaneko/overload-party-battle/packages/api-battle-rpc-go"
)

// Sentinel errors. wire 契約 (OpenAPI spec) で定義された status code の意味を sentinel として export する。
var (
	// ErrNotFound は status 404。
	ErrNotFound = errors.New("apibattlerpcclient: not found")

	// ErrUnauthorized は status 401。
	ErrUnauthorized = errors.New("apibattlerpcclient: unauthorized")

	// ErrForbidden は status 403。
	ErrForbidden = errors.New("apibattlerpcclient: forbidden")

	// ErrBadRequest は status 400。
	ErrBadRequest = errors.New("apibattlerpcclient: bad request")

	// ErrInternalServer は status 5xx。
	ErrInternalServer = errors.New("apibattlerpcclient: internal server error")
)

// Client は overload-party-battle RPC API の sentinel-error converted client。
type Client struct {
	api *apibattle.ClientWithResponses
}

// Option は Client 構築時の設定。
type Option func(*config)

type config struct {
	httpClient apibattle.HttpRequestDoer
	editors    []apibattle.RequestEditorFn
}

// WithHTTPClient は基底 HTTP doer を差し替える。デフォルトは http.DefaultClient。
func WithHTTPClient(doer apibattle.HttpRequestDoer) Option {
	return func(c *config) { c.httpClient = doer }
}

// WithRequestEditorFn は全リクエストに適用する RequestEditor を追加する。
func WithRequestEditorFn(fn apibattle.RequestEditorFn) Option {
	return func(c *config) { c.editors = append(c.editors, fn) }
}

// New は baseURL に接続する Client を生成する。
func New(baseURL string, opts ...Option) (*Client, error) {
	cfg := &config{}
	for _, opt := range opts {
		opt(cfg)
	}

	apiOpts := make([]apibattle.ClientOption, 0, 1+len(cfg.editors))
	if cfg.httpClient != nil {
		apiOpts = append(apiOpts, apibattle.WithHTTPClient(cfg.httpClient))
	}
	for _, ed := range cfg.editors {
		apiOpts = append(apiOpts, apibattle.WithRequestEditorFn(ed))
	}

	api, err := apibattle.NewClientWithResponses(baseURL, apiOpts...)
	if err != nil {
		return nil, fmt.Errorf("apibattlerpcclient: new: %w", err)
	}
	return &Client{api: api}, nil
}

// GetHealth はサービス死活監視用エンドポイントを叩く。
func (c *Client) GetHealth(ctx context.Context) (*apibattle.HealthResponse, error) {
	resp, err := c.api.GetHealthWithResponse(ctx)
	if err != nil {
		return nil, fmt.Errorf("apibattlerpcclient: GetHealth: %w", err)
	}
	if resp.JSON200 != nil {
		return resp.JSON200, nil
	}
	return nil, statusError("GetHealth", resp.StatusCode())
}

// ListNpcModels は NPC モデル一覧を返す。
func (c *Client) ListNpcModels(ctx context.Context) (*apibattle.NpcModelsResponse, error) {
	resp, err := c.api.ListNpcModelsWithResponse(ctx)
	if err != nil {
		return nil, fmt.Errorf("apibattlerpcclient: ListNpcModels: %w", err)
	}
	if resp.JSON200 != nil {
		return resp.JSON200, nil
	}
	return nil, statusError("ListNpcModels", resp.StatusCode())
}

// ListDevCards は dev 用カード一覧を返す。
func (c *Client) ListDevCards(ctx context.Context) ([]apibattle.DevCard, error) {
	resp, err := c.api.ListDevCardsWithResponse(ctx)
	if err != nil {
		return nil, fmt.Errorf("apibattlerpcclient: ListDevCards: %w", err)
	}
	if resp.JSON200 != nil {
		return *resp.JSON200, nil
	}
	return nil, statusError("ListDevCards", resp.StatusCode())
}

// CreateNpcGame は NPC バトルセッションを作成する。
func (c *Client) CreateNpcGame(ctx context.Context, req apibattle.NpcBattleRequest) (*apibattle.GameCreatedResult, error) {
	resp, err := c.api.CreateNpcGameWithResponse(ctx, apibattle.CreateNpcGameJSONRequestBody(req))
	if err != nil {
		return nil, fmt.Errorf("apibattlerpcclient: CreateNpcGame: %w", err)
	}
	if resp.JSON200 != nil {
		return resp.JSON200, nil
	}
	return nil, statusError("CreateNpcGame", resp.StatusCode())
}

// CreatePvpGame は PvP バトルセッションを作成する。
func (c *Client) CreatePvpGame(ctx context.Context, req apibattle.PvpBattleRequest) (*apibattle.GameCreatedResult, error) {
	resp, err := c.api.CreatePvpGameWithResponse(ctx, apibattle.CreatePvpGameJSONRequestBody(req))
	if err != nil {
		return nil, fmt.Errorf("apibattlerpcclient: CreatePvpGame: %w", err)
	}
	if resp.JSON200 != nil {
		return resp.JSON200, nil
	}
	return nil, statusError("CreatePvpGame", resp.StatusCode())
}

// ProcessGameAction はプレイヤーのゲームアクションを処理する。
func (c *Client) ProcessGameAction(ctx context.Context, gameID string, req apibattle.GameActionRequest) (*apibattle.ActionResult, error) {
	resp, err := c.api.ProcessGameActionWithResponse(ctx, gameID, apibattle.ProcessGameActionJSONRequestBody(req))
	if err != nil {
		return nil, fmt.Errorf("apibattlerpcclient: ProcessGameAction: %w", err)
	}
	if resp.JSON200 != nil {
		return resp.JSON200, nil
	}
	return nil, statusError("ProcessGameAction", resp.StatusCode())
}

// AdvanceNpcTurn は NPC ターンを進める。
func (c *Client) AdvanceNpcTurn(ctx context.Context, gameID string) (*apibattle.ActionResult, error) {
	resp, err := c.api.AdvanceNpcTurnWithResponse(ctx, gameID)
	if err != nil {
		return nil, fmt.Errorf("apibattlerpcclient: AdvanceNpcTurn: %w", err)
	}
	if resp.JSON200 != nil {
		return resp.JSON200, nil
	}
	return nil, statusError("AdvanceNpcTurn", resp.StatusCode())
}

// GetGameStateForPlayer は指定プレイヤー視点のゲーム状態を返す。
func (c *Client) GetGameStateForPlayer(ctx context.Context, gameID string, playerNum int32) (*apibattle.ClientGameState, error) {
	resp, err := c.api.GetGameStateForPlayerWithResponse(ctx, gameID, playerNum)
	if err != nil {
		return nil, fmt.Errorf("apibattlerpcclient: GetGameStateForPlayer: %w", err)
	}
	if resp.JSON200 != nil {
		return resp.JSON200, nil
	}
	return nil, statusError("GetGameStateForPlayer", resp.StatusCode())
}

// GetTurnControlsForPlayer は指定プレイヤーが現在実行可能なアクションを返す。
func (c *Client) GetTurnControlsForPlayer(ctx context.Context, gameID string, playerNum int32) (*apibattle.TurnControlsMessage, error) {
	resp, err := c.api.GetTurnControlsForPlayerWithResponse(ctx, gameID, playerNum)
	if err != nil {
		return nil, fmt.Errorf("apibattlerpcclient: GetTurnControlsForPlayer: %w", err)
	}
	if resp.JSON200 != nil {
		return resp.JSON200, nil
	}
	return nil, statusError("GetTurnControlsForPlayer", resp.StatusCode())
}

// GetGameLogJson はゲームログを JSON 形式で返す。
func (c *Client) GetGameLogJson(ctx context.Context, gameID string) (map[string]interface{}, error) {
	resp, err := c.api.GetGameLogJsonWithResponse(ctx, gameID)
	if err != nil {
		return nil, fmt.Errorf("apibattlerpcclient: GetGameLogJson: %w", err)
	}
	if resp.JSON200 != nil {
		return *resp.JSON200, nil
	}
	return nil, statusError("GetGameLogJson", resp.StatusCode())
}

// GetGameLogText はゲームログを text/plain 形式で返す。spec 上 JSON ではないため raw body を返す。
func (c *Client) GetGameLogText(ctx context.Context, gameID string) ([]byte, error) {
	resp, err := c.api.GetGameLogTextWithResponse(ctx, gameID)
	if err != nil {
		return nil, fmt.Errorf("apibattlerpcclient: GetGameLogText: %w", err)
	}
	if resp.StatusCode() == http.StatusOK {
		return resp.Body, nil
	}
	return nil, statusError("GetGameLogText", resp.StatusCode())
}

// statusError は HTTP status code を sentinel error (errors.Is 分岐可能) に変換する。
func statusError(op string, code int) error {
	var sentinel error
	switch {
	case code == http.StatusUnauthorized:
		sentinel = ErrUnauthorized
	case code == http.StatusForbidden:
		sentinel = ErrForbidden
	case code == http.StatusNotFound:
		sentinel = ErrNotFound
	case code == http.StatusBadRequest:
		sentinel = ErrBadRequest
	case code >= http.StatusInternalServerError:
		sentinel = ErrInternalServer
	default:
		return fmt.Errorf("apibattlerpcclient: %s: unexpected status %d", op, code)
	}
	return fmt.Errorf("apibattlerpcclient: %s: %w", op, sentinel)
}
