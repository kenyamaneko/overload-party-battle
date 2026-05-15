# Battle サービス設計

サービスの概要・エンドポイント・環境変数は [README.md](../README.md) を参照。

---

## 1. ゲームロジック

### 1.1 ターン管理

**フェーズ順序:**

| フェーズ | 内容 |
|------|------|
| `draw` | リポジトリから手札に1枚ドロー |
| `yield` | バックエンドリソースのInsight生成処理 |
| `main` | カードプレイ・スケールアップ・アタッチメント等 |
| `battle` | 攻撃実行 |
| `end` | エンドフェーズ処理、ターン切り替え |

**フェーズ進行フロー:**

```
draw → yield → main → battle → end → (ActivePlayer切替) → draw ...
```

**エンドフェーズの詳細手順:**

実装は `EndPhaseProcessor.ProcessEndPhaseLogic` (src/OverloadParty.Battle.Engine/Processors/EndPhaseProcessor.cs) 参照。

| 手順 | 処理 | 実装関数 / 備考 |
|------|------|------|
| 1 | Passive / OnEndPhase 効果の発火 | `FirePassiveEffects` — フィールドのカードを `DeployOrder` 昇順で走査し、`TriggerType.Passive` / `OnEndPhase` ハンドラを実行 |
| 2 | 維持費徴収 | `CollectMaintenanceCost` — 全表向きリソースの維持費を合算し budget から減算（Elastic カードは `BaseThroughput/Yield * RankMultiplier + ElasticBonus` を超過した分のみ従量課金） |
| 3 | Insight 生成 & Elastic ボーナス累積 | `GenerateInsight` — バックエンドの Data 系リソースが `StatCalculator.CalculateEffectiveInsight` で yield を計算し Insight プールに加算。続けて `StatCalculator.ApplyElasticBonus` で `ElasticBonus` を `elasticIncrement` ぶん**累積**（リセットではない。逓減は `EffectiveElasticBonus` が対数スケールで処理） |
| 4 | 一時効果の終了 | `ExpireTemporaryEffects` — `duration: "this_turn"` / `"until_next_own_turn_end"` の `TemporaryEffects` を除去 |
| 5 | ターン単位フラグのリセット | `ResetPerTurnFlags` — `HasAttacked` / `EffectUsedThisTurn` / `MonetizedAmount` / `IncidentPlayedThisTurn` を false/0 に戻す |
| 6 | 手札上限チェック | 手札が **6枚** を超過している場合、サーバーが `discard_prompt` を送信（後続 7–8 は破棄完了後に実行）|
| 7 | プレイヤーが破棄カードを選択 | クライアントが `discard_hand` で破棄するカードを送信（15秒タイムアウト）。タイムアウト時は手札の末尾から自動的に破棄（古い順）|
| 8 | ターン切り替え | `WinConditionChecker.CheckLaunchFailure` → 問題なければ `TurnManager.SwitchActivePlayer` → 次プレイヤーの `DrawPhaseProcessor.Process` を起動 |

> Note: 旧バージョンのドキュメントには「Elastic 値のリセット」手順が存在したが、実装上 `ElasticBonus` は毎ターン累積する設計（逓減は `StatCalculator.EffectiveElasticBonus` の対数スケーリングで表現）のため、リセットステップは存在しない。

### 1.2 チェーン解決

**解決アルゴリズム:**

| 項目 | 内容 |
|------|------|
| 解決順序 | LIFO（スタックの逆順） |
| アクションタイプ | `attack` / `component_effect` / `reactive` |
| 解決後 | 解決済みエントリをクリア |

### 1.3 効果計算

**リソーススタッツ計算の優先順序:**

| 優先度 | 適用内容 |
|--------|----------|
| 1 | ベース値（カード定義） |
| 2 | Rank倍率（small / medium / large） |
| 3 | Instance Family補正（M / C / R） |
| 4 | Platformカードの効果 |
| 5 | Attachmentの効果 |
| 6 | 一時効果（そのターンのみ） |
| 7 | 現在AV = MaxAV − ダメージ蔓積量 |

### 1.4 Available Actions と NPC AI 統合

**Available Actions（Master Duel 方式）:**

