"""Repo paths, config loading and Onshape URL parsing."""

import json
import re
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
CONFIG_PATH = REPO_ROOT / "onshape.config.json"
MATERIALS_PATH = REPO_ROOT / "standards" / "materials.json"
QUERIES_DIR = REPO_ROOT / "featurescript" / "queries"
CACHE_DIR = REPO_ROOT / ".cache"


def load_config() -> dict:
    return json.loads(CONFIG_PATH.read_text(encoding="utf-8"))


def load_materials() -> dict:
    return json.loads(MATERIALS_PATH.read_text(encoding="utf-8"))


@dataclass(frozen=True)
class Ref:
    """A document / workspace-version-microversion / element reference."""

    did: str
    wvm: str  # "w", "v" or "m"
    wvmid: str
    eid: str | None = None

    @property
    def dwvm(self) -> str:
        return f"d/{self.did}/{self.wvm}/{self.wvmid}"

    @property
    def dwvme(self) -> str:
        if not self.eid:
            raise ValueError("This URL has no element (/e/...) — open the tab in Onshape and copy its URL.")
        return f"{self.dwvm}/e/{self.eid}"


_URL_RE = re.compile(
    r"/documents/(?P<did>[0-9a-f]{24})/(?P<wvm>[wvm])/(?P<wvmid>[0-9a-f]{24})(?:/e/(?P<eid>[0-9a-f]{24}))?"
)


def parse_url(url: str) -> Ref:
    m = _URL_RE.search(url)
    if not m:
        raise ValueError(f"Not an Onshape document URL: {url}")
    return Ref(m["did"], m["wvm"], m["wvmid"], m["eid"])
