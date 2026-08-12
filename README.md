# overload-party-battle

カードゲーム Overload Party の NPC / PvP 対戦のゲーム作成・アクション処理・状態管理を担うマイクロサービス。

## 技術スタック

| レイヤー | 技術 |
|---|---|
| 言語 | C# (.NET 10) |
| フレームワーク | ASP.NET Core |
| データベース | Cloud SQL PostgreSQL |
| ストレージ | Cloud Storage |
| 同期通信 | REST |

## ドキュメント

| ドキュメント | 内容 |
|---|---|
| [セットアップ](docs/SETUP.md) | 環境変数と同梱するカードマスターデータの説明 |
| [API仕様書](data/openapi.yaml) | REST API のエンドポイント定義 |
| [API リファレンス](docs/API_REFERENCE.md) | HTTP RPC の解説 |
| [データ設計書](docs/DATA_DESIGN.md) | テーブル定義 |
| [ADR](https://github.com/kenyamaneko/overload-party-common/tree/main/docs/adr)（commonリポジトリ） | 設計判断の背景・理由・結果 |
| [システム構成図](https://github.com/kenyamaneko/overload-party-common#システム構成図)（commonリポジトリ） | Overload Party 全体の構成図 |
| [テスト観点カタログ](https://kenyamaneko.github.io/overload-party-battle/) | テスト名から自動生成した、テスト済みの観点一覧 |
