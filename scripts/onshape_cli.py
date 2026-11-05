"""Command-line access to the same operations as the MCP server, for batch jobs.

    python scripts/onshape_cli.py elements <doc-url>
    python scripts/onshape_cli.py parts <partstudio-url>
    python scripts/onshape_cli.py plates <partstudio-url>
    python scripts/onshape_cli.py features <partstudio-url>
    python scripts/onshape_cli.py query <partstudio-url> <query-name-or-file>
    python scripts/onshape_cli.py render <url> [--view isometric] [--kind partstudio|assembly]
    python scripts/onshape_cli.py weight [--system 200]
    python scripts/onshape_cli.py usage

Writes (materials, features, Feature Studio pushes) are only available through the MCP
server, where each one is approved individually.
"""

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from onshape_kit import ops  # noqa: E402


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    for name in ("elements", "workspaces", "parts", "plates", "features"):
        sub.add_parser(name).add_argument("url")
    q = sub.add_parser("query")
    q.add_argument("url")
    q.add_argument("query")
    r = sub.add_parser("render")
    r.add_argument("url")
    r.add_argument("--view", default="isometric")
    r.add_argument("--kind", default="partstudio", choices=["partstudio", "assembly"])
    w = sub.add_parser("weight")
    w.add_argument("--system")
    sub.add_parser("usage")
    a = p.parse_args()

    handlers = {
        "elements": lambda: ops.list_elements(a.url),
        "workspaces": lambda: ops.list_workspaces(a.url),
        "parts": lambda: ops.part_studio_summary(a.url),
        "plates": lambda: ops.plate_report(a.url),
        "features": lambda: ops.features(a.url),
        "query": lambda: ops.run_fs_query(a.url, a.query),
        "render": lambda: ops.render(a.url, a.view, a.kind),
        "weight": lambda: ops.weight_check(a.system),
        "usage": ops.api_usage,
    }
    print(json.dumps(handlers[a.cmd](), indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
