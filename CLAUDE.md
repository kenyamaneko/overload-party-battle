# CLAUDE.md

このファイルはリポジトリで作業する際のガイドラインを提供する。

## ビルド・テスト

```bash
make build   # ビルド
make test    # テスト実行
make run     # ローカル開発サーバー起動
```

## 設計原則

### ワークアラウンドではなく根本解決する

問題に対して場当たり的な回避策を取らない。原因を特定し、既存のフローや責務分担と整合する形で根本的に解決する。

- 暫定対応を入れる場合は、なぜ暫定なのか・いつ解消するかをコメントかイシューで明記する
- 既存の処理フローと異なる特殊パスを作らない。同じ種類の処理は同じパイプラインに乗せる

### エラーは握りつぶさない

どうしてもそうする必要がある場合はユーザーに確認する。

## C# コーディング規約

### LINQ を積極的に使う

コレクション操作には可能な限り LINQ を使い、宣言的で可読性の高いコードを書く。

**LINQ で置き換えるべきパターン:**
- フィルタ・変換・集計のための手動ループ → `Where`, `Select`, `Count`, `Sum`, `Any`, `All` 等
- 手動 Dictionary 構築 → `ToDictionary()`
- 手動ソート (`List.Sort()`) → `OrderBy()` / `OrderByDescending()`
- 手動 min/max 探索 → `MinBy()` / `MaxBy()`
- 手動の存在チェックループ → `Any()` / `All()`
- 手動の検索ループ → `First()` / `FirstOrDefault()`

**LINQ を使わない場面:**
- 副作用を伴うループ（状態変更、フィールドへの null 代入など）→ `foreach` 文を使う
- インデックスとゾーン情報が必要な検索（`FindResourceByID` 等）→ for ループで可
- 配置ロジックなど早期 return + 副作用が必要な処理

### foreach 文 vs List.ForEach()

`List<T>.ForEach()` は使わない。副作用の実行には `foreach` 文を使う。

- LINQ は**値の変換・問い合わせ**（副作用なし）
- `foreach` は**副作用の実行**

```csharp
// Good: 変換 → LINQ
var names = users.Where(u => u.Active).Select(u => u.Name).ToList();

// Good: 副作用 → foreach
foreach (var user in users)
    user.Deactivate();

// Bad: 副作用に ForEach を使わない
users.ForEach(u => u.Deactivate());
```

### switch 式を優先する

値を返す分岐には switch 式を使う。if/else チェーンや三項演算子のネストより宣言的で読みやすい。

```csharp
// Good: switch 式
var winnerID = actionResult.WinnerNum switch
{
    0 => "",
    1 => game.Player1ID,
    _ => game.Player2ID,
};

// Good: LINQ と組み合わせ
return conditions.All(cond => cond.Type switch
{
    "min_budget" => ctx.Budget >= cond.Value,
    "max_budget" => ctx.Budget <= cond.Value,
    _ => true,
});

// Bad: if/else で値を決める
string winnerID;
if (actionResult.WinnerNum == 0)
    winnerID = "";
else
    winnerID = actionResult.WinnerNum == 1 ? game.Player1ID : game.Player2ID;
```

副作用を伴う分岐（メソッド呼び出し、例外送出など）には従来の switch 文を使う。

### 既存ヘルパーの再利用

フィールド走査には `FieldHelpers` の LINQ ベースメソッドを再利用する:
- `FieldHelpers.AllFaceUpResources(field)` — 表向きリソース列挙
- `FieldHelpers.AllResources(field)` — 全リソース列挙
- `TargetSelector.FaceUpInZone(field, zone)` — ゾーン指定の表向きリソース（Npc 内）

同じパターンの for ループを新たに書かず、これらを `Where`, `Any`, `Count` 等と組み合わせる。

### コメントは「意図」だけ書く

実装を読めばわかる内容のコメントは書かない。コメントを残すのは、コードから読み取れない **意図・背景・理由** を伝える必要があるときだけ。Doc コメント（`/// <summary>` 等）はこのルールの対象外。

```csharp
// Bad: コードを読めばわかる
// Already at max rank
if (currentRank == Rank.Large) { continue; }

// Bad: 条件の言い換え
// Must have a family for Medium/Large
if (targetFamily is null) { ... }

// Good: ルール上の理由など、コードだけでは読み取れない意図
// ダメージは Rank 変更後も保持される（ルールブック §6）
resource.CurrentAV = newMaxAV - existingDamage;
```
