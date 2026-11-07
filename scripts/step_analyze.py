"""Analyze STEP files locally — no Onshape API calls.

Measures what the team's designs actually do, for the learn-patterns skill:
plates (thickness, size, pockets, webs, margins, holes), joints (holes sharing an axis) with the
fasteners on them, and fastener-like parts. Units: millimetres.

    <venv-python> scripts/step_analyze.py robot.step [more.step | folder ...]
    <venv-python> scripts/step_analyze.py exports/ --no-joints
    <venv-python> scripts/step_analyze.py arm.step --json-only

Needs CadQuery, which is installed in its own venv outside OneDrive:
    %LOCALAPPDATA%\\frc1690-cad\\venv\\Scripts\\python.exe
(see "Local STEP analysis" in standards/patterns/README.md for setup).

Writes one JSON per file to .cache/learn/<file>.json and prints a summary.
STEP files carry geometry and names only — no feature history and no materials.
"""

import argparse
import json
import math
import re
import statistics
import sys
from collections import Counter
from pathlib import Path

try:
    import cadquery as cq
    from OCP.BRepAdaptor import BRepAdaptor_Surface
    from OCP.BRepGProp import BRepGProp_Face
    from OCP.gp import gp_Pnt, gp_Vec
    from OCP.IFSelect import IFSelect_RetDone
    from OCP.STEPCAFControl import STEPCAFControl_Reader
    from OCP.TCollection import TCollection_ExtendedString
    from OCP.TDataStd import TDataStd_Name
    from OCP.TDF import TDF_Label, TDF_LabelSequence
    from OCP.TDocStd import TDocStd_Document
    from OCP.TopLoc import TopLoc_Location
    from OCP.XCAFDoc import XCAFDoc_DocumentTool
except ImportError:
    sys.exit("CadQuery is not installed for this Python. Run this script with "
             "%LOCALAPPDATA%\\frc1690-cad\\venv\\Scripts\\python.exe")

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / ".cache" / "learn"

# Heuristics (mm). Kept here, named, so they're easy to tune.
PLATE_MAX_THICKNESS = 13.0      # thicker than this is a block, not a plate
PLATE_MAX_ASPECT = 0.2          # thickness / width
AXIS_TOL = 0.1                  # hole axes closer than this are the same line
JOINT_GAP = 0.5                 # holes on one axis this close end to end are a joint
ANGLE_TOL = 1e-3                # radians, for "parallel"
CORNER_ARC_MAX = 3.0            # arcs up to this radius (or 1.5 × the smallest) are corner fillets…
CORNER_ARC_FRACTION = 0.15      # …as long as they're small next to the pocket
POLYGON_NAMES = {3: "triangle", 4: "rectangle", 5: "pentagon", 6: "hexagon"}
FASTENER_NAME = re.compile(r"bolt|screw|rivet|shcs|bhcs|fhcs|nut|washer|standoff|spacer|pin\b", re.I)
# Bought parts that can look like plates (belts, gears, bearings…). Skipped in plate stats.
COTS_NAME = re.compile(r"belt|bearing|gear|pulley|sprocket|wheel|motor|kraken|falcon|\bneo\b|vortex|"
                       r"collar|bushing|encoder|\b\d+\s?t\b", re.I)
_materials = json.loads((ROOT / "standards" / "materials.json").read_text(encoding="utf-8"))
STOCK_PLATE_MM = sorted({t for m in _materials.get("materials", _materials).values() if isinstance(m, dict)
                         for t in (m.get("stock") or {}).get("plateThicknessMm", [])})


# ── reading ──────────────────────────────────────────────────────────────────

def _label_name(label):
    attr = TDataStd_Name()
    if label.FindAttribute(TDataStd_Name.GetID_s(), attr):
        return attr.Get().ToExtString()
    return None


