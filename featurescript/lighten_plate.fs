FeatureScript 2493;
// Header/std version are rewritten by push_feature_studio to match the target Feature Studio.
import(path : "onshape/std/common.fs", version : "2493.0");

/**
 * Lighten Plate — cuts a lattice of pockets into a flat plate.
 *
 * Pockets sit on a hexagonal lattice spaced so that every pair of neighbours leaves exactly
 * `webWidth` of material. A pocket is kept only if its whole outline stays `edgeMargin` away
 * from the plate's straight/outer edges and `holeMargin` away from circular edges (bolt holes,
 * bearing bores). The check uses the pocket's circumradius, so it is conservative.
 *
 * Assumes a flat plate of uniform thickness (pockets are cut through the full thickness).
 */

export enum PocketShape
{
    annotation { "Name" : "Hexagon" }
    HEXAGON,
    annotation { "Name" : "Circle" }
    CIRCLE
}

const POCKET_SIZE_BOUNDS = { (meter) : [0.002, 0.015, 0.5], (millimeter) : 15, (inch) : 0.6 } as LengthBoundSpec;
const WEB_BOUNDS = { (meter) : [0.0005, 0.004, 0.1], (millimeter) : 4, (inch) : 0.16 } as LengthBoundSpec;
const EDGE_MARGIN_BOUNDS = { (meter) : [0, 0.005, 0.1], (millimeter) : 5, (inch) : 0.2 } as LengthBoundSpec;
const HOLE_MARGIN_BOUNDS = { (meter) : [0, 0.006, 0.1], (millimeter) : 6, (inch) : 0.25 } as LengthBoundSpec;
const CORNER_RADIUS_BOUNDS = { (meter) : [0, 0.0015, 0.05], (millimeter) : 1.5, (inch) : 0.06 } as LengthBoundSpec;

annotation { "Feature Type Name" : "Lighten Plate",
             "Feature Type Description" : "Cuts a lattice of pockets into a flat plate, keeping margins from edges and holes." }
export const lightenPlate = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Plate face", "Filter" : EntityType.FACE && GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
        definition.face is Query;

        annotation { "Name" : "Pocket shape" }
        definition.shape is PocketShape;

        annotation { "Name" : "Pocket size", "Description" : "Hexagon corner-to-corner, or circle diameter" }
        isLength(definition.pocketSize, POCKET_SIZE_BOUNDS);

        annotation { "Name" : "Web width" }
        isLength(definition.webWidth, WEB_BOUNDS);

        annotation { "Name" : "Edge margin" }
        isLength(definition.edgeMargin, EDGE_MARGIN_BOUNDS);

        annotation { "Name" : "Hole margin", "Description" : "Material kept around circular edges (holes, bearing bores)" }
        isLength(definition.holeMargin, HOLE_MARGIN_BOUNDS);

        if (definition.shape == PocketShape.HEXAGON)
        {
            annotation { "Name" : "Corner radius" }
            isLength(definition.cornerRadius, CORNER_RADIUS_BOUNDS);
        }
    }
    {
        const facePlane = evPlane(context, { "face" : definition.face });
        const plate = qOwnerBody(definition.face);
        const thickness = thicknessAlongNormal(context, plate, facePlane);

        const radius = definition.pocketSize / 2;
        // Centre-to-centre distance: hexagons face their neighbours flat-to-flat (flat width = R·√3).
        const pitch = (definition.shape == PocketShape.HEXAGON ? radius * sqrt(3) : 2 * radius) + definition.webWidth;

        const centers = pocketCenters(context, definition, facePlane, radius, pitch);
        if (size(centers) == 0)
            throw regenError("No pockets fit. Reduce the pocket size or the margins.", ["pocketSize"]);

        // Sketch just above the face and cut slightly past the far side so pockets go clean through.
        const clearance = 0.1 * millimeter;
        const sketchPlane = plane(facePlane.origin + facePlane.normal * clearance, facePlane.normal, facePlane.x);
        const sketch = newSketchOnPlane(context, id + "sketch", { "sketchPlane" : sketchPlane });
        for (var i = 0; i < size(centers); i += 1)
        {
            const c = centers[i];
            if (definition.shape == PocketShape.HEXAGON)
                skRegularPolygon(sketch, "pocket" ~ i, {
                            "center" : c,
                            "firstVertex" : c + radius * vector(cos(30 * degree), sin(30 * degree)),
                            "sides" : 6
                        });
            else
                skCircle(sketch, "pocket" ~ i, { "center" : c, "radius" : radius });
        }
        skSolve(sketch);

        opExtrude(context, id + "tools", {
                    "entities" : qSketchRegion(id + "sketch"),
                    "direction" : -facePlane.normal,
                    "endBound" : BoundingType.BLIND,
                    "endDepth" : thickness + 2 * clearance
                });
        opDeleteBodies(context, id + "deleteSketch", { "entities" : qCreatedBy(id + "sketch", EntityType.BODY) });

        if (definition.shape == PocketShape.HEXAGON && definition.cornerRadius > 0 * meter)
        {
            opFillet(context, id + "cornerFillet", {
                        "entities" : qParallelEdges(qCreatedBy(id + "tools", EntityType.EDGE), facePlane.normal),
                        "radius" : definition.cornerRadius
                    });
        }

        const volumeBefore = evVolume(context, { "entities" : plate });
        opBoolean(context, id + "cut", {
                    "tools" : qCreatedBy(id + "tools", EntityType.BODY),
                    "targets" : plate,
                    "operationType" : BooleanOperationType.SUBTRACTION
                });
        const removed = volumeBefore - evVolume(context, { "entities" : plate });
        reportFeatureInfo(context, id, "Cut " ~ size(centers) ~ " pockets, removed " ~
                    roundToPrecision(removed / (centimeter ^ 3), 1) ~ " cm³");
    }, {});

