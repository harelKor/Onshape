# Lightening patterns (LT)

Limits (web width, margins, corner radius) are in `design-rules.md` PL-2…PL-8. This file says
which **shape** to pick. Two features:
- `featurescript/part_lighten.fs` — **PartLighten** (team's copy of Evan Fish / 2471's generator).
  *Manual* = ribs along sketch lines. *Automatic* = generated ribs: Truss around holes (LT-0),
  Warren truss (LT-2), Grid (LT-3), Triangle grid (LT-1 alternative). Walls along every edge and a
  ring around every hole come from "Thickness of walls"; webs from "Thickness of ribs".
- `featurescript/lighten_plate.fs` — **Lighten Plate**: hexagon and circle lattices (LT-1, LT-4).

### LT-0 Truss pockets that wrap around holes — team habit (seen, not yet confirmed)
- **What it is:** the plate is pocketed into a truss of straight webs between holes and bores;
  each pocket's outline curves around nearby holes at a constant distance, leaving a ring of
  material. Pockets are mostly triangles, some quads; a few follow large round features with arcs.
- **Use when:** our default for custom aluminum plates (20 of 22 shooter plates).
- **Numbers seen (Kepler 2026 shooter, 1690-26-400):**
  - web between pockets: 5 mm typical (3–6 mm); **3 mm on the 6.7 mm side plates** (431/441)
  - ring around M5 clearance holes (Ø5.2): **5 mm** typical (4–6 mm)
  - ring around M5 tapped holes (Ø4.2): 5 mm or **3–3.5 mm** on the tighter side plates
  - edge margin: about 4 mm (3–6 mm); ring around the big Ø211 bore: 5–7.4 mm
  - pocket corner radius: **2 mm** (some 1.75 mm) — suggests a 4 mm router bit
  - area removed: about half the plate (median 51%, range 8–75%)
- **Tool:** PartLighten → Automatic → *Truss around holes*. Kepler-like start: walls 4–5 mm,
  ribs 5 mm (3 mm on 6.7 mm side plates), tool Ø4 mm, node spacing 40–60 mm. Ribs join hole
  centres, outline points and interior points (Delaunay), so check the result and add
  *Extra rib lines* where a load path (PL-8) needs one. Wall and ring are one value, so the
  separate edge (4 mm) vs ring (5 mm) habit isn't modelled.
- **Note:** these numbers are thinner than `design-rules.md` PL-2/PL-3/PL-4 allow for 6.7 mm
  plate (see "Rules vs. practice" at the end).
- **Source:** `scripts/step_analyze.py` on 1690-26-400.step, 2026-11-06.

### LT-1 Hexagon lattice — draft
- **Use when:** large flat plates with load in several directions (drivetrain side plates,
  electronics plates). Even webs, good stiffness for the weight removed.
- **Avoid when:** the plate is a narrow arm — a lattice leaves partial pockets and thin edges.
- **Numbers:** pocket 15–30 mm corner-to-corner on 3–6 mm aluminum; web per PL-2.
- **Tool:** `Lighten Plate`, shape Hexagon.
- **Examples:** _add one_

### LT-2 Triangle truss — draft
- **Use when:** long arms and elevator/cascade plates loaded mainly in bending along their length.
  The webs line up with the load paths between pivots and bolt patterns.
- **Avoid when:** the plate is mostly holes and bores already — there's no room for a truss.
- **Numbers:** keep a solid rib along both long edges ≥ PL-3 margin; triangles 30–60° apex.
- **Tool:** PartLighten → Automatic → *Warren truss (arms)*; truss angle 45–60°. Zigzag nodes sit
  on the bounding box, so tapered arms get partial end bays — add extra rib lines there.
- **Examples:** _add one_

### LT-3 Rounded-rectangle / slot pockets — draft
- **Use when:** plates with a regular bolt grid (e.g. 1/2" or 25 mm hole patterns) — one pocket
  per grid cell keeps the pattern usable.
- **Avoid when:** there's no grid; hexes remove more material.
- **Tool:** PartLighten → Automatic → *Grid*; set the cell size to the hole pattern pitch and
  *Pattern angle* if the grid isn't square to the longest edge.
- **Examples:** _add one_

### LT-4 Circles — draft
- **Use when:** polycarbonate (no sharp corners, PC-2) and quick small plates.
- **Avoid when:** weight matters a lot — circles remove the least material for a given web.
- **Tool:** `Lighten Plate`, shape Circle.

### LT-5 Don't lighten — draft
- Plates under ~100 × 100 mm, plates that are mostly bores, and anything where a pocket would cut
  the path between a bearing and the plate's mounting bolts (PL-8). Say so instead of forcing it.

## Team choices to fill in
- Router bit diameters we normally use: _4 mm? (2 mm pocket corners everywhere on Kepler) — confirm_
- Thickest aluminum the router cuts cleanly: _6.7 mm is used a lot — thicker?_
- Shape students prefer by default: _truss pockets around holes (LT-0) — confirm_

## Rules vs. practice (for the team to decide)
Kepler's shooter plates are pocketed thinner than `design-rules.md` allows. Either the rules
are too strict for how we build, or these plates are pushing it. Don't change the rules until a
student decides.

| Rule | Rule says (6.7 mm plate) | Kepler shooter does |
|---|---|---|
| PL-2 web ≥ max(3, 1.5 × t) | ≥ 10 mm | 3–5 mm |
| PL-3 edge ≥ max(4, 1.5 × t) | ≥ 10 mm | 3–4 mm |
| PL-4 ring ≥ 1 × hole Ø | ≥ 5.2 mm (M5 clearance), ≥ 4.2 mm (tapped) | 5 mm; 3–3.5 mm around tapped holes |
| PL-6 corner r ≥ 1.5 mm | ≥ 1.5 mm | 2 mm ✓ |