def read_step(path):
    """Return [(name, cq.Solid)] in world position. Uses STEP product names when present."""
    doc = TDocStd_Document(TCollection_ExtendedString("step"))
    reader = STEPCAFControl_Reader()
    reader.SetNameMode(True)
    if reader.ReadFile(str(path)) != IFSelect_RetDone or not reader.Transfer(doc):
        raise RuntimeError(f"could not read {path}")
    tool = XCAFDoc_DocumentTool.ShapeTool_s(doc.Main())

    parts = []

    def walk(label, loc, name):
        if tool.IsAssembly_s(label):
            comps = TDF_LabelSequence()
            tool.GetComponents_s(label, comps)
            for i in range(1, comps.Length() + 1):
                comp = comps.Value(i)
                ref = TDF_Label()
                if tool.IsReference_s(comp) and tool.GetReferredShape_s(comp, ref):
                    walk(ref, loc.Multiplied(tool.GetLocation_s(comp)),
                         _label_name(comp) or _label_name(ref))
            return
        shape = cq.Shape.cast(tool.GetShape_s(label).Moved(loc))
        solids = shape.Solids() if shape.ShapeType() != "Solid" else [shape]
        for k, solid in enumerate(solids):
            parts.append((name if len(solids) == 1 else f"{name} [{k + 1}]", solid))

    free = TDF_LabelSequence()
    tool.GetFreeShapes(free)
    for i in range(1, free.Length() + 1):
        label = free.Value(i)
        walk(label, TopLoc_Location(), _label_name(label))

    return [(name or f"Part {i + 1}", s) for i, (name, s) in enumerate(parts)]


# ── plates ───────────────────────────────────────────────────────────────────

def _r(x, nd=2):
    return None if x is None else round(float(x), nd)


def _classify_wire(wire, holes=()):
    """Describe one closed loop cut through a plate: a hole or a pocket.

    `holes` is [(centre, radius)] of the plate's holes. A pocket arc concentric with a hole is a
    "hole ring": the pocket wraps around the hole, leaving (arc radius − hole radius) of material.
    """
    edges = wire.Edges()
    kinds = Counter(e.geomType() for e in edges)
    face = cq.Face.makeFromWires(wire)
    area = face.Area()
    bb = wire.BoundingBox()

    if set(kinds) <= {"CIRCLE"}:
        radii = {round(e.radius(), 3) for e in edges}
        if len(radii) == 1:
            r = radii.pop()
            return {"kind": "hole", "diameter": _r(2 * r, 3), "radius": r, "area": area,
                    "centre": edges[0].arcCenter()}

    size = max(bb.xlen, bb.ylen, bb.zlen)
    straight = kinds.get("LINE", 0)
    arcs = sorted((e for e in edges if e.geomType() == "CIRCLE"), key=lambda e: e.radius())
    other = len(edges) - straight - len(arcs)  # splines, ellipses
    # Corner fillets are the small arcs; larger arcs are curved sides of the pocket.
    corner_limit = min(max(CORNER_ARC_MAX, 1.5 * arcs[0].radius()), CORNER_ARC_FRACTION * size) if arcs else 0
    corners = [e for e in arcs if e.radius() <= corner_limit]
    rings = []
    for e in arcs:
        if e in corners:
            continue
        c = e.arcCenter()
        for centre, hr in holes:
            if (c - centre).Length < 0.05 and e.radius() > hr:
                rings.append({"holeDiameter": _r(2 * hr, 2), "ring": _r(e.radius() - hr, 2)})
                break
    curved = len(arcs) - len(corners) - len(rings) + other
    corner_r = corners[0].radius() if corners else 0.0

    if straight == 2 and len(arcs) == 2 and other == 0 and abs(arcs[0].radius() - arcs[1].radius()) < 0.01:
        shape = "slot"  # two straight sides, two equal end arcs
    elif straight == 0:
        shape = "round" if other == 0 else "freeform"
    else:
        sides = straight + curved
        shape = POLYGON_NAMES.get(sides, f"{sides}-sided")
        if curved:
            shape += " (curved)"
    if rings:
        shape += " around holes"
    return {"kind": "pocket", "shape": shape, "area": area, "cornerRadius": corner_r, "size": size,
            "holeRings": rings}


def _min_gap(i, items, wires, boxes):
    """Smallest distance from loop i to any other loop in `items` (bbox-prefiltered)."""
    best = math.inf
    for j in items:
        if j == i:
            continue
        a, b = boxes[i], boxes[j]
        box_gap = max(a.xmin - b.xmax, b.xmin - a.xmax, a.ymin - b.ymax, b.ymin - a.ymax,
                      a.zmin - b.zmax, b.zmin - a.zmax, 0)
        if box_gap >= best:
            continue
        best = min(best, wires[i].distance(wires[j]))
    return best


