# overload-party-battle

C# ゲームエンジン。Gateway から HTTP RPC で呼ばれ、NPC / PvP 対戦のゲーム作成・アクション処理・状態管理を行う。カード定義は card が publish したマスターデータから起動時にロードする。

[テスト観点カタログ](https://kenyamaneko.github.io/overload-party-battle/): テスト名から生成した、テスト済みの観点の一覧。

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
  │                               player_summary / game_states /
  │                               game_actions / game_events)
  └─ Cloud Storage (起動時 1 回の cards.json / initiatives.json 取得)
```

- Gateway が唯一の呼び出し元。battle 自身は他のサービスを呼び出さない
- Pub/Sub なし

詳細は [API_REFERENCE.md](docs/API_REFERENCE.md) / [データ設計書](docs/DATA_DESIGN.md) を参照。設計判断 (Why) は [common の ADR](https://github.com/kenyamaneko/overload-party-common/tree/main/docs/adr) に記録する。

## 環境変数

**Deployment env (インフラ層):**

| 変数名 | デフォルト | 説明 |
|---|---|---|
| `PORT` | `9002` | リッスンポート |
| `DATABASE_CONN` / `ConnectionStrings__DefaultConnection` | *(本番必須)* | PostgreSQL 接続文字列 (`battle` スキーマ) |
| `DATABASE_IAM_AUTH_ENABLED` | *(必須)* | `true` なら Cloud SQL の IAM データベース認証で接続し、パスワードの代わりにアクセストークンを供給する。`false` なら接続文字列のパスワードで接続する。未設定と `true` / `false` 以外の値は起動時にエラー。`true` のときは接続文字列に接続ユーザー (`Username`) が要る |
| `MASTER_DATA_BUCKET` | *(必須)* | card が `cards.json` / `initiatives.json` を publish する Cloud Storage バケット。起動時に 1 回読み込む。未設定は起動時にエラー。ローカル開発モードで `CARDS_JSON_PATH` を指定したときは読まない |

**ConfigMap (アプリ挙動):**

| 変数名 | デフォルト | 説明 |
|---|---|---|
| `CARDS_JSON_PATH` | *(空)* | ローカル開発モード（`ASPNETCORE_ENVIRONMENT=Development`）時のみ。バケットの代わりにこの JSON ファイルからカード定義を読み込む |
| `INITIATIVES_JSON_PATH` | *(空)* | ローカル開発モード時に `CARDS_JSON_PATH` と併せて必須。バケットの代わりにこの JSON ファイルから施策定義を読み込む |
| `NPC_AI_CONFIG_DIR` | *(必須)* | NPC AI 設定 YAML ディレクトリ。未設定または非実在パスなら起動時にエラー。コンテナイメージは同梱データを指す `/app/NpcData` を設定済み |

## 同梱するカードマスターデータ

テストとローカル開発の入力として、card のカード定義・施策定義を `packages/game-state-dotnet/cache/` に同梱している。

| ファイル | 内容 |
|---|---|
| `cards_gen.json` | カード定義 |
| `initiatives_gen.json` | 施策定義 |
| `card_source_gen.json` | 上の 2 ファイルの取り込み元 (card のリポジトリ・コミット・コミット日時・取り込み日時) |

`card_source_gen.json` の `commit` は、コピー元にした card のコミットを指す。データを最後に変更したコミットとは限らない。

同梱データを更新するには以下を実行する。3 ファイルすべてが同時に書き換わるため、記録と中身がずれない。

```
make sync-card-master-data
```

card のクローンの位置は `CARD_REPO` (既定 `../overload-party-card`)、取り込むコミットは `CARD_REF` (既定 `origin/main`) で指定する。

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

SSoT: `data/openapi.yaml` + `data/game_logic_constants.yaml` → `bash scripts/generate_types.sh` で再生成。
