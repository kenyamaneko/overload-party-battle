# overload-party-battle

C# ゲームエンジン。Gateway から HTTP RPC で呼ばれ、NPC / PvP 対戦のゲーム作成・アクション処理・状態管理を行う。カード定義は card が publish したマスターデータから起動時にロードする。

詳細は [API_REFERENCE.md](docs/API_REFERENCE.md) / [データ設計書](docs/DATA_DESIGN.md) を参照。設計判断 (Why) は [common の ADR](https://github.com/kenyamaneko/overload-party-common/tree/main/docs/adr)、サービス構成全体の図は [common のシステム構成図](https://github.com/kenyamaneko/overload-party-common#システム構成図) を参照。環境変数・同梱するカードマスターデータは [docs/SETUP.md](docs/SETUP.md) を参照。

[テスト観点カタログ](https://kenyamaneko.github.io/overload-party-battle/): テスト名から生成した、テスト済みの観点の一覧。
