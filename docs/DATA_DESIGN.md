# battle スキーマ - データ設計

> **DDL の SSoT:** `db/schema.sql`

## 設計概要

battle スキーマはゲームエンジンが管理するバトル進行データを格納する。battle は pure engine としてスロット番号（1/2）のみを扱い、プレイヤー ID を知らない。人間プレイヤー ↔ スロットのマッピングは gateway スキーマの `game_players` が担う。

---

## テーブル構成

### games

ゲーム管理。PvP・NPC 共通で 1 ゲーム = 1 行。

- **PK:** `game_id` (VARCHAR(26), ULID)
- **INDEX:** `idx_games_status` ON `(status, created_at DESC)`
- **TRIGGER:** `updated_at` 自動更新

<!-- BEGIN GENERATED: games -->
| カラム名 | 型 | Nullable | 説明 |
|---|---|---|---|
| `game_id` | VARCHAR(26) | No | ULID |
| `status` | VARCHAR(20) | No | 'waiting' / 'playing' / 'finished' / 'no_game' |
| `first_player` | SMALLINT | No | 先攻プレイヤー番号 (1 or 2) |
| `winning_player_num` | SMALLINT | Yes | NULL=進行中またはノーゲーム(status で判別), 0=引分, 1=P1勝, 2=P2勝 |
| `win_reason` | TEXT | Yes | 'budget_zero', 'turn_timeout' 等 |
| `engine_version` | TEXT | No | バトルエンジンバージョン（ゲーム作成時に記録） |
| `card_data_version` | TEXT | No | カードデータバージョン（ゲーム作成時に記録） |
| `created_at` | TIMESTAMPTZ | No | 作成日時 |
| `updated_at` | TIMESTAMPTZ | No | 更新日時 |
| `finished_at` | TIMESTAMPTZ | Yes | 終了日時 |
<!-- END GENERATED: games -->

---

### game_npcs

NPC 設定。NPC 戦のみ行が存在し、PvP では行なし。

- **PK:** `(game_id, player_num)`
- **FK:** `game_id` → `games(game_id)`

<!-- BEGIN GENERATED: game_npcs -->
| カラム名 | 型 | Nullable | 説明 |
|---|---|---|---|
| `game_id` | VARCHAR(26) | No | 親テーブル参照 |
| `player_num` | SMALLINT | No | NPC が座っているスロット番号 (1 or 2) |
| `npc_model` | VARCHAR | No | NPC モデル名 |
<!-- END GENERATED: game_npcs -->

---

### player_summary

プレイヤー表示用スナップショット。ゲーム作成時に渡された name / level をそのまま保存する (account 同期依存なし)。

- **PK:** `(game_id, player_num)`
- **FK:** `game_id` → `games(game_id) ON DELETE CASCADE`

<!-- BEGIN GENERATED: player_summary -->
| カラム名 | 型 | Nullable | 説明 |
|---|---|---|---|
| `game_id` | VARCHAR(26) | No | 親テーブル参照 |
| `player_num` | SMALLINT | No | 1 or 2 |
| `name` | TEXT | No | battle 開始時点の name snapshot |
| `level` | INT | Yes | battle 開始時点の level snapshot (NPC は NULL) |
| `created_at` | TIMESTAMPTZ | No | 作成日時 |
<!-- END GENERATED: player_summary -->

---

### game_decks

デッキスナップショット。ゲーム作成時に各プレイヤーのデッキを JSONB で保存。常に 2 行。

- **PK:** `(game_id, player_num)`
- **FK:** `game_id` → `games(game_id)`

<!-- BEGIN GENERATED: game_decks -->
| カラム名 | 型 | Nullable | 説明 |
|---|---|---|---|
| `game_id` | VARCHAR(26) | No | 親テーブル参照 |
| `player_num` | SMALLINT | No | 1 or 2 |
| `deck_snapshot` | JSONB | No | デッキスナップショット |
<!-- END GENERATED: game_decks -->

---

### game_states

ゲーム状態。games と 1:1。更新は `SELECT ... FOR UPDATE` の行ロックで排他制御する。

- **PK:** `game_id`
- **FK:** `game_id` → `games(game_id) ON DELETE CASCADE`
- **TRIGGER:** `updated_at` 自動更新

