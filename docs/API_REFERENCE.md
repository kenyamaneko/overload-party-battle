# Battle Service API Reference

> 型の SSoT は `data/openapi.yaml`。型テーブル・エンドポイント説明とも手動で同期する。

## 概要

Battle Service は Gateway からのみ呼ばれる内部サービス。クライアントからの直接アクセスはない。エラー時は `{"error": "..."}` 形式で返却する（`GameRuleException` → 400、それ以外 → 500）。

- **Base path:** `/api/v1`
- **認証:** なし（internal。Gateway → Battle はクラスタ内通信）
- **ポート:** 9002（ローカル）/ 9090（k8s）

---

## Endpoints

### `GET /health`

ヘルスチェック。

**レスポンス:** `{"status": "ok"}`

---

### `GET /api/v1/npc/models`

NPC モデル一覧を返す。

**レスポンス:**

```json
{
  "models": [
    {"model": "she_easy", "faction": "she", "difficulty": "easy", "display_name": "..."},
    ...
  ]
}
```

---

### `POST /api/v1/games/npc`

NPC 戦を作成する。

**リクエスト:** `NpcBattleRequest`

<!-- BEGIN GENERATED: NpcBattleRequest -->
| フィールド | 型 | JSON | 説明 |
|---|---|---|---|
| `DeckCards` | `[]BattleDeckCard` | `deck_cards` |  |
| `NpcModel` | `string` | `npc_model` |  |
| `Player1Summary` | `PlayerSummaryRequest` | `player1_summary` | 人間プレイヤーの表示用スナップショット |
| `Player2Summary` | `PlayerSummaryRequest` | `player2_summary` | NPC の表示用スナップショット (level は null) |
<!-- END GENERATED: NpcBattleRequest -->

**レスポンス:** `GameCreatedResult`

<!-- BEGIN GENERATED: GameCreatedResult -->
| フィールド | 型 | JSON | 説明 |
|---|---|---|---|
| `GameID` | `string` | `game_id` |  |
<!-- END GENERATED: GameCreatedResult -->

---

### `POST /api/v1/games/pvp`

PvP 戦を作成する。マッチメイキング後に Gateway が呼び出す。

**リクエスト:** `PvpBattleRequest`

<!-- BEGIN GENERATED: PvpBattleRequest -->
| フィールド | 型 | JSON | 説明 |
|---|---|---|---|
| `Deck1Cards` | `[]BattleDeckCard` | `deck1_cards` |  |
| `Deck2Cards` | `[]BattleDeckCard` | `deck2_cards` |  |
| `Player1Summary` | `PlayerSummaryRequest` | `player1_summary` | Player 1 の表示用スナップショット |
| `Player2Summary` | `PlayerSummaryRequest` | `player2_summary` | Player 2 の表示用スナップショット |
<!-- END GENERATED: PvpBattleRequest -->

**レスポンス:** `GameCreatedResult`（同上）

---

### `POST /api/v1/games/{gameId}/actions`

ゲームアクションを実行する。

**リクエスト:** `GameActionRequest`

<!-- BEGIN GENERATED: GameActionRequest -->
| フィールド | 型 | JSON | 説明 |
|---|---|---|---|
| `PlayerNum` | `int64` | `player_num` |  |
| `ActionType` | `string` | `action_type` |  |
| `Data` | `json.RawMessage` | `data` |  |
<!-- END GENERATED: GameActionRequest -->

**ActionType 一覧:**

| action_type | data 内容 | 説明 |
|---|---|---|
| `play_card` | `{cardInstanceId, zone, index, targetInstanceId?, choiceData?}` | カードをフィールドに配置（`position: {zone, index}` のネスト形式も受理） |
| `attack` | `{attackerInstanceId, targetInstanceId}` | リソースで攻撃 |
| `scale_up` | `{instanceId, targetRank, instanceFamily?}` | リソースをスケールアップ |
| `monetize` | `{distributions: [{instanceId, amount}]}` | Insight をバックエンドのコンピュートへ配分して収益化 |
| `use_ignition` | `{instanceId, targetInstanceId?, choiceData?}` | カードの起動効果を発動 |
| `use_initiative` | `{kind, choiceData?}` | プロダクトの施策（ルーチン / スペシャル）を発動 |
| `discard_hand` | `{cardInstanceIds}` | 手札を破棄（end フェーズ、手札 > 6 枚時） |
| `end_phase` | `{}` | フェーズを終了 |
| `forfeit` | `{reason}` | 降参 |
| `forfeit_both` | `{}` | 両者投了。勝者なし・理由 `disconnect` で終了（`player_num` は無視される） |
| `select_slot` | `{zone, index}` | 効果デプロイのスロット選択に応答 |
| `resolve_pending_choice` | `{chosen_id}` | 効果処理中の選択を解決 |

**レスポンス:** `ActionResult`

