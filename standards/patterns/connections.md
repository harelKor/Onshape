# Connection patterns (CN)

Everything here is **draft** until a student confirms it. Fill in **Examples** with links to our
own part studios — that matters more than the text.

### CN-1 Tube to plate — bolts through tube — draft
- **Use when:** joints that must come apart (serviceable mechanisms, prototypes).
- **Numbers:** ≥ 2 bolts per joint, spaced ≥ 3 × bolt diameter; hole centre ≥ 1.5 × d from the
  tube end (HO-1). On 1.5 mm wall tube, check crush — see TU-2.
- **Hardware:** _from hardware.md_
- **Examples:** _add one_

### CN-2 Tube to plate — rivets — draft
- **Use when:** permanent frame joints (drivetrain, superstructure). Lighter and faster than bolts.
- **Avoid when:** the part will be replaced or adjusted during the season.
- **Numbers:** spacing 25–40 mm, edge distance ≥ 2 × rivet diameter.
- **Examples:** _add one_

### CN-3 Gusset — draft
- **Use when:** two tubes meet at an angle and the joint takes bending (towers, arm roots).
- **Numbers:** legs ≥ 2 × tube width each side of the joint; ≥ 2 fasteners per tube.
- **Examples:** _add one_

### CN-4 Plate-to-plate spacing — standoffs / spacer tubes — draft
- **Use when:** two parallel plates hold shafts between them (gearboxes, intakes, shooters).
- **Numbers:** a standoff near each bearing pair; spacing ≤ ~150 mm along the plate.
- **Examples:** _add one_

### CN-5 Hex shaft retention — draft
- **Use when:** any hex shaft between plates.
- **Options:** shaft collars, retaining (snap) rings, tapped shaft end + bolt and washer.
  Team default: _?_
- **Examples:** _add one_

### CN-6 Bearing in plate — draft
- **Use when:** flanged bearings in plates; flange on the side the load pushes toward.
- **Numbers:** bore per HO-3, PL-5 margin around the bore.
- **Examples:** _add one_

### CN-7 Belt / chain tensioning — draft
- **Options:** slotted motor mount, cam/eccentric, idler. Team default: _?_
- **Examples:** _add one_

### CN-8 Polycarbonate mounting — draft
- **Use when:** polycarb guards, hoods, intakes.
- **Numbers:** washers under bolt heads; hole centre ≥ 2 × thickness from bends (PC-1).
- **Examples:** _add one_

### CN-9 M5 bolt into a tapped plate — team habit (seen, not yet confirmed)
- **What it is:** an M5 socket-head or button-head bolt through a Ø5.2 clearance hole, threaded
  into a Ø4.2 (M5 tap drill) hole in the other part — no nut.
- **Seen:** Kepler 2026 shooter: of the M5 bolt positions between custom parts, 62 go 5.2 → 4.2
  (tapped) and 32 go 5.2 → 5.2 (through-bolted). Of 98 part-to-part joints, 66 use 1 bolt,
  19 use 2, and a few use 3–9.
- **Hardware:** M5 SHCS 10–20 mm (16 and 20 mm most common), M5 BHCS 18–20 mm, large M5 washers.
- **Watch:** thread engagement in 3–4 mm plate is short — 6.7 mm plate is the thinner limit
  for tapping, _confirm with the team_.
- **Source:** `scripts/step_analyze.py` on 1690-26-400.step, 2026-11-06.

### CN-10 Bearing bores — team habit (seen, not yet confirmed)
- Bores seen: Ø22.35 for 0.875" OD flanged bearings (+0.13 mm), Ø28.7 for 1.125" OD (+0.13),
  Ø27.1 for 20×27×4 bearings (+0.1). Shaft clearance holes next to them: Ø19.7–20.1.
- **Source:** Kepler 2026 shooter.

## Team choices to fill in
- Default fastener for frame joints (rivets / #10 / M5): _M5 for plates (Kepler shooter) — frames?_
- Do we use 3D-printed brackets in load paths? _?_