def analyze_plate(solid):
    planes = [f for f in solid.Faces() if f.geomType() == "PLANE"]
    if not planes:
        return None
    face = max(planes, key=lambda f: f.Area())
    normal = face.normalAt()
    plane = cq.Plane(origin=face.Center(), xDir=_perp(normal), normal=normal)
    local = solid.transformShape(plane.fG).BoundingBox()
    thickness = local.zlen
    length, width = max(local.xlen, local.ylen), min(local.xlen, local.ylen)
    is_plate = thickness <= PLATE_MAX_THICKNESS and width > 0 and thickness / width <= PLATE_MAX_ASPECT

    info = {"isPlate": is_plate, "thickness": _r(thickness), "length": _r(length),
            "width": _r(width), "faceArea": _r(face.Area(), 0),
            "stockThickness": any(abs(thickness - t) < 0.05 for t in STOCK_PLATE_MM)}
    if not is_plate:
        return info

    outer = face.outerWire()
    inner = face.innerWires()
    loops = [_classify_wire(w) for w in inner]  # first pass finds the holes
    hole_circles = [(l["centre"], l["radius"]) for l in loops if l["kind"] == "hole"]
    loops = [l if l["kind"] == "hole" else _classify_wire(w, hole_circles) for w, l in zip(inner, loops)]
    boxes = [w.BoundingBox() for w in inner]
    pockets = [i for i, l in enumerate(loops) if l["kind"] == "pocket"]
    holes = [i for i, l in enumerate(loops) if l["kind"] == "hole"]

    for i in pockets:
        loops[i]["web"] = _min_gap(i, pockets, inner, boxes)
        loops[i]["edgeMargin"] = inner[i].distance(outer)
        loops[i]["holeMargin"] = _min_gap(i, holes, inner, boxes) if holes else math.inf

    outer_area = cq.Face.makeFromWires(outer).Area()
    pocket_area = sum(loops[i]["area"] for i in pockets)

    def stat(key, fn=min):
        vals = [loops[i][key] for i in pockets if math.isfinite(loops[i][key])]
        return _r(fn(vals)) if vals else None

    info.update({
        "holes": dict(sorted(Counter(loops[i]["diameter"] for i in holes).items())),
        "holeRings": dict(Counter(f'{r["holeDiameter"]}:{r["ring"]}' for i in pockets for r in loops[i]["holeRings"])),
        "pocketCount": len(pockets),
        "pocketShapes": dict(Counter(loops[i]["shape"] for i in pockets)),
        "pocketSizeMedian": stat("size", statistics.median),
        "cornerRadiusMin": stat("cornerRadius"),
        "webMin": stat("web"),
        "webMedian": stat("web", statistics.median),
        "edgeMarginMin": stat("edgeMargin"),
        "holeMarginMin": stat("holeMargin"),
        "lightenedFraction": _r(pocket_area / outer_area, 3) if outer_area else None,
    })
    return info


def _perp(v):
    """Any unit vector perpendicular to v."""
    other = cq.Vector(1, 0, 0) if abs(v.x) < 0.9 else cq.Vector(0, 1, 0)
    return v.cross(other).normalized()


# ── joints ───────────────────────────────────────────────────────────────────
# Joints are found from hole axes, not by measuring distances between solids (far too slow on a
# few hundred parts): two parts are joined where they have complete holes on the same axis that
# meet end to end, e.g. a clearance hole in a plate above a tapped hole in a tube wall.
# Fastener parts whose shank lies on that axis are counted as the joint's fasteners.

def _canonical(dv):
    for c in (dv.x, dv.y, dv.z):
        if abs(c) > 1e-6:
            return (dv, 1) if c > 0 else (-dv, -1)
    return dv, 1


def cylinders(solid):
    """Complete cylinders of a solid: [{concave, diameter, dir, point, interval}], one per axis+radius."""
    groups = {}
    for f in solid.Faces():
        if f.geomType() != "CYLINDER":
            continue
        surf = BRepAdaptor_Surface(f.wrapped).Cylinder()
        axis = surf.Axis()
        loc = cq.Vector(axis.Location().X(), axis.Location().Y(), axis.Location().Z())
        d = axis.Direction()
        dv, sign = _canonical(cq.Vector(d.X(), d.Y(), d.Z()))
        t0 = loc.dot(dv)
        perp = loc - dv * t0
        umin, umax, vmin, vmax = f._uvBounds()
        # a hole's surface normal points at its axis; a shaft's / boss's points away
        pnt, nrm = gp_Pnt(), gp_Vec()
        BRepGProp_Face(f.wrapped).Normal((umin + umax) / 2, (vmin + vmax) / 2, pnt, nrm)
        radial = cq.Vector(pnt.X(), pnt.Y(), pnt.Z()) - loc
        radial = radial - dv * radial.dot(dv)
        concave = radial.dot(cq.Vector(nrm.X(), nrm.Y(), nrm.Z())) < 0
        a, b = sorted((t0 + sign * vmin, t0 + sign * vmax))
        key = (concave, round(surf.Radius(), 3), round(dv.x, 3), round(dv.y, 3), round(dv.z, 3),
               round(perp.x, 1), round(perp.y, 1), round(perp.z, 1), round(a, 1))
        g = groups.setdefault(key, {"concave": concave, "diameter": 2 * surf.Radius(), "dir": dv,
                                    "point": perp, "interval": [a, b], "span": 0.0})
        g["span"] += umax - umin
        g["interval"] = [min(g["interval"][0], a), max(g["interval"][1], b)]
    return [g for g in groups.values() if g["span"] >= 2 * math.pi - 1e-3]


