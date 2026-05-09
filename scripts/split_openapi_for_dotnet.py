"""data/openapi.yaml を `x-battle-csharp-package` 拡張で 2 つのビューに分割する。

NSwag は単一 spec から単一 .cs を出力するため、battle 側で
`packages/api-battle-rpc-dotnet` (RPC) と `packages/game-state-dotnet` (state) の
2 パッケージに分けるには、事前に schema をビュー化する必要がある。

各 schema に `x-battle-csharp-package: rpc | state` を付け、対応するビューだけを
抽出した OpenAPI document を一時ディレクトリに書き出す。`paths` は両ビュー共通とし
(NSwag が DTO 生成時に未参照スキーマを drop しないように), 出力先のスキーマ集合だけを
切り替える。

使い方:

    python scripts/split_openapi_for_dotnet.py \
        --input data/openapi.yaml \
        --rpc-out /tmp/openapi.rpc.yaml \
        --state-out /tmp/openapi.state.yaml
"""

from __future__ import annotations

import argparse
import copy
import sys
from pathlib import Path

import yaml

PACKAGE_KEY = "x-battle-csharp-package"
VALID_PACKAGES = {"rpc", "state"}


def build_view(spec: dict, package: str) -> dict:
    if package not in VALID_PACKAGES:
        raise ValueError(f"unknown package {package!r}, expected one of {sorted(VALID_PACKAGES)}")

    view = copy.deepcopy(spec)
    schemas = view.get("components", {}).get("schemas", {})

    kept: dict[str, dict] = {}
    for name, schema in schemas.items():
        annotation = schema.get(PACKAGE_KEY)
        if annotation is None:
            raise ValueError(
                f"schema {name!r} is missing required extension {PACKAGE_KEY!r}; "
                "every schema must declare which dotnet package it belongs to"
            )
        if annotation not in VALID_PACKAGES:
            raise ValueError(
                f"schema {name!r} has invalid {PACKAGE_KEY}={annotation!r}; "
                f"expected one of {sorted(VALID_PACKAGES)}"
            )
        if annotation == package:
            stripped = {k: v for k, v in schema.items() if k != PACKAGE_KEY}
            kept[name] = stripped

    view["components"]["schemas"] = kept

    # NSwag は DTO-only 生成でも paths を解決するため、別 package の schema を
    # 参照する path が view に残ると `Could not resolve the path '#/components/schemas/X'`
    # で fail する。NSwag 出力には paths/operations は不要なので view から除去する。
    view["paths"] = {}
    if "components" in view:
        view["components"].pop("parameters", None)

    return view


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--rpc-out", required=True, type=Path)
    parser.add_argument("--state-out", required=True, type=Path)
    args = parser.parse_args()

    spec = yaml.safe_load(args.input.read_text(encoding="utf-8"))

    rpc_view = build_view(spec, "rpc")
    state_view = build_view(spec, "state")

    args.rpc_out.parent.mkdir(parents=True, exist_ok=True)
    args.state_out.parent.mkdir(parents=True, exist_ok=True)
    args.rpc_out.write_text(yaml.safe_dump(rpc_view, sort_keys=False, allow_unicode=True), encoding="utf-8")
    args.state_out.write_text(yaml.safe_dump(state_view, sort_keys=False, allow_unicode=True), encoding="utf-8")

    print(f"::notice::wrote {args.rpc_out} ({len(rpc_view['components']['schemas'])} schemas)")
    print(f"::notice::wrote {args.state_out} ({len(state_view['components']['schemas'])} schemas)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
