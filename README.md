# Overload Party — Battle Server

クラウドインフラをテーマにしたカードゲーム「Overload Party」のバトルサーバー。

ASP.NET Core によるゲームエンジン API バックエンド。エフェクトシステム・NPC AIを含むステートレスな対戦ロジックを提供します。

## 必要環境

- .NET 10 SDK
- PostgreSQL（本番モード時のみ）

## クイックスタート

```bash
# ビルド
make build

# ローカルサーバー起動（port 9002, in-memory mock repos）
make run

# テスト実行
make test
```

## プロジェクト構成

```
src/
├── OverloadParty.Battle.Models/        # POCO モデル・定数・enum
├── OverloadParty.Battle.Engine/        # ゲームエンジン（ステートレス）
│   └── Effects/                        # エフェクトシステム（Op パイプライン）
├── OverloadParty.Battle.Npc/           # NPC AI（ルールベース + 陣営別戦略）
├── OverloadParty.Battle.Data/          # リポジトリ（Dapper / Mock）
├── OverloadParty.Battle.Service/       # サービス層（ゲーム操作ファサード）
└── OverloadParty.Battle.Server/        # ASP.NET Core エントリポイント (REST API)
tests/
└── OverloadParty.Battle.Tests/         # xUnit テスト
```

## アーキテクチャ

```
Server (ASP.NET REST API)
    ↓
Service (ゲーム操作ファサード)
    ↓
Engine / Effects / NPC   ← 外部技術に依存しない純粋なドメインロジック
    ↓
Models                   ← POCO のみ
    ↑
Data (Dapper, PostgreSQL) ← インターフェース経由で分離
```

Engine / Effects / NPC は NuGet パッケージに依存せず、`System.*` のみ使用。

## Make コマンド

| コマンド | 内容 |
|---|---|
| `make build` | ソリューションビルド |
| `make run` | ローカル開発サーバー起動 |
| `make test` | ユニットテスト実行（DB 不要） |
| `make test-integration` | DB 統合テスト込みで実行（コンテナ自動起動） |
| `make test-coverage` | カバレッジ付きテスト |
| `make generate` | common リポからカード定義を生成 |
| `make clean` | ビルド成果物削除 |

## 統合テスト

`PgGameRepository` / `PgCardRepository` の DB 統合テストは、共通リポジトリの PostgreSQL テストコンテナを使用します。

```bash
# 自動（コンテナ起動→テスト→停止）
make test-integration

# 手動
docker compose -f ../overload-party-common/db/docker-compose.test.yml up -d
TEST_DB_URL="Host=localhost;Port=5433;Database=testdb;Username=testuser;Password=testpass" make test
docker compose -f ../overload-party-common/db/docker-compose.test.yml down
```

`TEST_DB_URL` が未設定の場合、DB テストは自動スキップされます。CI では `make test` のみで既存のユニットテストだけ実行されます。

## Roadmap

- [ ] ターンタイマー
- [ ] セキュリティ監査
- [ ] ロードテスト
- [ ] 本番リリース

## ローカル開発モード

`make run` で起動するローカルモードでは:

- DB 不要（in-memory mock リポジトリ）
- Firebase 不要（`dev-token-{uid}` 形式のトークンで認証）
- NPC 対戦が即時プレイ可能
- REST: `/api/v1/games/npc`, `/api/v1/games/{id}/actions`, `/health` 等
