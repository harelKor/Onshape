"""High-level Onshape operations shared by the MCP server and the CLI.

Every function takes Onshape URLs (copied from the browser) and returns plain JSON-able data.
Lengths are reported in millimetres and masses in kilograms.
"""

import base64
import re
import time
from pathlib import Path

from . import fsvalue
from .client import Client, OnshapeError
from .config import CACHE_DIR, QUERIES_DIR, REPO_ROOT, load_config, load_materials, parse_url

_client: Client | None = None


def client() -> Client:
    global _client
    if _client is None:
        _client = Client()
    return _client


def _mm(meters):
    return None if meters is None else round(meters * 1000, 2)


# ---- documents ----------------------------------------------------------------------------

def list_elements(url: str) -> list:
    ref = parse_url(url)
    elements = client().get(f"/documents/{ref.dwvm}/elements", ref)
    return [
        {"name": e["name"], "type": e.get("elementType"), "id": e["id"],
         "url": f"{client().cfg['baseUrl']}/documents/{ref.did}/{ref.wvm}/{ref.wvmid}/e/{e['id']}"}
        for e in elements
    ]


def list_workspaces(url: str) -> list:
    ref = parse_url(url)
    prefix = client().cfg["guard"]["writeWorkspacePrefix"]
    return [
        {"name": w["name"], "id": w["id"], "writableByClaude": w["name"].startswith(prefix),
         "url": f"{client().cfg['baseUrl']}/documents/{ref.did}/w/{w['id']}"}
        for w in client().workspaces(ref.did)
    ]


def create_version(url: str, name: str, description: str = "") -> dict:
    """Versions are non-destructive snapshots, so this is allowed on any workspace (with user approval)."""
    ref = parse_url(url)
    if ref.wvm != "w":
        raise OnshapeError("Create versions from a workspace URL (/w/...).")
    body = {"documentId": ref.did, "workspaceId": ref.wvmid, "name": name, "description": description}
    v = client().request("POST", f"/documents/d/{ref.did}/versions", body=body, write=True)
    return {"name": v.get("name"), "id": v.get("id")}


# ---- part studios -------------------------------------------------------------------------

def _material_of(props: list) -> dict | None:
    prop = next((p for p in props if p.get("name") == "Material"), None)
    value = prop and prop.get("value")
    if not value:
        return None
    density = next((p.get("value") for p in value.get("properties", []) if p.get("name") == "RHO"), None)
    return {"name": value.get("displayName"), "densityKgM3": density}


def part_studio_summary(url: str) -> list:
    """Parts with name, material and mass. Two API calls (metadata + mass properties)."""
    ref = parse_url(url)
    meta = client().get(f"/metadata/{ref.dwvme}/p", ref)
    mass = client().get(f"/partstudios/{ref.dwvme}/massproperties", ref, {"massAsGroup": "false"})
    bodies = mass.get("bodies", {})
    parts = []
    for item in meta.get("items", []):
        pid = item.get("partId")
        props = item.get("properties", [])
        name = next((p.get("value") for p in props if p.get("name") == "Name"), pid)
        body = bodies.get(pid, {})
        has_mass = body.get("hasMass", False)
        parts.append({
            "partId": pid,
            "name": name,
            "material": _material_of(props),
            "hasMass": has_mass,
            "massKg": round(body["mass"][0], 4) if has_mass and body.get("mass") else None,
        })
    return parts


def features(url: str) -> list:
    ref = parse_url(url)
    data = client().get(f"/partstudios/{ref.dwvme}/features", ref)
    states = data.get("featureStates", {})
    return [
        {"featureId": f.get("featureId"), "name": f.get("name"), "type": f.get("featureType"),
         "suppressed": f.get("suppressed", False),
         "status": states.get(f.get("featureId"), {}).get("featureStatus")}
        for f in data.get("features", [])
    ]


def run_fs_query(url: str, query: str) -> object:
    """Run a read-only FeatureScript lambda. `query` is a file in featurescript/queries/ or raw code."""
    ref = parse_url(url)
    path = QUERIES_DIR / (query if query.endswith(".fs") else f"{query}.fs")
    script = path.read_text(encoding="utf-8") if path.exists() else query
    result = client().evaluate(f"/partstudios/{ref.dwvme}/featurescript", ref, {"script": script, "queries": []})
    if result.get("notices"):
        errors = [n for n in result["notices"] if n.get("level") == "ERROR" or "ERROR" in str(n.get("type", ""))]
        if errors:
            raise OnshapeError(f"FeatureScript query failed: {errors[:3]}")
    return fsvalue.decode(result.get("result"))


