# Battle サービス設計

サービスの概要・エンドポイント・環境変数は [README.md](../README.md) を参照。

---

## ゲームロジック

### ターン管理

**フェーズ順序:**

| フェーズ | 内容 |
|------|------|
| `draw` | デッキアウト判定 → 手札に1枚ドロー → デプロイターン経過処理 |
| `main` | カードプレイ・スケールアップ・アタッチメント等 |
| `battle` | 攻撃実行 |
| `end` | エンドフェーズ処理、ターン切り替え |

**フェーズ進行フロー:**

```
draw → main → battle → end → (ActivePlayer切替) → draw ...
```

**エンドフェーズの詳細手順:**

実装は `EndPhaseProcessor.ProcessEndPhaseLogic` (src/OverloadParty.Battle.Engine/Processors/EndPhaseProcessor.cs) 参照。

| 手順 | 処理 | 実装関数 / 備考 |
|------|------|------|
| 1 | Passive / OnEndPhase 効果の発火 | `FirePassiveEffects`：フィールドのカードを `DeployOrder` 昇順で走査し、`TriggerType.Passive` / `OnEndPhase` ハンドラを実行 |
| 2 | 維持コスト徴収 | `CollectMaintenanceCost`：全表向きリソースの維持コストを合算し budget から減算（Elastic カードは `BaseThroughput/Yield * RankMultiplier + ElasticBonus` を超過した分のみ従量課金） |
| 3 | 維持コスト徴収後の敗北判定 | `WinConditionChecker.CheckBudgetZero` → `CheckSystemDown` の順に判定し、成立したらそこで決着する。以降の手順は行わず、手札が上限を超えていても破棄を要求しない |
| 4 | Insight 生成 & Elastic ボーナス累積 | `GenerateInsight`：バックエンドの Data 系リソースが `StatCalculator.CalculateEffectiveInsight` で yield を計算し Insight プールに加算。続けて `StatCalculator.ApplyElasticBonus` で `ElasticBonus` を `elasticIncrement` ぶん**累積**（リセットではない。逓減は `CalculateEffectiveElasticBonus` が対数スケールで処理） |
| 5 | 一時効果の終了 | `ExpireTemporaryEffects`：`duration: "this_turn"` / `"until_next_own_turn_end"` の `TemporaryEffects` を除去 |
| 6 | ターン単位フラグのリセット | `ResetPerTurnFlags`：`HasAttacked` / `EffectUsedThisTurn` / `MonetizedAmount` / `IncidentPlayedThisTurn` を false/0 に戻す |
| 7 | 手札上限チェック | 手札が **6枚** を超過していなければ手順 8 を飛ばして手順 9 へ進む。超過していれば `EndPhaseProcessor` が `phase_end`（`needsDiscard: true`）イベントを返し、手順 9 のターン交代を保留する |
| 8 | プレイヤーが破棄カードを選択（手順 7 で超過時のみ） | `TurnControlsMessage.DiscardRequired`（手札枚数 − 6、`AvailableActions.ComputeTurnControls`）を見たクライアントが `discard_hand` で破棄するカードを送信。`DiscardProcessor` が枚数を、`CardMoveHelpers.DiscardCards` が指定カードの重複と手札への実在を検証し、いずれかに反すれば手札もトラッシュも変えずに拒否する。個別のタイムアウトは持たず、ターン全体のタイムバンクが時間の上限として働く |
| 9 | 手札調整後の判定とターン切り替え | 手順 7 で超過が無ければ `EndPhaseProcessor`、超過があれば手順 8 の `DiscardProcessor` が `EndPhaseProcessor.AdvanceAfterHandAdjustment` を呼び、`WinConditionChecker.CheckLaunchFailure` → `CheckTurnLimit` → どちらも成立しなければ `TurnManager.SwitchActivePlayer` → 次プレイヤーの `DrawPhaseProcessor.Process` を起動 |

> Note: ターンリミット（T30）は手順 9 でのみ成立する。アクション解決後の汎用判定 (`WinConditionChecker.Check`) に含めると T30 のプレイヤーが行動できなくなるため、`Check` は Budget Zero → System-Down → Timeout だけを見る。

> Note: 旧バージョンのドキュメントには「Elastic 値のリセット」手順が存在したが、実装上 `ElasticBonus` は毎ターン累積する設計（逓減は `StatCalculator.CalculateEffectiveElasticBonus` の対数スケーリングで表現）のため、リセットステップは存在しない。

### 効果計算

**リソーススタッツ計算の優先順序:**

| 優先度 | 適用内容 |
|--------|----------|
| 1 | ベース値（カード定義） |
| 2 | Rank倍率（small / medium / large） |
| 3 | Instance Family補正（M / C / R） |
| 4 | Platformカードの効果 |
| 5 | Attachmentの効果 |
| 6 | 一時効果（そのターンのみ） |
| 7 | 現在AV = MaxAV − ダメージ蓄積量 |

### Available Actions と NPC AI 統合

**Available Actions（Master Duel 方式）:**

サーバーがフェーズごとの有効アクションを計算し、クライアントとNPC AIの両方に提供する。