def _axis_clusters(items):
    """Group (part, cylinder) items whose axes coincide within AXIS_TOL. Returns lists of items."""
    cell = AXIS_TOL
    grid, clusters = {}, []
    for it in items:
        c = it[1]
        dkey = (round(c["dir"].x, 3), round(c["dir"].y, 3), round(c["dir"].z, 3))
        gx, gy, gz = (int(math.floor(v / cell)) for v in (c["point"].x, c["point"].y, c["point"].z))
        found = None
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    for idx in grid.get((dkey, gx + dx, gy + dy, gz + dz), ()):
                        if (clusters[idx][0][1]["point"] - c["point"]).Length <= AXIS_TOL:
                            found = idx
                            break
                    if found is not None:
                        break
                if found is not None:
                    break
            if found is not None:
                break
        if found is None:
            clusters.append([it])
            grid.setdefault((dkey, gx, gy, gz), []).append(len(clusters) - 1)
        else:
            clusters[found].append(it)
    return clusters


def find_joints(parts, kinds):
    """kinds[i] is 'fastener', 'cots' or 'part'. Returns one entry per joined pair of non-fastener parts."""
    items = []
    for i, (_, solid) in enumerate(parts):
        for c in cylinders(solid):
            if kinds[i] == "fastener" or c["concave"]:
                items.append((i, c))
    joints = {}
    for cluster in _axis_clusters(items):
        holes = [(i, c) for i, c in cluster if kinds[i] != "fastener" and c["concave"]]
        shanks = [(i, c) for i, c in cluster if kinds[i] == "fastener"]
        seen = set()
        for x in range(len(holes)):
            for y in range(x + 1, len(holes)):
                (i, a), (j, b) = holes[x], holes[y]
                if i == j:
                    continue
                gap = max(a["interval"][0], b["interval"][0]) - min(a["interval"][1], b["interval"][1])
                if gap > JOINT_GAP:
                    continue
                i, j, a, b = (i, j, a, b) if i < j else (j, i, b, a)
                if (i, j) in seen:
                    continue
                seen.add((i, j))
                lo, hi = min(a["interval"][0], b["interval"][0]), max(a["interval"][1], b["interval"][1])
                fast = sorted({parts[k][0] for k, s in shanks
                               if s["interval"][0] < hi and s["interval"][1] > lo})
                j_entry = joints.setdefault((i, j), {"a": parts[i][0], "b": parts[j][0], "axes": 0,
                                                     "holePairs": Counter(), "fasteners": Counter()})
                j_entry["axes"] += 1
                j_entry["holePairs"][f"{_r(a['diameter'], 2)}/{_r(b['diameter'], 2)}"] += 1
                j_entry["fasteners"].update(fast)
    out = []
    for (i, j), e in joints.items():
        e["aKind"], e["bKind"] = kinds[i], kinds[j]
        e["holePairs"] = dict(e["holePairs"])
        e["fasteners"] = dict(e["fasteners"])
        out.append(e)
    return out


def is_fastener(name, solid):
    if FASTENER_NAME.search(name or ""):
        return True
    bb = solid.BoundingBox()
    return solid.Volume() < 2000 and max(bb.xlen, bb.ylen, bb.zlen) < 80 and any(
        f.geomType() == "CYLINDER" for f in solid.Faces()) and not any(
        f.geomType() == "PLANE" and f.Area() > 400 for f in solid.Faces())


# ── report ───────────────────────────────────────────────────────────────────

def analyze_file(path, contacts=True):
    parts = read_step(path)
    result = {"file": path.name, "partCount": len(parts), "parts": [], "joints": []}
    for name, solid in parts:
        entry = {"name": name, "volume_cm3": _r(solid.Volume() / 1000, 2),
                 "cots": bool(COTS_NAME.search(name or ""))}
        entry["fastenerLike"] = bool(FASTENER_NAME.search(name or "")) or (
            not entry["cots"] and is_fastener(name, solid))
        if not entry["fastenerLike"] and not entry["cots"]:
            try:
                entry.update(analyze_plate(solid) or {})
            except Exception as e:  # odd geometry shouldn't stop the whole file
                entry["error"] = f"plate analysis failed: {e}"
        result["parts"].append(entry)
    if contacts:
        kinds = ["fastener" if p["fastenerLike"] else "cots" if p["cots"] else "part" for p in result["parts"]]
        result["joints"] = find_joints(parts, kinds)
    return result