def plate_report(url: str) -> list:
    """Every solid body measured as if it were a plate, with stock-thickness checks."""
    stock = {key: set(m.get("stock", {}).get("plateThicknessMm", []))
             for key, m in load_materials()["materials"].items()}
    rows = []
    for b in run_fs_query(url, "plate_summary") or []:
        t = _mm(b.get("thickness"))
        length, width = _mm(b.get("length")), _mm(b.get("width"))
        is_plate = bool(t and length and width and t <= 13 and t < 0.2 * min(length, width))
        rows.append({
            "partId": b.get("partId"),
            "name": b.get("name"),
            "faceId": b.get("faceId"),
            "isPlate": is_plate,
            "thicknessMm": t,
            "lengthMm": length,
            "widthMm": width,
            "faceAreaCm2": round(b["faceArea"] * 1e4, 1) if b.get("faceArea") else None,
            "volumeCm3": round(b["volume"] * 1e6, 2) if b.get("volume") else None,
            "holeDiametersMm": sorted({_mm(d) for d in b.get("holes") or []}),
            "matchesStockFor": sorted(k for k, s in stock.items() if t is not None and any(abs(t - x) < 0.05 for x in s)),
        })
    return rows


def render(url: str, view: str = "isometric", kind: str = "partstudio", width: int = 1000, height: int = 700) -> str:
    """Save a shaded image to .cache/renders and return its path (open it with the Read tool)."""
    ref = parse_url(url)
    base = "partstudios" if kind == "partstudio" else "assemblies"
    params = {"viewMatrix": view, "outputWidth": width, "outputHeight": height, "pixelSize": 0}
    data = client().get(f"/{base}/{ref.dwvme}/shadedviews", ref, params)
    images = data.get("images") or []
    if not images:
        raise OnshapeError("Onshape returned no image.")
    out_dir = CACHE_DIR / "renders"
    out_dir.mkdir(parents=True, exist_ok=True)
    out = out_dir / f"{ref.eid}-{view}-{int(time.time())}.png"
    out.write_bytes(base64.b64decode(images[0]))
    return str(out)


# ---- mass and weight ----------------------------------------------------------------------

def assembly_mass(url: str) -> dict:
    ref = parse_url(url)
    data = client().get(f"/assemblies/{ref.dwvme}/massproperties", ref)
    body = data.get("bodies", {}).get("-all-", data)
    mass = body.get("mass")
    return {"hasMass": body.get("hasMass", mass is not None), "massKg": round(mass[0], 3) if mass else None}


def weight_check(system: str | None = None) -> dict:
    """Total assembly mass per system against budgets in onshape.config.json."""
    cfg = load_config()
    names = [system] if system else list(cfg["systems"])
    report, total = {}, 0.0
    for name in names:
        sysc = cfg["systems"][name]
        entries = [e for e in sysc.get("design", []) if e.get("kind") == "assembly"]
        if not entries:
            report[name] = {"status": "no assembly configured"}
            continue
        mass = 0.0
        problems = []
        for e in entries:
            m = assembly_mass(e["url"])
            if m["massKg"] is None:
                problems.append(f"{e.get('label', e['url'])}: no mass")
            else:
                mass += m["massKg"]
        budget = sysc.get("weightBudgetKg")
        total += mass
        report[name] = {
            "massKg": round(mass, 3),
            "budgetKg": budget,
            "overBudgetKg": round(mass - budget, 3) if budget is not None and mass > budget else 0,
            "problems": problems,
        }
    robot_budget = cfg["robot"].get("weightBudgetKg")
    return {"systems": report, "checkedTotalKg": round(total, 3), "robotBudgetKg": robot_budget}


# ---- writes (guarded: claude/ workspaces only) ---------------------------------------------

