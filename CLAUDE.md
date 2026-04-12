# CLAUDE.md - overload-party-battle

## 行動制約

- 設計変更を伴う作業の後は memory ファイルを更新する
- エラーは握りつぶさない
- コメントは意図（なぜそうしたか）が読み取りづらい場合のみ記述する
- git tag を手動で打たない（CI が自動作成する）
- card service の `card_definitions` テーブルを battle から直接 DB 参照しない。`CardCache` 経由のみ
- Card 取得失敗時はリトライせず `Environment.Exit(1)` で即死し k8s 再起動に任せる

## C# コーディング規約

### 上流で保証された契約を下流で再検証しない

上流のプロデューサ（`AvailableActions`, `ResourceHelpers`, `GameEngine` 等）が契約上保証している値を「念のため」再検証して silent fallback しない。契約違反は `throw new InvalidOperationException(...)` で即クラッシュ。

- `FirstOrDefault` + silent default → 禁止。契約上存在するなら `First()` または `MustGet`
- `switch` / `if/else` の default は `throw`。sentinel 値（空文字・0・`"?"`）を返さない
- カード定義の取得は `ICardCache.MustGet` に統一
- 判断基準: 「この null/空文字は実運用で発生しうるか」 → 上流のバグ以外で起きないなら throw

### YAML パーサで unknown 値を silent drop しない

typo 検知のため全パーサで unknown → throw 統一。

### エラーハンドリングの責務分担

例外は「意味のある処置ができる層」まで伝播。途中で catch して silent default を返さない。`app.UseExceptionHandler` (Program.cs) が唯一の catch 地点。

### LINQ を積極的に使う

フィルタ・変換・集計には LINQ。副作用を伴うループには `foreach` 文。`List<T>.ForEach()` は使わない。

### switch 式を優先する

値を返す分岐には switch 式。default アームは `throw`。副作用を伴う分岐には switch 文。

### 既存ヘルパーの再利用

フィールド走査には `FieldHelpers.AllFaceUpResources` / `AllResources` / `TargetSelector.FaceUpInZone` を再利用する。同じパターンの for ループを新たに書かない。
