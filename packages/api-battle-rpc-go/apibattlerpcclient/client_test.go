package apibattlerpcclient_test

import (
	"context"
	"net/http"
	"net/http/httptest"
	"testing"

	apibattle "github.com/kenyamaneko/overload-party-battle/packages/api-battle-rpc-go"
	"github.com/kenyamaneko/overload-party-battle/packages/api-battle-rpc-go/apibattlerpcclient"
	"github.com/kenyamaneko/overload-party-battle/packages/api-battle-rpc-go/apibattlerpcserverfake"
	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

// 以下の TestClient_<Endpoint>_StatusMapping 群は、SDK の固有責務である
// 「OpenAPI spec で宣言された 4xx/5xx status を sentinel error に変換する」契約を
// endpoint ごとに検証する。
//
// 4xx/5xx 宣言が無い endpoint (ListNpcModels / ListDevCards / GetHealth) は省略。
// GetGameLogJson / GetGameLogText は apibattlerpcserverfake に handler が無く、
// statusError logic は他 endpoint で十分カバーされるため省略。

func TestClient_CreateNpcGame_StatusMapping(t *testing.T) {
	cases := []struct {
		name       string
		status     int
		wantTarget error
	}{
		{
			name:       "400 を受けたとき ErrBadRequest",
			status:     http.StatusBadRequest,
			wantTarget: apibattlerpcclient.ErrBadRequest,
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			srv := apibattlerpcserverfake.NewServer()
			defer srv.Close()
			srv.CreateNpcGameFn = func(_ apibattle.NpcBattleRequest) (int, any) { return tc.status, nil }

			c := newTestClient(t, srv.URL())
			// status mapping 検証のため request body の内容は無関係 (server fake は body を見ず tc.status を返す)。
			_, err := c.CreateNpcGame(context.Background(), apibattle.NpcBattleRequest{})
			assertSentinel(t, err, tc.wantTarget)
		})
	}
}

func TestClient_CreatePvpGame_StatusMapping(t *testing.T) {
	cases := []struct {
		name       string
		status     int
		wantTarget error
	}{
		{
			name:       "400 を受けたとき ErrBadRequest",
			status:     http.StatusBadRequest,
			wantTarget: apibattlerpcclient.ErrBadRequest,
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			srv := apibattlerpcserverfake.NewServer()
			defer srv.Close()
			srv.CreatePvpGameFn = func(_ apibattle.PvpBattleRequest) (int, any) { return tc.status, nil }

			c := newTestClient(t, srv.URL())
			_, err := c.CreatePvpGame(context.Background(), apibattle.PvpBattleRequest{})
			assertSentinel(t, err, tc.wantTarget)
		})
	}
}

func TestClient_ProcessGameAction_StatusMapping(t *testing.T) {
	cases := []struct {
		name       string
		status     int
		wantTarget error
	}{
		{
			name:       "400 を受けたとき ErrBadRequest",
			status:     http.StatusBadRequest,
			wantTarget: apibattlerpcclient.ErrBadRequest,
		},
		{
			name:       "500 を受けたとき ErrInternalServer",
			status:     http.StatusInternalServerError,
			wantTarget: apibattlerpcclient.ErrInternalServer,
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			srv := apibattlerpcserverfake.NewServer()
			defer srv.Close()
			srv.ProcessActionFn = func(_ string, _ apibattle.GameActionRequest) (int, any) { return tc.status, nil }

			c := newTestClient(t, srv.URL())
			_, err := c.ProcessGameAction(context.Background(), "g1", apibattle.GameActionRequest{})
			assertSentinel(t, err, tc.wantTarget)
		})
	}
}

func TestClient_AdvanceNpcTurn_StatusMapping(t *testing.T) {
	cases := []struct {
		name       string
		status     int
		wantTarget error
	}{
		{
			name:       "400 を受けたとき ErrBadRequest",
			status:     http.StatusBadRequest,
			wantTarget: apibattlerpcclient.ErrBadRequest,
		},
		{
			name:       "500 を受けたとき ErrInternalServer",
			status:     http.StatusInternalServerError,
			wantTarget: apibattlerpcclient.ErrInternalServer,
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			srv := apibattlerpcserverfake.NewServer()
			defer srv.Close()
			srv.AdvanceNpcTurnFn = func(_ string) (int, any) { return tc.status, nil }

			c := newTestClient(t, srv.URL())
			_, err := c.AdvanceNpcTurn(context.Background(), "g1")
			assertSentinel(t, err, tc.wantTarget)
		})
	}
}

func TestClient_GetGameStateForPlayer_StatusMapping(t *testing.T) {
	cases := []struct {
		name       string
		status     int
		wantTarget error
	}{
		{
			name:       "500 を受けたとき ErrInternalServer",
			status:     http.StatusInternalServerError,
			wantTarget: apibattlerpcclient.ErrInternalServer,
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			srv := apibattlerpcserverfake.NewServer()
			defer srv.Close()
			srv.GetGameStateFn = func(_ string, _ int) (int, any) { return tc.status, nil }

			c := newTestClient(t, srv.URL())
			_, err := c.GetGameStateForPlayer(context.Background(), "g1", 1)
			assertSentinel(t, err, tc.wantTarget)
		})
	}
}

func TestClient_GetTurnControlsForPlayer_StatusMapping(t *testing.T) {
	cases := []struct {
		name       string
		status     int
		wantTarget error
	}{
		{
			name:       "500 を受けたとき ErrInternalServer",
			status:     http.StatusInternalServerError,
			wantTarget: apibattlerpcclient.ErrInternalServer,
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			srv := apibattlerpcserverfake.NewServer()
			defer srv.Close()
			srv.GetTurnControlsFn = func(_ string, _ int) (int, any) { return tc.status, nil }

			c := newTestClient(t, srv.URL())
			_, err := c.GetTurnControlsForPlayer(context.Background(), "g1", 1)
			assertSentinel(t, err, tc.wantTarget)
		})
	}
}

// TestClient_RequestEditor は Option pattern の契約 (WithRequestEditorFn で渡した
// editor が全リクエストに適用される) を検証する。X-Internal-Auth header 注入の
// 接続点として SDK が機能することを担保する。
func TestClient_RequestEditor(t *testing.T) {
	var gotHeader string
	spy := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		gotHeader = r.Header.Get("X-Internal-Auth")
		w.Header().Set("Content-Type", "application/json")
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write([]byte(`{"models":[]}`))
	}))
	defer spy.Close()

	c, err := apibattlerpcclient.New(spy.URL,
		apibattlerpcclient.WithRequestEditorFn(func(_ context.Context, req *http.Request) error {
			req.Header.Set("X-Internal-Auth", "test-token")
			return nil
		}),
	)
	require.NoError(t, err)

	_, err = c.ListNpcModels(context.Background())
	require.NoError(t, err)
	assert.Equal(t, "test-token", gotHeader)
}

func newTestClient(t *testing.T, baseURL string) *apibattlerpcclient.Client {
	t.Helper()
	c, err := apibattlerpcclient.New(baseURL)
	require.NoError(t, err)
	return c
}

func assertSentinel(t *testing.T, gotErr, wantTarget error) {
	t.Helper()
	require.Error(t, gotErr)
	assert.ErrorIs(t, gotErr, wantTarget)
}
