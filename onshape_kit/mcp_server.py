"""MCP server exposing the team's Onshape design-assistant tools to Claude.

Run by Claude Code from .mcp.json:  python -m onshape_kit.mcp_server
Read tools are safe to auto-approve; write tools are set to "ask" in .claude/settings.json
and are additionally limited to `claude/...` branch workspaces by the client.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from mcp.server.mcpserver import MCPServer  # noqa: E402

from onshape_kit import ops  # noqa: E402

mcp = MCPServer("onshape")


# ---- read tools ---------------------------------------------------------------------------

@mcp.tool()
def list_elements(url: str) -> list:
    """List a document's tabs (part studios, assemblies, Feature Studios) with their URLs."""
    return ops.list_elements(url)


@mcp.tool()
def list_workspaces(url: str) -> list:
    """List a document's workspaces and whether Claude may write to each (name starts with 'claude/')."""
    return ops.list_workspaces(url)


@mcp.tool()
def part_studio_summary(url: str) -> list:
    """Parts in a part studio with name, material and mass (kg). Parts without material have no mass."""
    return ops.part_studio_summary(url)


@mcp.tool()
def plate_report(url: str) -> list:
    """Measure every body as a plate: thickness, size, hole diameters (mm), whether thickness matches
    stocked material, and the faceId to use with add_lighten_feature. One API call."""
    return ops.plate_report(url)


@mcp.tool()
def list_features(url: str) -> list:
    """A part studio's feature list with names, types, suppression and error status."""
    return ops.features(url)


@mcp.tool()
def run_fs_query(url: str, query: str) -> object:
    """Run a read-only FeatureScript lambda in a part studio. `query` is a file name from
    featurescript/queries/ (e.g. 'plate_summary') or the lambda source itself. Results are in SI units."""
    return ops.run_fs_query(url, query)


@mcp.tool()
def render(url: str, view: str = "isometric", kind: str = "partstudio") -> str:
    """Render a part studio or assembly (kind='assembly') to PNG and return the file path.
    Views: isometric, front, back, top, bottom, left, right. Open the path with the Read tool to see it."""
    return ops.render(url, view, kind)


@mcp.tool()
def assembly_mass(url: str) -> dict:
    """Total mass (kg) of an assembly."""
    return ops.assembly_mass(url)


@mcp.tool()
def weight_check(system: str | None = None) -> dict:
    """Mass of each configured system (or one system) against its budget in onshape.config.json."""
    return ops.weight_check(system)


@mcp.tool()
def api_usage() -> dict:
    """How many Onshape API calls are used in the current rate-limit window."""
    return ops.api_usage()


# ---- write tools (claude/ workspaces only) -------------------------------------------------

@mcp.tool()
def set_material(url: str, part_id: str, material: str) -> dict:
    """Assign a team material from standards/materials.json (key, e.g. 'aluminum') to one part.
    Only works in a claude/ branch workspace."""
    return ops.set_material(url, part_id, material)


@mcp.tool()
def add_lighten_feature(url: str, face_id: str, shape: str = "HEXAGON", pocket_size: str = "15 mm",
                        web_width: str = "4 mm", edge_margin: str = "5 mm", hole_margin: str = "6 mm",
                        corner_radius: str = "1.5 mm") -> dict:
    """Add the team's Lighten Plate feature to one plate face (faceId from plate_report).
    shape: HEXAGON or CIRCLE. Sizes are Onshape expressions like '4 mm'. claude/ workspaces only."""
    return ops.add_lighten_feature(url, face_id, shape, pocket_size, web_width, edge_margin,
                                   hole_margin, corner_radius)


@mcp.tool()
def add_feature(url: str, feature: dict) -> dict:
    """Append a raw BTMFeature JSON to a part studio. Prefer add_lighten_feature when it fits.
    claude/ workspaces only."""
    return ops.add_feature(url, feature)


@mcp.tool()
def push_feature_studio(url: str, file: str) -> dict:
    """Replace a Feature Studio's code with a local file from featurescript/ (std version is matched
    to the target automatically). claude/ workspaces or unguarded library documents only."""
    return ops.push_feature_studio(url, file)


@mcp.tool()
def create_version(url: str, name: str, description: str = "") -> dict:
    """Create a named version of a workspace (a safe snapshot, e.g. before branching)."""
    return ops.create_version(url, name, description)


if __name__ == "__main__":
    mcp.run()
