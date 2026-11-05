# Onshape design assistant — FRC 1690

This repo lets Claude review and improve the team's **design** documents in Onshape:
design reviews, plate lightening, material assignment, weight checks and FeatureScript.

It is **not** part of the manufacturing/BOM workflow. Do not use the OrbitParts tools
from this project, and do not touch manufacture documents.

## How Claude talks to Onshape

```
Claude ──► onshape MCP server (onshape_kit/mcp_server.py) ──► Onshape REST API
Claude ──► scripts/onshape_cli.py (same code, for batch jobs) ─┘
```

- All Onshape access goes through `onshape_kit/client.py`. Never call the API with curl,
  `requests` or ad-hoc code — the client holds the rate limiter, cache and write guard.
- Keys come from Windows user environment variables (see `.env.example`). Never read,
  print or ask for key values.
- `onshape.config.json` lists the robot's systems, their design documents and weight budgets.
- `standards/` holds the team rules Claude reviews against: `design-rules.md` and `materials.json`.

## Hard rules

1. **Writes only in `claude/` branch workspaces.** The client refuses any write to a workspace
   whose name doesn't start with `claude/` (except documents listed in
   `guard.unguardedDocuments`). Never work around this. If no branch exists, ask the user to
   create one in Onshape (Branch from the latest version, name it `claude/<task>`) and paste its URL.
2. **Change CAD by adding features**, never by editing a student's sketches or features.
   Every change must be something a student can see, tweak, suppress or delete.
3. **Ask before every write** (materials, features, Feature Studio pushes, versions), stating
   the document, workspace and exactly what will change.
4. **Rate limit: 10 API calls per rolling minute** (enforced in the client, shared by the MCP
   server and scripts). Onshape also has a yearly per-plan API quota, so:
   - Prefer one FeatureScript query (`run_fs_query`) over many small calls.
   - Don't re-fetch what you already have this session; the cache handles unchanged documents.
   - Don't trigger rebuilds or renders you don't need. Render only when a visual helps.
   - Batch work (whole-robot weight checks, many part studios) goes through `scripts/onshape_cli.py`.

## Skills

Use the skill for each workflow — they hold the step-by-step procedures:

| Skill | When |
|---|---|
| `design-review` | Review a part studio or assembly against `standards/design-rules.md` |
| `lighten` | Suggest or apply plate lightening |
| `materials` | Find parts without materials and assign team materials |
| `weight-check` | Total system / robot weight against budgets |
| `new-feature` | Write or change FeatureScript in `featurescript/` and push it to Onshape |

## FeatureScript conventions (`featurescript/`)

- One feature per file, `camelCase` feature names, `Feature Type Name` in Title Case.
- Every numeric input is a parameter with a `LengthBoundSpec`/`AngleBoundSpec`; no magic numbers.
- Use `op*` functions on `id + "subId"` and clean up helper bodies (sketches, tools) before returning.
- Throw `regenError` with the parameter name when inputs can't work, so the user sees which field.
- Keep the `FeatureScript NNNN;` header and std import versions as-is locally —
  `push_feature_studio` rewrites them to match the target Feature Studio.
- Read-only analysis snippets for `run_fs_query` live in `featurescript/queries/` as a single
  `function(context is Context, queries is map) { ... }` lambda.

## Output

- Review reports go to `reviews/<system>-<YYYY-MM-DD>.md` (committed).
- Renders and API cache go to `.cache/` (ignored). Exports go to `exports/` (ignored).
- Units in reports: millimetres and kilograms.