サーバーが `ComputeAvailableActions()` でフェーズごとの有効アクションを計算し、クライアントとNPC AIの両方に提供する。

| 項目 | 内容 |
|------|------|
| 計算タイミング | 状態更新ごと（Battle Server）、NPC ターン開始時（`GameService`） |
| 関数 | `Engine.ComputeAvailableActions(state, game, playerNum, ...)` |
| 戻り値 | `List<AvailableAction>`（タイプ別 discriminated union） |
| クライアント向け | `ClientGameState.my.available_actions` に含めて Gateway 経由で WebSocket 送信 |
| NPC 向け | `RunNpcTurnIfNeeded` 内で計算し Strategy に渡す |

**AvailableAction のアクションタイプ:**

| Type | 主要フィールド |
|------|---------------|
| `play_card` | `HandInstanceID`, `CardID`, `ValidZones`, `ValidTargets`, `Cost` |
| `attack` | `SourceInstanceID`, `ValidTargets` |
| `scale_up` | `SourceInstanceID`, `Cost`, `TargetRank`, `NeedsFamily`, `RequiredCount` |
| `monetize` | `SourceInstanceID`, `RemainingCapacity` |
| `use_effect` | `SourceInstanceID`, `ValidTargets`, `EffectTargetType` |
| `set_reactive` | `SourceInstanceID` |

ゲームフロー制御（フェーズ終了、手札破棄）は `available_actions` に含めず、`turn_controls` メッセージとして別途送信される。

**NPC AI アーキテクチャ（Battle Server / C#）:**

```
Engine.ComputeAvailableActions()
        │
        ▼
┌─────────────────────┐
│  IStrategy interface │  DecideMainPhaseActions(state, game, playerNum, available)
│                      │  DecideBattlePhaseActions(state, game, playerNum, available)
│                      │  DecideDiscard(state, playerNum)
│                      │  DecideStartingResources(deckCards)
└────────┬────────────┘
         │
    ┌────┴────┐
    ▼         ▼
StandardAi   FactionAi (SHE / Tenki / Sugar / Tuners)
```

NPC は `List<AvailableAction>` から最適なアクションを選択するのみ。
アクションの有効性判定はすべて Engine 側が担当し、ロジック重複を排除。

**NPC の決定フロー（Main Phase）:**

| 順序 | 処理 | ヘルパー |
|------|------|---------|
| 1 | Strategy/Incident カードを使用 | `DoImmediateActions()` — `EvaluateCard` でスコアリング |
| 2 | Resource カードをデプロイ | `DoDeployActions()` — `PickBestZone` でゾーン選択 |
| 3 | フィールドエフェクトを発動 | `DecideActivateActions()` — `SelectTargetFromValid` でターゲット制約 |
| 4 | スケールアップ | `DoScaleUpActions()` — `AvailableAction.Cost` / `TargetRank` を使用 |
| 5 | Insight 配分 | `DoDistributeYieldActions()` — `RemainingCapacity` で greedy 配分 |
| 6 | フェーズ終了 | `MakeEndPhaseAction()` |

**NPC 関連ファイル（battle リポ: `src/OverloadParty.Battle.Npc/`）:**

| ファイル | 役割 |
|---------|------|
| `StandardAi.cs` | IStrategy インターフェース、StandardAI 実装 |
| `FactionAi.cs` | FactionAI（陣営別パラメータ・オーバーライド） |
| `ActionFilter.cs` | AvailableAction 用ヘルパー（`FilterByType`, `PickBestZone` 等） |
| `ActionEvaluator.cs` | カードスコアリング、ターゲット選択ヒューリスティクス |
| `Targeting.cs` | ターゲット選択戦略（`WeakestInZone`, `StrongestInZone` 等） |
| `NpcDecks.cs` | 陣営別デッキ定義 |

---

## 2. 状態管理

### 2.1 楽観的ロック

| 項目 | 内容 |
|------|------|
| メカニズム | `GameStates.version` フィールドで楽観的ロック |
| 更新手順 | 読み取り→更新処理→version++→書き込み（トランザクション内） |
| 競合時 | PostgreSQL の SELECT FOR UPDATE で排他制御。競合時は自動リトライ |
| 利点 | デッドロックなし、コンフリクト時のみリトライ |

