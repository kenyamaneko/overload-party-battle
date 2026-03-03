# Overload Party — Battle Server

クラウドインフラをテーマにしたカードゲーム「Overload Party」のバトルサーバー。

ASP.NET Core + WebSocket で構築。ゲームエンジン・エフェクトシステム・NPC AI を含む。

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
├── OverloadParty.Battle.Matchmaking/   # マッチメイキング（FIFO キュー）
└── OverloadParty.Battle.Server/        # ASP.NET Core エントリポイント + WebSocket
tests/
└── OverloadParty.Battle.Tests/         # xUnit テスト
```

## アーキテクチャ

```
Server (ASP.NET, WebSocket)
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
| `make test` | テスト実行 |
| `make test-coverage` | カバレッジ付きテスト |
| `make generate` | common リポからカード定義を生成 |
| `make clean` | ビルド成果物削除 |

## ローカル開発モード

`make run` で起動するローカルモードでは:

- DB 不要（in-memory mock リポジトリ）
- Firebase 不要（`dev-token-{uid}` 形式のトークンで認証）
- NPC 対戦が即時プレイ可能
- WebSocket: `ws://localhost:9002/ws?token=dev-token-player1`
- REST: `/api/dev/cards`, `/api/dev/status`, `/health`