<!-- BEGIN GENERATED: game_states -->
| カラム名 | 型 | Nullable | 説明 |
|---|---|---|---|
| `game_id` | VARCHAR(26) | No | 親テーブル参照 |
| `initial_state` | JSONB | No | ゲーム開始時の初期状態スナップショット（作成後は上書きされない） |
| `version` | BIGINT | No | 更新回数カウンタ（行ロック下で更新ごとに +1） |
| `current_turn` | BIGINT | No | 現在ターン数 |
| `current_phase` | VARCHAR(20) | No | 'draw' / 'main' / 'battle' / 'end' |
| `active_player` | BIGINT | No | 現在のターンプレイヤー (1 or 2) |
| `player1_budget` | BIGINT | No | Player 1 Budget |
| `player1_insight_pool` | BIGINT | No | Player 1 Insight Pool |
| `player1_field` | JSONB | No | Player 1 フィールド上のカード |
| `player1_hand` | JSONB | No | Player 1 手札 |
| `player1_repository` | JSONB | No | Player 1 リポジトリ（山札） |
| `player1_trash` | JSONB | No | Player 1 トラッシュ |
| `player1_time_bank` | BIGINT | No | Player 1 残り時間 |
| `player1_incident_played_this_turn` | BOOLEAN | No | Player 1 がこのターンにインシデントを使用済みか |
| `player1_has_operated` | BOOLEAN | No | Player 1 の稼働実績フラグ |
| `player2_budget` | BIGINT | No | Player 2 Budget |
| `player2_insight_pool` | BIGINT | No | Player 2 Insight Pool |
| `player2_field` | JSONB | No | Player 2 フィールド上のカード |
| `player2_hand` | JSONB | No | Player 2 手札 |
| `player2_repository` | JSONB | No | Player 2 リポジトリ（山札） |
| `player2_trash` | JSONB | No | Player 2 トラッシュ |
| `player2_time_bank` | BIGINT | No | Player 2 残り時間 |
| `player2_incident_played_this_turn` | BOOLEAN | No | Player 2 がこのターンにインシデントを使用済みか |
| `player2_has_operated` | BOOLEAN | No | Player 2 の稼働実績フラグ |
| `current_action_timer` | BIGINT | Yes | アクションタイマー |
| `next_instance_seq` | BIGINT | No | インスタンスID発番用シーケンス |
| `turn_started_at` | TIMESTAMPTZ | No | 現在のターンの開始日時（タイムバンク減算の基準点） |
| `next_deploy_order_seq` | BIGINT | No | デプロイ順発番用シーケンス |
| `pending_slot_selects` | JSONB | No | 効果デプロイのスロット選択待ちキュー |
| `pending_effect_choice` | JSONB | Yes | 効果の選択待ち状態（NULL=待ちなし） |
| `updated_at` | TIMESTAMPTZ | No | 更新日時 |
<!-- END GENERATED: game_states -->

---

### game_actions

アクションログ。append-only。リプレイ再生用。

- **PK:** `(game_id, seq)`
- **FK:** `game_id` → `games(game_id) ON DELETE CASCADE`

<!-- BEGIN GENERATED: game_actions -->
| カラム名 | 型 | Nullable | 説明 |
|---|---|---|---|
| `game_id` | VARCHAR(26) | No | 親テーブル参照 |
| `seq` | INT | No | アクション連番 |
| `player_num` | SMALLINT | No | アクション実行プレイヤー番号 (1 or 2) |
| `action_type` | TEXT | No | アクション種別（play_card, attack, scale_up 等） |
| `action_data` | JSONB | No | アクションの入力データ |
| `created_at` | TIMESTAMPTZ | No | 記録日時 |
<!-- END GENERATED: game_actions -->

---

### game_events

ゲームイベント。エンジンが発行するイベントの永続化。リプレイ・デバッグ用。

- **PK:** `(game_id, sequence_number)`
- **FK:** `game_id` → `games(game_id) ON DELETE CASCADE`

<!-- BEGIN GENERATED: game_events -->
| カラム名 | 型 | Nullable | 説明 |
|---|---|---|---|
| `game_id` | VARCHAR(26) | No | 親テーブル参照 |
| `sequence_number` | BIGINT | No | イベント連番 |
| `event_type` | VARCHAR(50) | No | イベント種別 |
| `player_num` | SMALLINT | Yes | NULL=system event, 1 or 2=プレイヤーイベント |
| `event_data` | JSONB | No | イベント詳細データ |
| `created_at` | TIMESTAMPTZ | No | 発生日時 |
<!-- END GENERATED: game_events -->
