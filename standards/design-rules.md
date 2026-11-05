# Team design rules

Claude reviews designs against these rules. They are **draft FRC defaults** — edit the
numbers to match what the team actually builds and machines. Each rule has an ID so review
reports can cite it (e.g. `PL-2`).

Severity: **must** = flag as a problem · **should** = flag as a suggestion.

## Plates (PL)

| ID | Rule | Severity |
|---|---|---|
| PL-1 | Plate thickness is one of the stocked thicknesses in `materials.json` for its material. | must |
| PL-2 | Web width between pockets ≥ max(3 mm, 1.5 × plate thickness). | must |
| PL-3 | Material kept around the outer edge ≥ max(4 mm, 1.5 × thickness). | must |
| PL-4 | Material kept around bolt holes ≥ 1 × hole diameter (edge of hole to edge of pocket). | must |
| PL-5 | Material kept around bearing bores ≥ 5 mm. | must |
| PL-6 | Inside pocket corners have a radius ≥ the router bit radius (assume 1.5 mm for a 3 mm bit). | must |
| PL-7 | Plates over ~150 × 150 mm with no lightening are candidates for pockets. | should |
| PL-8 | Pockets don't cut through load paths between bearings, shafts and mounting holes. | should |

## Holes (HO)

| ID | Rule | Severity |
|---|---|---|
| HO-1 | Hole centre to plate edge ≥ 1.5 × hole diameter. | must |
| HO-2 | Hole sizes are standard clearance/tap sizes the team has bits for (e.g. M3/M4/M5, #10). | should |
| HO-3 | Hex bores and bearing bores use the team's standard press/slip sizes. | should |

## Polycarbonate (PC)

| ID | Rule | Severity |
|---|---|---|
| PC-1 | Bend radius ≥ 1 × thickness; bends are placed away from holes (≥ 2 × thickness). | must |
| PC-2 | No sharp inside corners — radius ≥ 2 mm (cracks start there). | must |

## Tubes (TU)

| ID | Rule | Severity |
|---|---|---|
| TU-1 | Tube sizes are stocked sizes from `materials.json`. | must |
| TU-2 | Bolt holes through 1.5 mm wall tube near loads are flagged for a reinforcement/insert check. | should |

## Parts & data (DA)

| ID | Rule | Severity |
|---|---|---|
| DA-1 | Every part has a material (otherwise it has no mass and breaks weight checks). | must |
| DA-2 | COTS parts without a correct material have a mass override. | must |
| DA-3 | Parts have meaningful names (not "Part 1"). | should |
| DA-4 | Features are named when their purpose isn't obvious; no long chains of unnamed features. | should |
