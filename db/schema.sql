-- Overload Party Battle Server - PostgreSQL DDL (SSoT for battle-owned tables)
-- ADR-014 に従い battle サービスが所有するテーブルのみをこのファイルで管理する。
--
-- Schema: battle
-- Owner : overload-party-battle
--
-- 備考:
--   - battle は pure engine としてスロット番号 (1/2) のみを扱い、プレイヤー ID を
--     知らない。人間プレイヤー ↔ スロットのマッピング (game_players) と経験値付与は
--     gateway が gateway スキーマで所有する。

-- =============================================================================
-- Schemas
-- =============================================================================

CREATE SCHEMA IF NOT EXISTS battle;

-- =============================================================================
-- Schema-local helpers
-- =============================================================================

CREATE OR REPLACE FUNCTION battle.update_updated_at()
RETURNS TRIGGER AS $$
BEGIN
  NEW.updated_at = now();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- =============================================================================
-- 4.1 Game Management (schema: battle)
-- =============================================================================

CREATE TABLE battle.games (
  game_id              VARCHAR(26) NOT NULL,           -- ULID
  status               VARCHAR(20) NOT NULL,           -- 'waiting' / 'playing' / 'finished'
  first_player         SMALLINT NOT NULL,              -- 先攻プレイヤー番号 (1 or 2)
  winning_player_num   SMALLINT,                       -- NULL=進行中, 0=引分, 1=P1勝, 2=P2勝
  win_reason           TEXT,                           -- 'budget_zero', 'turn_timeout' 等
  engine_version       TEXT NOT NULL DEFAULT '',        -- バトルエンジンバージョン（ゲーム作成時に記録）
  card_data_version    TEXT NOT NULL DEFAULT '',        -- カードデータバージョン（ゲーム作成時に記録）
  created_at           TIMESTAMPTZ NOT NULL DEFAULT now(), -- 作成日時
  updated_at           TIMESTAMPTZ NOT NULL DEFAULT now(), -- 更新日時
  finished_at          TIMESTAMPTZ,                    -- 終了日時
  PRIMARY KEY (game_id)
);

CREATE INDEX idx_games_status ON battle.games(status, created_at DESC);
CREATE TRIGGER trg_games_updated_at BEFORE UPDATE ON battle.games FOR EACH ROW EXECUTE FUNCTION battle.update_updated_at();

-- 4.1a Game NPC Settings (child of games, NPC 戦のみ。PvP では行なし)

CREATE TABLE battle.game_npcs (
  game_id       VARCHAR(26) NOT NULL REFERENCES battle.games(game_id), -- 親テーブル参照
  player_num    SMALLINT NOT NULL,              -- NPC が座っているスロット番号 (1 or 2)
  npc_model     VARCHAR NOT NULL,               -- NPC モデル名
  PRIMARY KEY (game_id, player_num)
);

-- 4.1b Game Decks (child of games, 常に 2 行)

CREATE TABLE battle.game_decks (
  game_id        VARCHAR(26) NOT NULL REFERENCES battle.games(game_id), -- 親テーブル参照
  player_num     SMALLINT NOT NULL,             -- 1 or 2
  deck_snapshot  JSONB NOT NULL,                -- デッキスナップショット
  PRIMARY KEY (game_id, player_num)
);

-- 4.2 Game State (child of games, 1:1)

CREATE TABLE battle.game_states (
  game_id              VARCHAR(26) PRIMARY KEY REFERENCES battle.games(game_id) ON DELETE CASCADE, -- 親テーブル参照
  initial_state        JSONB NOT NULL DEFAULT '{}',  -- ゲーム開始時の初期状態スナップショット（作成後は上書きされない）
  version              BIGINT NOT NULL,              -- 楽観的ロック用バージョン
  current_turn         BIGINT NOT NULL,              -- 現在ターン数
  current_phase        VARCHAR(20) NOT NULL,         -- 'draw' / 'main' / 'battle' / 'end'
  active_player        BIGINT NOT NULL,              -- 現在のターンプレイヤー (1 or 2)
  player1_budget       BIGINT NOT NULL,              -- Player 1 Budget
  player1_insight_pool BIGINT NOT NULL,              -- Player 1 Insight Pool
  player1_field        JSONB NOT NULL,               -- Player 1 フィールド上のカード
  player1_hand         JSONB NOT NULL,               -- Player 1 手札
  player1_repository   JSONB NOT NULL,               -- Player 1 リポジトリ（山札）
  player1_trash        JSONB NOT NULL,               -- Player 1 トラッシュ
  player1_time_bank    BIGINT NOT NULL,              -- Player 1 残り時間
  player2_budget       BIGINT NOT NULL,              -- Player 2 Budget
  player2_insight_pool BIGINT NOT NULL,              -- Player 2 Insight Pool
  player2_field        JSONB NOT NULL,               -- Player 2 フィールド上のカード
  player2_hand         JSONB NOT NULL,               -- Player 2 手札
  player2_repository   JSONB NOT NULL,               -- Player 2 リポジトリ（山札）
  player2_trash        JSONB NOT NULL,               -- Player 2 トラッシュ
  player2_time_bank    BIGINT NOT NULL,              -- Player 2 残り時間
  chain_stack          JSONB,                        -- 現在積まれているチェーンスタック
  current_action_timer BIGINT,                       -- アクションタイマー
  next_instance_seq    BIGINT NOT NULL DEFAULT 0,    -- インスタンスID発番用シーケンス
  updated_at           TIMESTAMPTZ NOT NULL DEFAULT now() -- 更新日時
);
CREATE TRIGGER trg_game_states_updated_at BEFORE UPDATE ON battle.game_states FOR EACH ROW EXECUTE FUNCTION battle.update_updated_at();

-- Game Actions (child of games, append-only action log)

CREATE TABLE battle.game_actions (
  game_id     VARCHAR(26) NOT NULL REFERENCES battle.games(game_id) ON DELETE CASCADE, -- 親テーブル参照
  seq         INT NOT NULL,                          -- アクション連番
  player_num  SMALLINT NOT NULL,                     -- アクション実行プレイヤー番号 (1 or 2)
  action_type TEXT NOT NULL,                         -- アクション種別（play_card, attack, scale_up 等）
  action_data JSONB NOT NULL,                        -- アクションの入力データ
  created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),    -- 記録日時
  PRIMARY KEY (game_id, seq)
);

-- 4.3 Game Events (child of games)

CREATE TABLE battle.game_events (
  game_id         VARCHAR(26) NOT NULL REFERENCES battle.games(game_id) ON DELETE CASCADE, -- 親テーブル参照
  sequence_number BIGINT NOT NULL,                   -- イベント連番
  event_type      VARCHAR(50) NOT NULL,              -- イベント種別
  player_num      SMALLINT,                          -- NULL=system event, 1 or 2=プレイヤーイベント
  event_data      JSONB NOT NULL,                    -- イベント詳細データ
  created_at      TIMESTAMPTZ NOT NULL DEFAULT now(), -- 発生日時
  PRIMARY KEY (game_id, sequence_number)
);