def set_material(url: str, part_id: str, material_key: str) -> dict:
    ref = parse_url(url)
    materials = load_materials()["materials"]
    if material_key not in materials:
        raise OnshapeError(f"Unknown material '{material_key}'. Options: {', '.join(materials)}")
    mat = materials[material_key]
    c = client()
    c.check_writable(ref)
    meta = c.request("GET", f"/metadata/{ref.dwvme}/p/{part_id}")
    props = meta.get("properties") or (meta.get("items") or [{}])[0].get("properties", [])
    prop = next((p for p in props if p.get("name") == "Material"), None)
    if prop is None:
        raise OnshapeError("Could not find the Material property on this part.")
    value = {
        "displayName": mat["displayName"],
        "id": mat["displayName"],
        "libraryName": "FRC 1690",
        "properties": [{"name": "RHO", "displayName": "Density", "type": "REAL",
                        "units": "kg/m^3", "value": mat["densityKgM3"]}],
    }
    body = {"jsonType": "metadata-part", "partId": part_id,
            "properties": [{"propertyId": prop["propertyId"], "value": value}]}
    c.write("POST", f"/metadata/{ref.dwvme}/p/{part_id}", ref, body)
    return {"partId": part_id, "material": mat["displayName"], "densityKgM3": mat["densityKgM3"]}


def add_feature(url: str, feature: dict) -> dict:
    """Append a raw BTMFeature to a part studio's feature list."""
    ref = parse_url(url)
    result = client().write("POST", f"/partstudios/{ref.dwvme}/features", ref, {"feature": feature})
    state = result.get("featureState", {})
    return {"featureId": result.get("feature", {}).get("featureId"), "status": state.get("featureStatus")}


def _feature_namespace() -> str:
    lib = load_config()["featureLibrary"]
    lref = parse_url(lib["url"])
    elements = client().get(f"/documents/{lref.dwvm}/elements", lref, {"elementId": lref.eid})
    mv = next(e for e in elements if e["id"] == lref.eid)["microversionId"]
    if lref.wvm == "v":
        return f"d{lref.did}::v{lref.wvmid}::e{lref.eid}::m{mv}"
    return f"e{lref.eid}::m{mv}"


def add_lighten_feature(url: str, face_id: str, shape: str = "HEXAGON", pocket_size: str = "15 mm",
                        web_width: str = "4 mm", edge_margin: str = "5 mm", hole_margin: str = "6 mm",
                        corner_radius: str = "1.5 mm") -> dict:
    """Insert the team's Lighten Plate feature on one plate face (faceId from plate_report)."""
    ns = _feature_namespace()

    def qty(pid, expr):
        return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr}

    feature = {
        "btType": "BTMFeature-134",
        "featureType": load_config()["featureLibrary"].get("lightenFeatureType", "lightenPlate"),
        "name": "Lighten (Claude)",
        "namespace": ns,
        "parameters": [
            {"btType": "BTMParameterQueryList-148", "parameterId": "face",
             "queries": [{"btType": "BTMIndividualQuery-138", "deterministicIds": [face_id]}]},
            {"btType": "BTMParameterEnum-145", "parameterId": "shape", "enumName": "PocketShape",
             "value": shape.upper(), "namespace": ns},
            qty("pocketSize", pocket_size),
            qty("webWidth", web_width),
            qty("edgeMargin", edge_margin),
            qty("holeMargin", hole_margin),
            qty("cornerRadius", corner_radius),
        ],
    }
    return add_feature(url, feature)


_HEADER_RE = re.compile(r"^FeatureScript\s+\d+;", re.M)
_STD_IMPORT_RE = re.compile(r'(import\(path\s*:\s*"onshape/std/[^"]+",\s*version\s*:\s*")[^"]+(")')


def push_feature_studio(url: str, file: str) -> dict:
    """Replace a Feature Studio's code with a local .fs file, matching the target's std version."""
    ref = parse_url(url)
    c = client()
    c.check_writable(ref)
    path = Path(file)
    if not path.is_absolute():
        path = REPO_ROOT / path
    source = path.read_text(encoding="utf-8")

    current = c.request("GET", f"/featurestudios/{ref.dwvme}")
    header = _HEADER_RE.search(current.get("contents", ""))
    if header:
        version = re.search(r"\d+", header.group()).group()
        source = _HEADER_RE.sub(header.group(), source, count=1)
        source = _STD_IMPORT_RE.sub(lambda m: f"{m.group(1)}{version}.0{m.group(2)}", source)

    body = {
        "contents": source,
        "serializationVersion": current.get("serializationVersion"),
        "sourceMicroversion": current.get("sourceMicroversion"),
        "rejectMicroversionSkew": False,
    }
    result = c.write("POST", f"/featurestudios/{ref.dwvme}", ref, body)
    notices = result.get("notices") if isinstance(result, dict) else None
    return {"pushed": str(path.relative_to(REPO_ROOT)), "stdVersion": header and header.group(),
            "notices": notices or []}


def api_usage() -> dict:
    return client().limiter.usage()
