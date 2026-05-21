# テスト規約 (overload-party-battle)

battle リポのテストコードに関する固有の規約。一般的なテスト方針は
common の `rules/principles.md` を参照する。

## ダミーカード ID

### prefix と形式

テストフィクスチャに使うカード ID は `TST-XXYY` のダミー形式とする。
実カード ID (`SH-`, `SL-`, `TK-`, `TN-`, `NT-` 等) を直接書かない。

```cs
// OK
_cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));

// NG (実カード ID は使わない)
_cc.Add(TestFactory.ComputeCard(cardId: "TK-0001"));
```

理由: 実カードの仕様 (CardType / Subtype / stats / 効果) は SSoT 側で
独立に変更されうる。テストが実カードに紐づくと、関係ない仕様変更で
テストが連鎖崩壊し、テストの意図 (何を検証しているか) も曖昧になる。

### 番号の付け方

- 番号は **`0001` から開始** する。`TST-0001` がデフォルト。
- **同テスト内で複数のカードが共存する場合のみインクリメント** して
  `TST-0002` / `TST-0003` … を使う。
- 連番でなくてよい。テストケースが増減してもカード ID 番号は再採番しない。

```cs
public class FooTests
{
    private readonly TestCardCache _cc = new();

    public FooTests()
    {
        // 1 つのテストクラス内で 2 つのカードが共存するので 0001 / 0002
        _cc.Add(TestFactory.ComputeCard(cardId: "TST-0001"));
        _cc.Add(TestFactory.ReactiveCard(cardId: "TST-0002"));
    }
}
```

## 実カード挙動の担保

実カードが期待通り効果ハンドラに登録されているか、データロードが正しい
かは、ダミーカードでは確認できない。これらは `EffectInitTests.cs` 等の
登録レイヤーのテストで、real `cards_gen.json` を読み込んで確認する。

つまりレイヤーを分ける:

| レイヤー | 目的 | カード ID |
|---|---|---|
| 機構の単体テスト | choice / suspend / event firing 等の振る舞い | ダミー (TST-XXYY) |
| 登録の整合性テスト | 期待カードがレジストリに居る / トリガーが妥当 | 実 ID で参照 |
