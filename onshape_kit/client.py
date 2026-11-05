"""Thin Onshape REST client.

Everything that talks to Onshape goes through `Client`, which provides:
  * Basic auth with one API key (an optional separate ONSHAPE_RW_* key is used for writes)
  * A rate limiter shared across processes (MCP server + CLI) via a state file
  * A response cache keyed by document microversion, so unchanged documents cost nothing
  * A write guard: writes only to workspaces named `claude/...`
"""

import base64
import hashlib
import json
import os
import time
import urllib.error
import urllib.parse
import urllib.request

from .config import CACHE_DIR, REPO_ROOT, Ref, load_config


class OnshapeError(RuntimeError):
    pass


class WriteGuardError(OnshapeError):
    pass


def _load_env_local() -> dict:
    """Fallback for machines that keep keys in .env.local instead of user env vars."""
    path = REPO_ROOT / ".env.local"
    values = {}
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                key, _, value = line.partition("=")
                values[key.strip()] = value.strip().strip('"')
    return values


def _user_env(name: str) -> str | None:
    """Read a Windows user environment variable straight from the registry, so keys set after
    the Claude app started work without restarting it."""
    try:
        import winreg
    except ImportError:
        return None
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as key:
            return winreg.QueryValueEx(key, name)[0] or None
    except OSError:
        return None


def _has_error_notice(result) -> bool:
    notices = result.get("notices") if isinstance(result, dict) else None
    return any((n.get("message", n) or {}).get("level") == "ERROR" for n in notices or [])


