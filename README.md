# overload-party-battle

C# ゲームエンジン。Gateway から HTTP RPC で呼ばれ、NPC / PvP 対戦のゲーム作成・アクション処理・状態管理を行う。カード定義は card service から起動時にロードする。

## サービス間連携

```
Gateway (:9001)
  ├─ POST /api/v1/games/npc          ← NPC 対戦作成
  ├─ POST /api/v1/games/pvp          ← PvP 対戦作成
  ├─ POST /api/v1/games/{id}/actions ← アクション実行
  ├─ POST /api/v1/games/{id}/advance-npc ← NPC ターン進行
  ├─ GET  /api/v1/games/{id}/state/{n}   ← プレイヤー n の状態取得
  ├─ GET  /api/v1/games/{id}/controls/{n} ← ターン制御取得
  ├─ GET  /api/v1/games/{id}/log     ← ゲームログ (JSON)
  ├─ GET  /api/v1/games/{id}/log/text ← ゲームログ (テキスト)
  └─ GET  /api/v1/npc/models         ← NPC モデル一覧
              │
              ▼
Battle (このサービス, :9002)
  ├─ PostgreSQL  battle スキーマ (games / game_npcs / game_decks /
  │                               game_players / game_states /
  │                               game_actions / game_events)
  └─ Card Service (:9003, 起動時 1 回の GET /internal/v1/cards)
```

- Gateway が唯一の呼び出し元。battle 自身は外部サービスを呼び出さない (card service への起動時フェッチを除く)
- Pub/Sub なし

内部設計は [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) を参照。

## 環境変数

**Deployment env (インフラ層):**

| 変数名 | デフォルト | 説明 |
|---|---|---|
| `PORT` | `9002` | リッスンポート |
| `DATABASE_URL` / `ConnectionStrings__DefaultConnection` | *(本番必須)* | PostgreSQL 接続文字列 (`battle` スキーマ) |

**ConfigMap (サービス URL):**

| 変数名 | デフォルト | 説明 |
|---|---|---|
| `CARD_SERVICE_URL` | `http://card:9003` | Card Service ベース URL (起動時カードロード先) |

**ConfigMap (アプリ挙動):**

| 変数名 | デフォルト | 説明 |
|---|---|---|
| `CARDS_JSON_PATH` | *(空)* | ローカル開発モード（`ASPNETCORE_ENVIRONMENT=Development`）時のみ。card service の代わりにこの JSON ファイルからカード定義を読み込む |
| `NPC_AI_CONFIG_DIR` | `src/OverloadParty.Battle.Npc/Data` | NPC AI 設定 YAML ディレクトリ |

## 公開パッケージ

| パッケージ | 言語 | 説明 |
|---|---|---|
| `packages/api-battle-rpc-go/` | Go | gateway が import する RPC 型 |
| `packages/api-battle-rpc-dotnet/` | NuGet | Battle 内部で使う RPC 型 |
| `packages/game-state-dotnet/` | NuGet | ゲーム状態型 |
| `packages/game-state-npm/` | npm | クライアント向けゲーム状態型 |
| `packages/game-logic-constants-go/` | Go | ゲームロジック定数 |
| `packages/game-logic-constants-dotnet/` | NuGet | ゲームロジック定数 |
| `packages/game-logic-constants-npm/` | npm | ゲームロジック定数 |

SSoT: `data/models.yaml` + `data/game_logic_constants.yaml` + `data/event_schemas.yaml` → `python3 scripts/generate_types.py` で再生成。
