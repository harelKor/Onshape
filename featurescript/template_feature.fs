FeatureScript 2493;
// The header and std version are placeholders: push_feature_studio rewrites them to match
// the target Feature Studio, so local files never need manual version bumps.
import(path : "onshape/std/common.fs", version : "2493.0");

/**
 * TEMPLATE — copy this file to start a new feature.
 *
 * Mounting Hole: cuts through-holes of one diameter at selected points on a flat plate.
 * Shows the standard shape of a team feature:
 *   1. bound specs for every numeric input (no magic numbers)
 *   2. precondition = the feature dialog
 *   3. body: evaluate inputs -> build helper geometry -> op* calls -> clean up helpers
 *   4. regenError naming the field the user has to fix
 */

const HOLE_DIAMETER_BOUNDS = { (meter) : [0.0005, 0.005, 0.2], (millimeter) : 5, (inch) : 0.201 } as LengthBoundSpec;

annotation { "Feature Type Name" : "Mounting Hole",
             "Feature Type Description" : "Cuts through-holes at selected points on a flat plate face." }
export const mountingHole = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Plate face", "Filter" : EntityType.FACE && GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
        definition.face is Query;

        annotation { "Name" : "Hole centers", "Filter" : EntityType.VERTEX }
        definition.centers is Query;

        annotation { "Name" : "Diameter" }
        isLength(definition.diameter, HOLE_DIAMETER_BOUNDS);
    }
    {
        const points = evaluateQuery(context, definition.centers);
        if (size(points) == 0)
            throw regenError("Select at least one hole center.", ["centers"]);

        const facePlane = evPlane(context, { "face" : definition.face });
        const plate = qOwnerBody(definition.face);
        const thickness = thicknessAlongNormal(context, plate, facePlane);

        // Sketch just above the face and cut slightly past the far side so holes go clean through.
        const clearance = 0.1 * millimeter;
        const sketchPlane = plane(facePlane.origin + facePlane.normal * clearance, facePlane.normal, facePlane.x);
        const sketch = newSketchOnPlane(context, id + "sketch", { "sketchPlane" : sketchPlane });
        for (var i = 0; i < size(points); i += 1)
        {
            const center = worldToPlane(sketchPlane, evVertexPoint(context, { "vertex" : points[i] }));
            skCircle(sketch, "hole" ~ i, { "center" : center, "radius" : definition.diameter / 2 });
        }
        skSolve(sketch);

        opExtrude(context, id + "tools", {
                    "entities" : qSketchRegion(id + "sketch"),
                    "direction" : -facePlane.normal,
                    "endBound" : BoundingType.BLIND,
                    "endDepth" : thickness + 2 * clearance
                });
        opDeleteBodies(context, id + "deleteSketch", { "entities" : qCreatedBy(id + "sketch", EntityType.BODY) });

        opBoolean(context, id + "cut", {
                    "tools" : qCreatedBy(id + "tools", EntityType.BODY),
                    "targets" : plate,
                    "operationType" : BooleanOperationType.SUBTRACTION
                });
    }, {});

/** Extent of a body along a plane's normal — the thickness, for a flat plate. */
function thicknessAlongNormal(context is Context, body is Query, facePlane is Plane) returns ValueWithUnits
{
    const box = evBox3d(context, { "topology" : body, "cSys" : planeToCSys(facePlane), "tight" : true });
    return box.maxCorner[2] - box.minCorner[2];
}