| 項目 | 内容 |
|------|------|
| 計算タイミング | `GameStateView.Build`（Service）が ClientGameState を組み立てるとき。アクティブプレイヤー（効果選択待ち中は chooser）の分のみ算出 |
| 関数 | `AvailableActions.GetAllAvailableActions(state, myField, oppField, hand, budget, insightPool, ...)`（Engine） |
| 戻り値 | `List<AvailableAction>`（タイプ別 discriminated union） |
| クライアント向け | `ClientGameState.myView.availableActions` に含めて Gateway 経由で WebSocket 送信 |
| NPC 向け | `NpcRunner` が情報秘匿済み ClientGameState の一部として strategy に渡す |

**AvailableAction のアクションタイプ:**

| Type | 主要フィールド |
|------|---------------|
| `play_card` | `HandInstanceID`, `CardID`, `ValidZones`, `ValidTargets`, `EffectTargetType` |
| `attack` | `SourceInstanceID`, `ValidTargets` |
| `scale_up` | `SourceInstanceID`, `TargetRank`, `InstanceFamily`, `NeedsFamily` |
| `monetize` | `SourceInstanceID`, `RemainingCapacity` |
| `use_ignition` | `CardID`, `SourceInstanceID`, `ValidTargets`, `EffectTargetType`, `RequiredCount` |
| `use_initiative` | `CardID`, `Kind`, `Cost`, `ValidTargets`, `EffectTargetType` |
| `resolve_pending_choice` | `EffectCardId`, `ChoiceKind`, `ChoiceOptions` |

AvailableAction は `type` による判別ユニオンで、各 variant は自身に必要な型固有の名前付きフィールドだけを持つ。汎用の `{type, value}` メタデータ機構は導入しない。named field は生成型（C# / Go / TS）に乗り、client / NPC が型で辿れることを優先するためである。

ゲームフロー制御（フェーズ終了、手札破棄）は `available_actions` に含めず、`turn_controls` メッセージとして別途送信される。

**NPC AI アーキテクチャ（Battle Server / C#）:**

```
NpcRunner (Service)
        │ GameStateView.Build で情報秘匿済み ClientGameState を組み立て
        ▼
┌──────────────────────┐  DecideMainPhaseActions(clientState)
│ INpcStrategy         │  DecideBattlePhaseActions(clientState)
│ interface            │  DecideDiscard(clientState, discardCount)
│                      │  DecideSlotSelect(clientState)
│                      │  DecidePendingEffectChoice(clientState)
└────────┬─────────────┘
         │
         ▼
NpcAi (YAML 設定駆動: 陣営 SHE / Tenki / Sugar / Tuners × 難易度 easy / hard)
```

NPC は `List<AvailableAction>` から最適なアクションを選択するのみ。
アクションの有効性判定はすべて Engine 側が担当し、ロジック重複を排除。

**NPC の決定フロー（Main Phase）:**

| 順序 | 処理 | 実装 |
|------|------|------|
| 1 | Strategy / Incident カードを使用 | `ImmediateActionStrategy` |
| 2 | Resource カードをデプロイ | `DeployStrategy`（Attachment / Reactive は設定がある場合のみ `AttachmentDeployStrategy` / `ReactiveDeployStrategy`） |
| 3 | 起動効果を発動 | `IgnitionStrategy` |
| 4 | 施策を使用 | `InitiativeStrategy`（施策カタログがある場合のみ） |
| 5 | スケールアップ | `ScaleUpStrategy` |
| 6 | Insight 配分 | `MonetizeStrategy`（Insight Pool > 0 の場合のみ） |
| 7 | フェーズ終了 | `MakeEndPhaseAction()` |

**NPC 関連ファイル（battle リポ: `src/OverloadParty.Battle.Npc/`）:**

| ファイル | 役割 |
|---------|------|
| `INpcStrategy.cs` | NPC 意思決定のインターフェース |
| `NpcAi.cs` | YAML 設定駆動の実装。判断を `Strategies/` 配下へ委譲 |
| `Strategies/` | アクション種別ごとの戦略（デプロイ・起動効果・施策・スケールアップ・収益化 等） |
| `AiConfig.cs` / `AiConfigLoader.cs` / `AiConfigValidator.cs` | 陣営 × 難易度の AI 設定 YAML の読み込みと検証 |
| `ActionFilter.cs` | AvailableAction 用ヘルパー（`FilterByType`, `PickBestZone` 等） |
| `TargetSelector.cs` | ターゲット選択ヘルパー（`WeakestInZone`, `StrongestInZone` 等） |
| `Data/` | 同梱の陣営別 AI 設定 YAML |

---

## 状態管理

### 排他制御（悲観ロック）

| 項目 | 内容 |
|------|------|
| メカニズム | `game_states` の対象行を `SELECT ... FOR UPDATE` で行ロックし、読み取り→更新処理→書き込みを単一トランザクションで行う |
| 競合時 | 後続のトランザクションは行ロックの解放を待って直列に実行される（リトライ不要） |
| version | 更新ごとに +1 する更新回数カウンタ。障害調査で更新の進行を確認する用途 |