### 2.2 イベントソーシング

| 項目 | 内容 |
|------|------|
| 目的 | リプレイ機能・デバッグ用に全アクションを記録 |
| テーブル | `GameEvents`（追記のみ） |
| リプレイ方法 | 初期状態からイベントを順番に適用 |
| リプレイクエリ | `sequence_number ASC` 順に取得 |

### 2.3 トランザクション失敗時のフィードバック

PostgreSQL トランザクションが失敗した場合、クライアントに `action_rejected` メッセージを返す。

**Server → Client メッセージ:**

```json
{
  "type": "action_rejected",
  "originalAction": "attack",
  "errorCode": "CONFLICT",
  "message": "State conflict detected",
  "retryable": true
}
```

**エラーコード一覧:**

| errorCode | 意味 | retryable |
|-----------|------|-----------|
| `CONFLICT` | 楽観的ロックの競合（他プレイヤーが先に状態を更新） | `true` |
| `TIMEOUT` | トランザクションタイムアウト | `true` |
| `INVALID_STATE` | 状態不整合（フェーズ遷移済み等） | `false` |
| `INTERNAL_ERROR` | 内部エラー | `false` |

**リトライ方式:**

| レイヤー | リトライ回数 | 説明 |
|---------|------------|------|
| サーバー側 | 最大3回 | pgxpool によるトランザクション自動リトライ |
| クライアント側 | 最大2回 | `retryable: true` の場合、指数バックオフで自動リトライ |

> サーバー側で3回リトライしても失敗した場合にのみ `action_rejected` をクライアントに送信する。

---


---

## 3. アクション検証

全アクションで以下の **統一された検証順序** を適用する。各ステップで不正があれば即座にエラーを返し、後続の検証は行わない。

**統一検証順序:**

| 順序 | 検証カテゴリ | 説明 |
|------|------------|------|
| 1 | **フェーズ確認** | 現在のフェーズでそのアクションが許可されているか |
| 2 | **実行元カード確認** | 指定されたカードが手札またはフィールドに存在し、プレイヤーの所有か |
| 3 | **対象確認** | 攻撃対象・効果対象が有効か（存在する、対象に取れる等） |
| 4 | **コスト確認** | Budget・Insight Pool 等のリソースが足りているか |
| 5 | **その他の条件** | 1ターン1回制限、Resizable属性、手札上限など個別ルール |

**アクション別の検証項目:**

| アクション | 1. フェーズ | 2. 実行元 | 3. 対象 | 4. コスト | 5. その他 |
|-----------|-----------|----------|---------|----------|----------|
| `play_card` | Main Phase | 手札に存在 | 配置先が空き | — | デプロイターン 0 なら即表向き、1以上なら裏向き配置 |
| `attack` | Battle Phase | フィールド上の自コンピュート（表向き） | 相手フィールド上の表向きリソース | — | 攻撃済みでない |
| `scale_up` | Main Phase | フィールド上の自リソース（表向き） | — | — | Resizable 属性、現在Rank < 対象Rank |
| `monetize` | Main Phase | バックエンドのコンピュート | — | — | Insight Pool 残量 ≥ 分配量、TP上限 |
| `use_effect` | Main/Battle Phase | 効果を持つカード | 効果の対象 | 効果コスト | 1ターン1回制限 |

## 4. 実装規約

### Card 取得失敗時の fail-fast 戦略

card service からの Card 取得に失敗した場合、リトライせず `Environment.Exit(1)` で即死し、k8s の再起動に任せる。プロセス内のリトライバックオフを実装すると k8s の指数バックオフと二重化するため、cluster 側の再起動戦略を活用する。

### 既存ヘルパーの再利用

フィールド走査には `FieldHelpers.AllFaceUpResources` / `AllResources` / `TargetSelector.FaceUpInZone` を再利用する。同じパターンの for ループを新たに書かない。

### csproj 依存の集約

共通依存は最浅の csproj に一度だけ宣言し、transitive resolution で各層へ伝播させる。各 csproj が同じ依存を再列挙しない。追加・削除箇所とバージョン指定を 1 箇所に保ち、層をまたいだ同期漏れを防ぐ。

