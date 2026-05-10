"""NSwag が出力する snake_case の C# プロパティ名 (例: ``Card_id``) を
PascalCase (``CardId``) に書き換える post-process。

NSwag のプロパティ名生成器は JSON config から差し替えられないため codegen 後に
ここで固定する。``[JsonPropertyName]`` は wire 形式のままなので JSON 互換性は
保たれる。
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

# NSwag 出力の "public TYPE Foo_bar { get; set; }" を捕まえる。
# - TYPE は型名 (List<string>, System.Text.Json.JsonElement, long?, string 等)
# - 名前部分には少なくとも 1 つの '_' が含まれる
PROPERTY_RE = re.compile(
    r"^(?P<indent>\s+)public\s+(?P<type>[\w\.\<\>\?\,\s]+?)\s+(?P<name>[A-Z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)+)\s*\{\s*get;\s*set;\s*\}(?P<tail>.*)$"
)


def to_pascal(name: str) -> str:
    parts = name.split("_")
    return "".join(p[:1].upper() + p[1:] for p in parts if p)


def fix_file(path: Path) -> int:
    original = path.read_text(encoding="utf-8")
    changed = 0
    out_lines = []
    for line in original.splitlines():
        m = PROPERTY_RE.match(line)
        if m:
            new_name = to_pascal(m.group("name"))
            if new_name != m.group("name"):
                line = f"{m.group('indent')}public {m.group('type').strip()} {new_name} {{ get; set; }}{m.group('tail')}"
                changed += 1
        out_lines.append(line)

    if changed:
        path.write_text("\n".join(out_lines) + "\n", encoding="utf-8")
        print(f"::notice::renamed {changed} properties in {path}", file=sys.stderr)
    return changed


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: fix_nswag_property_names.py FILE [FILE ...]", file=sys.stderr)
        return 2
    total = 0
    for arg in sys.argv[1:]:
        total += fix_file(Path(arg))
    print(f"::notice::total {total} property renames", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