1 ゲームの書き手はターンプレイヤーのアクション（+ NPC 進行）に限られ同一行への同時書き込みが稀なため、リトライ機構を持たない行ロックによる直列化を採用する。ロック対象は常に単一行のためデッドロックは発生しない。

### イベントソーシング

| 項目 | 内容 |
|------|------|
| 目的 | リプレイ機能・デバッグ用に全アクションを記録 |
| テーブル | `game_events`（追記のみ） |
| リプレイ方法 | 初期状態からイベントを順番に適用 |
| リプレイクエリ | `sequence_number ASC` 順に取得 |

### アクション失敗時のフィードバック

アクション処理が失敗した場合、battle は `GameRuleException` を 400、その他を 500 として `{"error": "..."}` 形式で gateway に返す。gateway はこれを WS の `action_rejected` メッセージ（`gameID` / `actionType` / `reason`）としてクライアントへ中継する。ルール違反はリトライしても成功しないため、自動リトライは行わない。

---

## アクション検証

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
| `monetize` | Main Phase | バックエンドのコンピュート（休止でない） | — | — | Insight Pool 残量 ≥ 分配量、TP上限 |
| `use_ignition` | Main/Battle Phase | 効果を持つカード | 効果の対象 | 効果コスト | 1ターン1回制限 |
| `use_initiative` | Main Phase | デッキが選んだプロダクトの施策 | 施策の対象 | Insight | ルーチン 1ターン1回 / スペシャル 1ゲーム1回、先攻 T1 不可 |

---

## プレイヤー ID と到達制御

battle は `PlayerNum`（1 または 2）を呼び出し元の申告としてそのまま受け取る。手札・フィールドの所有やターン順との整合性はアクション検証（前節）で確認するが、その `PlayerNum` を名乗る呼び出し元が実際にそのプレイヤー本人かどうかは検証できない。battle がプレイヤー ID 自体を持たない点は [DATA_DESIGN.md](DATA_DESIGN.md) のとおりで、プレイヤー ID から `PlayerNum` への対応付けと認可は gateway が担う。

内部トークンによるプレイヤー ID の伝播は、プレイヤーのデータを扱うサービスに対して行われる。battle はその対象外であり、内部トークンの検証を持たない。呼び出し元自体の到達制御は実行基盤の呼び出し認可に委ねる（[ADR-057](https://github.com/kenyamaneko/overload-party-common/blob/main/docs/adr/057-cloudrun-service-auth-iam-and-rs256.md)）。

---

## 実装規約

### Card 取得失敗時の fail-fast 戦略

card service からの Card 取得に失敗した場合、リトライせず `Environment.Exit(1)` で即死し、k8s の再起動に任せる。プロセス内のリトライバックオフを実装すると k8s の指数バックオフと二重化するため、cluster 側の再起動戦略を活用する。

### 既存ヘルパーの再利用

フィールド走査には `FieldHelpers.AllFaceUpResources` / `AllResources`、NPC 側は `TargetSelector` のゾーン走査ヘルパー（`WeakestInZone` / `StrongestInZone` 等）を再利用する。同じパターンの for ループを新たに書かない。

---

## csproj 境界と責務

Battle サービスは複数の csproj に分割し、各プロジェクトの責務と依存方向を境界として固定する。責務違反 (domain logic が外界に依存する等) がビルド時に検出できる状態を保つ。

### 各 csproj の責務

| プロジェクト | 担うもの |
|---|---|
| Models | ゲームに共通する値オブジェクトと列挙 |
| Engine | ゲームの domain logic と、外界へ要求する port の宣言 |
| Npc | NPC の思考戦略 |
| Data | port を満たす adapter (永続化・外部サービス接続) |
| Service | ユースケースの組み立て |
| Server | composition root と HTTP / WebSocket の入口 |

### 依存の方向

依存は Server → (Service / Data / Npc) → Engine → Models の一方向に流れる。Engine は domain logic を担い、外界 (DB・HTTP・外部サービス) を知らずに Models のみへ依存する。Engine が外界に求める操作は Engine 自身が port として宣言し、その実体は Data が adapter として与える。依存を一方向に保つことで、domain logic を外界の都合から切り離して変更・テストできる。

### port の置き場所

port（Engine が外界に要求する操作の interface）は `Engine/Ports/` 配下に置く。port は Engine が宣言した外界への要求であり Engine に閉じているため、別プロジェクトへ切り出さず Engine 内に置く。domain logic と同じ階層に混在させず `Ports/` に分離することで、両者の性質の違いを構造で示す。

### 依存の集約

共通依存は最浅の csproj に一度だけ宣言し、transitive resolution で各層へ伝播させる。各 csproj が同じ依存を再列挙しないことで、依存の追加・削除を 1 箇所に閉じ、層をまたいだ同期漏れを防ぐ。

### composition root

Server は composition root として各層の concrete を組み立て、依存を注入する。Server が直接参照するのは自身が組み立てる concrete に限り、transitive で解決できる中間層は再列挙しない。これにより Server が組み立てる concrete が参照リストから読み取れ、Server が domain logic を直接呼び始める変更は境界の diff として現れる。