def summarize(results):
    plates = [p for r in results for p in r["parts"] if p.get("isPlate")]
    lines = [f"{len(results)} file(s), {sum(r['partCount'] for r in results)} parts, {len(plates)} plates"]
    if plates:
        lines.append("Plate thickness (mm): " + ", ".join(
            f"{t}×{n}" for t, n in sorted(Counter(p["thickness"] for p in plates).items())))
        odd = [f"{p['name']} ({p['thickness']})" for p in plates if not p["stockThickness"]]
        if odd:
            lines.append("Not a stocked thickness (printed? raised features?): " + ", ".join(odd))
        pocketed = [p for p in plates if p.get("pocketCount")]
        lines.append(f"Pocketed plates: {len(pocketed)} of {len(plates)}")
        shapes = Counter()
        for p in pocketed:
            shapes.update(p["pocketShapes"])
        if shapes:
            lines.append("Pocket shapes: " + ", ".join(f"{s} {n}" for s, n in shapes.most_common()))
        for key, label in (("webMin", "Web min"), ("edgeMarginMin", "Edge margin min"),
                           ("holeMarginMin", "Hole margin min"), ("cornerRadiusMin", "Corner radius min"),
                           ("lightenedFraction", "Area removed")):
            vals = sorted(p[key] for p in pocketed if p.get(key) is not None)
            if vals:
                lines.append(f"{label}: median {statistics.median(vals)}, range {vals[0]}–{vals[-1]}")
        rings = Counter()
        for p in pocketed:
            rings.update(p.get("holeRings", {}))
        if rings:
            lines.append("Pockets wrap around holes leaving a ring (hole Ø : ring, mm): "
                         + ", ".join(f"{k}×{n}" for k, n in rings.most_common(8)))
        holes = Counter()
        for p in plates:
            for d, n in p.get("holes", {}).items():
                holes[d] += n
        if holes:
            lines.append("Plate hole diameters (mm): " + ", ".join(f"{d}×{n}" for d, n in holes.most_common(12)))
    joints = [j for r in results for j in r.get("joints", []) if j["aKind"] == j["bKind"] == "part"]
    if joints:
        lines.append(f"Joints between custom parts: {len(joints)}; fastener positions per joint: "
                     + ", ".join(f"{k}→{v}" for k, v in sorted(Counter(j["axes"] for j in joints).items())))
        pairs, fasteners = Counter(), Counter()
        for j in joints:
            pairs.update(j["holePairs"])
            fasteners.update(j["fasteners"])
        lines.append("Hole pairs at joints (mm, a/b): " + ", ".join(f"{k}×{n}" for k, n in pairs.most_common(8)))
        if fasteners:
            lines.append("Fasteners in joints: " + ", ".join(f"{k}×{n}" for k, n in fasteners.most_common(8)))
        unfastened = sum(1 for j in joints if not j["fasteners"])
        if unfastened:
            lines.append(f"Joints with no modelled fastener on the axis: {unfastened}")
    return "\n".join(lines)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("paths", nargs="+", type=Path, help="STEP files or folders")
    p.add_argument("--no-joints", action="store_true", help="skip joint detection")
    p.add_argument("--json-only", action="store_true", help="print the JSON instead of the summary")
    args = p.parse_args()
    for stream in (sys.stdout, sys.stderr):  # Windows consoles default to cp1252; part names may be Hebrew
        stream.reconfigure(encoding="utf-8", errors="replace")

    files = []
    for path in args.paths:
        files += sorted(f for f in path.rglob("*") if f.suffix.lower() in (".step", ".stp")) if path.is_dir() else [path]
    if not files:
        sys.exit("no .step/.stp files found")

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    results = []
    for f in files:
        print(f"reading {f.name} ...", file=sys.stderr)
        r = analyze_file(f, contacts=not args.no_joints)
        (OUT_DIR / f"{f.stem}.json").write_text(json.dumps(r, indent=1, ensure_ascii=False, default=str),
                                                 encoding="utf-8")
        results.append(r)

    if args.json_only:
        print(json.dumps(results, indent=1, ensure_ascii=False, default=str))
    else:
        print(summarize(results))
        print(f"\nDetails: {OUT_DIR.relative_to(ROOT)}\\<file>.json", file=sys.stderr)


if __name__ == "__main__":
    main()