<!-- BEGIN GENERATED: ActionResult -->
| フィールド | 型 | JSON | 説明 |
|---|---|---|---|
| `GameOver` | `bool` | `game_over` | ゲーム終了フラグ |
| `WinningPlayerNum` | `int64` | `winning_player_num` | 勝者プレイヤー番号（1 or 2、未終了時は 0） |
| `WinReason` | `string` | `win_reason` | 終了理由（WinReasons enum の文字列、未終了時は空文字） |
| `NpcPending` | `bool` | `npc_pending` | true の場合、Gateway は AdvanceNpcTurn を呼んで次の NPC アクションを取得する必要がある |
| `Events` | `[]ActionEvent` | `events` | このアクションで発生したイベント列 |
<!-- END GENERATED: ActionResult -->

<!-- BEGIN GENERATED: ActionEvent -->
| フィールド | 型 | JSON | 説明 |
|---|---|---|---|
| `Sequence` | `int64` | `sequence` |  |
| `EventType` | `string` | `event_type` |  |
| `PlayerNum` | `*int64` | `player_num` | アクション元のスロット番号（1 or 2）。system event (turn_start) では null |
| `EventData` | `json.RawMessage` | `event_data` |  |
| `State` | `json.RawMessage` | `state` |  |
<!-- END GENERATED: ActionEvent -->

---

### `POST /api/v1/games/{gameId}/advance-npc`

NPC のターンを進行させる。NPC 戦で `game_enter` 後に Gateway が呼び出す。

**リクエスト:** ボディなし

**レスポンス:** `ActionResult`（同上）

---

### `GET /api/v1/games/{gameId}/state/{playerNum}`

指定プレイヤー視点のゲーム状態を取得する。相手の手札など秘匿情報は隠蔽済み。

**レスポンス:** `ClientGameState`

<!-- BEGIN GENERATED: ClientGameState -->
| フィールド | 型 | JSON | 説明 |
|---|---|---|---|
| `GameID` | `string` | `gameID` | ゲームID（ULID） |
| `CurrentTurn` | `int64` | `currentTurn` | 現在のターン番号 |
| `CurrentPhase` | `string` | `currentPhase` | 現在のフェーズ（`selecting` / `draw` / `yield` / `main` / `battle` / `end`） |
| `ActivePlayer` | `int64` | `activePlayer` | アクティブプレイヤー番号（1 or 2） |
| `IsMyTurn` | `bool` | `isMyTurn` | 自分のターンか |
| `TurnStartedAt` | `time.Time` | `turnStartedAt` | ターン開始日時 |
| `MyView` | `PlayerView` | `myView` | 自分の視点 |
| `OppView` | `OpponentView` | `oppView` | 相手の視点（情報秘匿適用済み） |
<!-- END GENERATED: ClientGameState -->

---

### `GET /api/v1/games/{gameId}/controls/{playerNum}`

ターン制御情報を取得する。フェーズ終了可否・手札破棄枚数を返す。

**レスポンス:** `TurnControlsMessage`

<!-- BEGIN GENERATED: TurnControlsMessage -->
| フィールド | 型 | JSON | 説明 |
|---|---|---|---|
| `CanEndPhase` | `bool` | `canEndPhase` | 現在のフェーズを終了できるか（main / battle フェーズで `true`） |
| `DiscardRequired` | `int` | `discardRequired` | 手札破棄が必要な枚数（end フェーズで手札 > 6 枚の場合のみ > 0） |
<!-- END GENERATED: TurnControlsMessage -->

---

### `GET /api/v1/games/{gameId}/log`

ゲームログ（JSON 形式）。リプレイ用。

**レスポンス:** `application/json`

---

### `GET /api/v1/games/{gameId}/log/text`

ゲームログ（テキスト形式）。デバッグ用。

**レスポンス:** `text/plain`

---

## 共通型

### BattleDeckCard

<!-- BEGIN GENERATED: BattleDeckCard -->
| フィールド | 型 | JSON | 説明 |
|---|---|---|---|
| `CardID` | `string` | `card_id` |  |
| `ArtNo` | `int64` | `art_no` |  |
<!-- END GENERATED: BattleDeckCard -->

---

### PlayerSummaryRequest

ゲーム作成時に battle へ渡す player の name / level スナップショット。battle は upstream に依存せず、渡された値を `player_summary` テーブルへ永続化する。

<!-- BEGIN GENERATED: PlayerSummaryRequest -->
| フィールド | 型 | JSON | 説明 |
|---|---|---|---|
| `Name` | `string` | `name` | 表示名スナップショット |
| `Level` | `*int64` | `level` | level スナップショット (NPC など level を持たない player では null) |
<!-- END GENERATED: PlayerSummaryRequest -->

---

## 開発用エンドポイント（ローカル開発モードのみ）

### `GET /api/dev/cards`

カードキャッシュの一覧を返す。ローカル開発モード（`ASPNETCORE_ENVIRONMENT=Development`）時のみ有効。