/** Lattice points (2D, in the face plane's coordinates) where a whole pocket fits. */
function pocketCenters(context is Context, definition is map, facePlane is Plane,
    radius is ValueWithUnits, pitch is ValueWithUnits) returns array
{
    const bounds = evBox3d(context, { "topology" : definition.face, "cSys" : planeToCSys(facePlane), "tight" : true });

    const edges = qAdjacent(definition.face, AdjacencyType.EDGE, EntityType.EDGE);
    const holeEdges = qGeometry(edges, GeometryType.CIRCLE);
    const otherEdges = qSubtraction(edges, holeEdges);
    const hasHoles = !isQueryEmpty(context, holeEdges);
    const hasOther = !isQueryEmpty(context, otherEdges);
    const yDir = yAxis(facePlane);

    // Rows are pitch·√3/2 apart and every other row shifts half a pitch: a hexagonal lattice.
    const rowStep = pitch * sqrt(3) / 2;
    var centers = [];
    for (var j = floor(bounds.minCorner[1] / rowStep); j <= ceil(bounds.maxCorner[1] / rowStep); j += 1)
    {
        const v = j * rowStep;
        const shift = (abs(j) % 2) * pitch / 2;
        for (var i = floor((bounds.minCorner[0] - shift) / pitch); i <= ceil((bounds.maxCorner[0] - shift) / pitch); i += 1)
        {
            const u = shift + i * pitch;
            const world = facePlane.origin + u * facePlane.x + v * yDir;

            if (evDistance(context, { "side0" : world, "side1" : definition.face }).distance > TOLERANCE.zeroLength * meter)
                continue; // outside the face, or inside an existing hole
            if (hasOther && evDistance(context, { "side0" : world, "side1" : otherEdges }).distance < radius + definition.edgeMargin)
                continue;
            if (hasHoles && evDistance(context, { "side0" : world, "side1" : holeEdges }).distance < radius + definition.holeMargin)
                continue;

            centers = append(centers, vector(u, v));
        }
    }
    return centers;
}

/** Extent of a body along a plane's normal — the thickness, for a flat plate. */
function thicknessAlongNormal(context is Context, body is Query, facePlane is Plane) returns ValueWithUnits
{
    const bounds = evBox3d(context, { "topology" : body, "cSys" : planeToCSys(facePlane), "tight" : true });
    return bounds.maxCorner[2] - bounds.minCorner[2];
}