class _FileLock:
    def __init__(self, path, stale_seconds=10):
        self.path = path
        self.stale_seconds = stale_seconds

    def __enter__(self):
        while True:
            try:
                fd = os.open(self.path, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                os.close(fd)
                return self
            except FileExistsError:
                try:
                    if time.time() - os.path.getmtime(self.path) > self.stale_seconds:
                        os.remove(self.path)
                        continue
                except FileNotFoundError:
                    continue
                time.sleep(0.05)

    def __exit__(self, *exc):
        try:
            os.remove(self.path)
        except FileNotFoundError:
            pass


class RateLimiter:
    """Sliding-window limiter whose state lives in .cache/ so every process shares it."""

    def __init__(self, calls: int, window_seconds: float, max_wait_seconds: float):
        self.calls = calls
        self.window = window_seconds
        self.max_wait = max_wait_seconds
        self.state_path = CACHE_DIR / "ratelimit.json"
        self.lock_path = CACHE_DIR / "ratelimit.lock"

    def _read(self) -> list[float]:
        try:
            return json.loads(self.state_path.read_text())
        except (FileNotFoundError, ValueError):
            return []

    def acquire(self):
        deadline = time.time() + self.max_wait
        while True:
            with _FileLock(self.lock_path):
                now = time.time()
                stamps = [t for t in self._read() if now - t < self.window]
                if len(stamps) < self.calls:
                    stamps.append(now)
                    self.state_path.write_text(json.dumps(stamps))
                    return
                wait = stamps[0] + self.window - now
            if time.time() + wait > deadline:
                raise OnshapeError(
                    f"Rate limit: {self.calls} calls per {self.window:.0f}s used up; "
                    f"next slot in {wait:.0f}s. Try again shortly or batch the work."
                )
            time.sleep(wait + 0.05)

    def usage(self) -> dict:
        now = time.time()
        stamps = [t for t in self._read() if now - t < self.window]
        return {
            "callsInWindow": len(stamps),
            "limit": self.calls,
            "windowSeconds": self.window,
            "nextSlotInSeconds": 0 if len(stamps) < self.calls else round(stamps[0] + self.window - now, 1),
        }


class Client:
    MICROVERSION_TTL = 60  # seconds a workspace microversion is trusted before re-checking

    def __init__(self):
        cfg = load_config()
        self.cfg = cfg
        self.api = cfg["baseUrl"].rstrip("/") + cfg.get("apiPath", "/api")
        rl = cfg["rateLimit"]
        CACHE_DIR.mkdir(exist_ok=True)
        (CACHE_DIR / "responses").mkdir(exist_ok=True)
        self.limiter = RateLimiter(rl["calls"], rl["windowSeconds"], rl.get("maxWaitSeconds", 90))
        self._mv_memo: dict[tuple[str, str], tuple[float, str]] = {}
        self._ws_memo: dict[str, tuple[float, list]] = {}
        env_local = _load_env_local()
        self._env = lambda name: os.environ.get(name) or _user_env(name) or env_local.get(name)

    # ---- auth -------------------------------------------------------------

    def _auth_header(self, write: bool) -> str:
        if write:
            access = self._env("ONSHAPE_RW_ACCESS_KEY") or self._env("ONSHAPE_ACCESS_KEY")
            secret = self._env("ONSHAPE_RW_SECRET_KEY") or self._env("ONSHAPE_SECRET_KEY")
        else:
            access = self._env("ONSHAPE_ACCESS_KEY") or self._env("ONSHAPE_RW_ACCESS_KEY")
            secret = self._env("ONSHAPE_SECRET_KEY") or self._env("ONSHAPE_RW_SECRET_KEY")
        if not access or not secret:
            raise OnshapeError(
                "Onshape API keys are not set. Set ONSHAPE_ACCESS_KEY / ONSHAPE_SECRET_KEY as Windows "
                "user environment variables (see .env.example)."
            )
        token = base64.b64encode(f"{access}:{secret}".encode()).decode()
        return f"Basic {token}"

    # ---- raw request ------------------------------------------------------

    def request(self, method: str, path: str, params: dict | None = None, body=None,
                write: bool = False, accept: str = "application/json"):
        url = self.api + path
        if params:
            url += "?" + urllib.parse.urlencode({k: v for k, v in params.items() if v is not None})
        data = json.dumps(body).encode() if body is not None else None
        headers = {"Authorization": self._auth_header(write), "Accept": accept}
        if data is not None:
            headers["Content-Type"] = "application/json"

        for attempt in range(3):
            self.limiter.acquire()
            req = urllib.request.Request(url, data=data, method=method, headers=headers)
            try:
                with urllib.request.urlopen(req, timeout=120) as resp:
                    raw = resp.read()
                    if "json" in (resp.headers.get("Content-Type") or ""):
                        return json.loads(raw) if raw else None
                    return raw
            except urllib.error.HTTPError as e:
                if e.code == 429 and attempt < 2:
                    time.sleep(float(e.headers.get("Retry-After") or 30))
                    continue
                detail = e.read().decode(errors="replace")[:800]
                raise OnshapeError(f"{method} {path} -> HTTP {e.code}: {detail}") from None
        raise OnshapeError(f"{method} {path} -> still rate-limited by Onshape after retries")

    # ---- cached reads -----------------------------------------------------

    def _cache_token(self, ref: Ref) -> str:
        """Versions and microversions never change; workspaces are keyed by current microversion."""
        if ref.wvm in ("v", "m"):
            return f"{ref.wvm}:{ref.wvmid}"
        key = (ref.did, ref.wvmid)
        hit = self._mv_memo.get(key)
        if hit and time.time() - hit[0] < self.MICROVERSION_TTL:
            return f"m:{hit[1]}"
        mv = self.request("GET", f"/documents/d/{ref.did}/w/{ref.wvmid}/currentmicroversion")["microversion"]
        self._mv_memo[key] = (time.time(), mv)
        return f"m:{mv}"

    def get(self, path: str, ref: Ref | None = None, params: dict | None = None):
        """GET with caching when `ref` says which document state the path belongs to."""
        return self._cached("GET", path, ref, params, None)

    def evaluate(self, path: str, ref: Ref, body: dict):
        """Read-only POST (e.g. FeatureScript evaluation), cached like a GET."""
        return self._cached("POST", path, ref, None, body)

    def _cached(self, method, path, ref, params, body):
        if ref is None:
            return self.request(method, path, params, body)
        token = self._cache_token(ref)
        key = hashlib.sha256(json.dumps([method, path, params, body, token], sort_keys=True).encode()).hexdigest()
        cache_file = CACHE_DIR / "responses" / f"{key}.json"
        if cache_file.exists():
            return json.loads(cache_file.read_text(encoding="utf-8"))
        result = self.request(method, path, params, body)
        if not _has_error_notice(result):  # a fixed script must not hit a cached failure
            cache_file.write_text(json.dumps(result), encoding="utf-8")
        return result

    # ---- guarded writes ---------------------------------------------------

    def workspaces(self, did: str) -> list:
        hit = self._ws_memo.get(did)
        if hit and time.time() - hit[0] < 300:
            return hit[1]
        result = self.request("GET", f"/documents/d/{did}/workspaces")
        self._ws_memo[did] = (time.time(), result)
        return result

    def check_writable(self, ref: Ref) -> str:
        """Return the workspace name, or raise if writes there are not allowed."""
        guard = self.cfg["guard"]
        if ref.wvm != "w":
            raise WriteGuardError("Writes need a workspace URL (/w/...), not a version or microversion.")
        ws = next((w for w in self.workspaces(ref.did) if w["id"] == ref.wvmid), None)
        if ws is None:
            raise WriteGuardError(f"Workspace {ref.wvmid} not found in document {ref.did}.")
        if ref.did in guard.get("unguardedDocuments", []):
            return ws["name"]
        if not ws["name"].startswith(guard["writeWorkspacePrefix"]):
            raise WriteGuardError(
                f"Refusing to write to workspace '{ws['name']}'. Claude only edits branch workspaces named "
                f"'{guard['writeWorkspacePrefix']}...'. Create one in Onshape (Branch from a version) and use its URL."
            )
        return ws["name"]

    def write(self, method: str, path: str, ref: Ref, body=None, params=None):
        self.check_writable(ref)
        result = self.request(method, path, params, body, write=True)
        self._mv_memo.pop((ref.did, ref.wvmid), None)  # document changed
        return result
