/**
 *
 * Lightening Generator FeatureScript
 * Created By Evan Fish, Designer, FRC Team 2471
 * Find Me On ChiefDelphi @Mr_Fishy
 *
 * Edited By FRC 1690 Orbit — added Automatic rib patterns (truss around holes, Warren truss,
 * grid, triangle grid), and Extrude-style depth options: Opposite direction, Symmetric (Blind)
 * and Second end position. Manual mode and single-ended depths are unchanged, so existing
 * features rebuild the same.
 *
 * How it works: the face is shrunk inward by (wall + tool radius) — that already leaves a wall
 * along every edge and a ring around every hole. Rib bars are then subtracted from that pocket
 * tool and the result is grown back by the tool radius so every inside corner is millable.
 * Manual mode takes the rib bars from sketch lines; Automatic mode generates the lines.
 *
 */

FeatureScript 1560;
import(path : "onshape/std/geometry.fs", version : "1560.0");

const WALL_BOUNDS = { (inch) : [0.001, 0.25, 300] } as LengthBoundSpec;
const TOOL_BOUNDS = { (inch) : [0.001, 0.125, 300] } as LengthBoundSpec;
const RIB_BOUNDS = { (inch) : [0.001, 0.2, 300] } as LengthBoundSpec;
const FILLET_BOUNDS = { (inch) : [0.001, 0.15, 300] } as LengthBoundSpec;
const DEPTH_BOUNDS = { (inch) : [0.001, 0.15, 300] } as LengthBoundSpec;

// ----- Automatic rib pattern inputs ----- //
const TRUSS_SPACING_BOUNDS = { (meter) : [0.01, 0.05, 1], (millimeter) : 50, (inch) : 2 } as LengthBoundSpec;
const GRID_CELL_BOUNDS = { (meter) : [0.005, 0.04, 1], (millimeter) : 40, (inch) : 1.5 } as LengthBoundSpec;
const ISOGRID_SIZE_BOUNDS = { (meter) : [0.01, 0.05, 1], (millimeter) : 50, (inch) : 2 } as LengthBoundSpec;
const TRUSS_ANGLE_BOUNDS = { (degree) : [20, 60, 80] } as AngleBoundSpec;
const PATTERN_ANGLE_BOUNDS = { (degree) : [-180, 0, 180] } as AngleBoundSpec;

// ----- Internal tolerances for the rib generator (not design dimensions) ----- //
const FULL_CIRCLE_TOLERANCE = 0.01;               // radians short of 2*PI still counted as a full hole
const HOLE_PROBE_DISTANCE = 0.01 * millimeter;    // step outside a circle to tell holes from a round outer edge
const OUTLINE_SAMPLE_SPACING = 10 * millimeter;   // outline sampling for the pattern bounding box
const FILL_CLEARANCE = 0.6;                       // interior truss nodes keep this fraction of the spacing from other nodes
const MAX_TRUSS_NODES = 250;                      // regen-time safety limit
const SUPER_TRIANGLE_SCALE = 20;
const DEGENERATE_DETERMINANT = 1e-14;

export enum LightenBoundingType
{
    annotation { "Name" : "Blind" }
    BLIND,
    annotation { "Name" : "Through" }
    THROUGH,
    annotation { "Name" : "Up to face" }
    UP_TO_SURFACE,
    annotation { "Name" : "Up to vertex" }
    UP_TO_VERTEX
}

export enum lightenType
{
    annotation { "Name" : "Manual" }
    MANUAL,
    annotation { "Name" : "Automatic" }
    AUTO
}

export enum AutoRibPattern
{
    annotation { "Name" : "Truss around holes" }
    TRUSS,
    annotation { "Name" : "Warren truss (arms)" }
    WARREN,
    annotation { "Name" : "Grid" }
    GRID,
    annotation { "Name" : "Triangle grid" }
    ISOGRID
}

export predicate isManual(definition is map)
{
    definition.lighten == lightenType.MANUAL;
}

export predicate isAuto(definition is map)
{
    definition.lighten == lightenType.AUTO;
}

export predicate isSymmetric(definition is map)
{
    definition.endBound == LightenBoundingType.BLIND;
    definition.symmetric == true;
}

annotation {  "Feature Type Name" : "PartLighten",
        "Feature Name Template" : "Part Lightening - #ribs Ribs",
        "Feature Type Description" : "Lightens part given selections of lines to create ribs along, or generates the ribs automatically (truss around holes, Warren truss, grid, triangle grid). Checks most cases to produce a machinable part.",
    // "Description Image" : DescriptionNameSpace::BLOB_DATA
    }
export const lighteningGen = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Lightening type", "UIHint" : UIHint.HORIZONTAL_ENUM, "Default" : lightenType.MANUAL }
        definition.lighten is lightenType;

        annotation { "Name" : "Face to lighten", "Filter" : EntityType.FACE && GeometryType.PLANE && SketchObject.NO, "MaxNumberOfPicks" : 1 }
        definition.face is Query;

        annotation { "Name" : "Exclude face regions" }
        definition.exclude is boolean;

        if (definition.exclude)
        {
            annotation { "Name" : "Exlude regions", "Filter" : EntityType.FACE && GeometryType.PLANE }
            definition.excludeRegions is Query;
        }

        if (isManual(definition))
        {
            annotation { "Name" : "Rib lines", "Filter" : EntityType.EDGE && SketchObject.YES }
            definition.locations is Query;
        }

        if (isAuto(definition))
        {
            annotation { "Name" : "Rib pattern", "Default" : AutoRibPattern.TRUSS, "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
            definition.pattern is AutoRibPattern;

            if (definition.pattern == AutoRibPattern.TRUSS)
            {
                annotation { "Name" : "Truss node spacing", "Description" : "Ribs join every hole and points along the edges and inside the plate about this far apart. Pockets come out as triangles about this size.", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
                isLength(definition.trussSpacing, TRUSS_SPACING_BOUNDS);
            }
            else if (definition.pattern == AutoRibPattern.WARREN)
            {
                annotation { "Name" : "Truss angle", "Description" : "Angle of the zigzag ribs from the plate's long direction", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
                isAngle(definition.trussAngle, TRUSS_ANGLE_BOUNDS);
            }
            else if (definition.pattern == AutoRibPattern.GRID)
            {
                annotation { "Name" : "Cell size", "Description" : "Rounded to fit a whole number of cells across the plate", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
                isLength(definition.cellSize, GRID_CELL_BOUNDS);
            }
            else if (definition.pattern == AutoRibPattern.ISOGRID)
            {
                annotation { "Name" : "Triangle size", "Description" : "Side length of the triangle lattice", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
                isLength(definition.triangleSize, ISOGRID_SIZE_BOUNDS);
            }

            if (definition.pattern != AutoRibPattern.TRUSS)
            {
                annotation { "Name" : "Pattern angle", "Description" : "Rotation from the face's longest straight edge" }
                isAngle(definition.patternAngle, PATTERN_ANGLE_BOUNDS);
            }

            annotation { "Name" : "Extra rib lines", "Filter" : EntityType.EDGE && SketchObject.YES }
            definition.extraLocations is Query;
        }

        annotation { "Name" : "Thickness of walls", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
        isLength(definition.thickness, WALL_BOUNDS);

        annotation { "Name" : "Thickness of ribs", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
        isLength(definition.ribThickness, WALL_BOUNDS);

        annotation { "Name" : "Overide fillet radius", "Default" : false }
        definition.overide is boolean;

        if (definition.overide)
        {
            annotation { "Name" : "Fillet radius" }
            isLength(definition.radius, FILLET_BOUNDS);

        }
        else
        {
            annotation { "Name" : "Tool diameter", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
            isLength(definition.diameter, TOOL_BOUNDS);
        }

        annotation { "Name" : "Lighten multiple bodies" }
        definition.multiple is boolean;

        if (definition.multiple)
        {
            annotation { "Name" : "Parts to lighten", "Filter" : BodyType.SOLID && EntityType.BODY }
            definition.entities is Query;
        }

        annotation { "Name" : "End type", "UIHint" : "REMEMBER_PREVIOUS_VALUE" }
        definition.endBound is LightenBoundingType;

        if (definition.endBound == LightenBoundingType.BLIND)
        {
            annotation { "Name" : "Cut depth", "UIHint" : "REMEMBER_PREVIOUS_VALUE" }
            isLength(definition.depth, NONNEGATIVE_LENGTH_BOUNDS);
        }
        else if (definition.endBound == LightenBoundingType.UP_TO_SURFACE)
        {
            annotation { "Name" : "Up to face", "Filter" : EntityType.FACE && GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
            definition.endBoundEntityFace is Query;
        }
        else if (definition.endBound == LightenBoundingType.UP_TO_VERTEX)
        {
            annotation { "Name" : "Up to vertex", "Filter" : EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
            definition.endBoundEntityVertex is Query;
        }

        annotation { "Name" : "Opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.oppositeDirection is boolean;

        if (definition.endBound == LightenBoundingType.BLIND)
        {
            annotation { "Name" : "Symmetric", "Description" : "Cut half the depth to each side of the face" }
            definition.symmetric is boolean;
        }

        if (!isSymmetric(definition))
        {
            annotation { "Name" : "Second end position", "Description" : "Also cut out of the other side of the face" }
            definition.hasSecondDirection is boolean;

            if (definition.hasSecondDirection)
            {
                annotation { "Name" : "Second end type", "UIHint" : "REMEMBER_PREVIOUS_VALUE" }
                definition.secondEndBound is LightenBoundingType;

                if (definition.secondEndBound == LightenBoundingType.BLIND)
                {
                    annotation { "Name" : "Second cut depth", "UIHint" : "REMEMBER_PREVIOUS_VALUE" }
                    isLength(definition.secondDepth, NONNEGATIVE_LENGTH_BOUNDS);
                }
                else if (definition.secondEndBound == LightenBoundingType.UP_TO_SURFACE)
                {
                    annotation { "Name" : "Second up to face", "Filter" : EntityType.FACE && GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                    definition.secondEndBoundEntityFace is Query;
                }
                else if (definition.secondEndBound == LightenBoundingType.UP_TO_VERTEX)
                {
                    annotation { "Name" : "Second up to vertex", "Filter" : EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
                    definition.secondEndBoundEntityVertex is Query;
                }
            }
        }
    }
    {
        definition.entity = qOwnerBody(definition.face);
        if (definition.multiple)
        {
            definition.entity = definition.entities;
        }
        var startVolume = evVolume(context, { "entities" : definition.entity });
        // ----- Rounds Up Tool Radius For Better Machining ----- //
        var toolRad = definition.radius;
        if (!definition.overide)
        {
            toolRad = round((definition.diameter / 2) + (0.01 * inch), 0.001 * inch);
        }
        // ----- Gets Plane Of Selected Face ----- //
        const sPlane = evPlane(context, { "face" : definition.face });
        // ----- Generates Automatic Rib Lines (before any geometry changes, so errors are shown) ----- //
        definition.autoSegments = [];
        if (isAuto(definition))
        {
            definition.autoSegments = autoRibSegments(context, definition, sPlane, toolRad);
            if (size(definition.autoSegments) == 0)
            {
                throw regenError("No ribs fit this face with the chosen pattern. Try a smaller size.", [autoPatternParameter(definition)]);
            }
        }
        // ----- Depth Work: how far the cut goes on each side of the face ----- //
        // "forward" is into the part (or out of it with Opposite direction), "backward" is the second end.
        const forwardDir = definition.oppositeDirection == true ? sPlane.normal : -sPlane.normal;
        var depths = { "direction" : forwardDir, "forward" : 0 * meter, "backward" : 0 * meter };
        if (isSymmetric(definition))
        {
            depths.forward = definition.depth / 2;
            depths.backward = definition.depth / 2;
        }
        else
        {
            depths.forward = endDistance(context, definition, forwardDir, definition.endBound, definition.depth,
                definition.endBoundEntityFace, definition.endBoundEntityVertex, "endBoundEntityFace");
            if (definition.hasSecondDirection == true)
            {
                depths.backward = endDistance(context, definition, -forwardDir, definition.secondEndBound, definition.secondDepth,
                    definition.secondEndBoundEntityFace, definition.secondEndBoundEntityVertex, "secondEndBoundEntityFace");
            }
        }
        if (depths.forward + depths.backward < TOLERANCE.zeroLength * meter)
        {
            throw regenError("Cut depth is zero.", isSymmetric(definition) || definition.endBound == LightenBoundingType.BLIND ? ["depth"] : ["endBound"]);
        }
        var part = baseOperations(context, id, definition.entity, sPlane, toolRad, definition, depths);
        setFeatureComputedParameter(context, id, {
                    "name" : "ribs",
                    "value" : size(evaluateQuery(context, ribEdgeQuery(definition))) + size(definition.autoSegments)
                });
        try silent
        {
            if (size(evaluateQuery(context, part)) != 1 && !definition.multiple)
            {
                reportFeatureWarning(context, id, "Lightening results in more than one part, try adding more ribs or cut a partial depth.");
            }
            var endVolume = evVolume(context, { "entities" : part });
            if (!featureHasNonTrivialStatus(context, id))
            {
                reportFeatureInfo(context, id, "Weight of parts reduced by " ~ roundToPrecision((1 - (endVolume / startVolume)) * 100, 1) ~ "%.");
            }
        }
    });

/**
 * Distance from the face to one end of the cut, measured along `direction`.
 * Same measurements as the original single-ended feature, so existing features keep their depth.
 */
function endDistance(context is Context, definition is map, direction is Vector, boundType is LightenBoundingType, depth,
    boundFace, boundVertex, faceParameter is string) returns ValueWithUnits
{
    if (boundType == LightenBoundingType.BLIND)
    {
        return depth;
    }
    if (boundType == LightenBoundingType.THROUGH)
    {
        const farthest = qFarthestAlong(qOwnedByBody(definition.entity, EntityType.EDGE), direction);
        return evDistance(context, { "side0" : definition.face, "side1" : farthest, "maximum" : true }).distance;
    }
    var endBound = boundVertex;
    if (boundType == LightenBoundingType.UP_TO_SURFACE)
    {
        // ----- Up To Surface Plane must be Parallel for Distance Measurement ----- //
        if (isQueryEmpty(context, qParallelPlanes(boundFace, evPlane(context, { "face" : definition.face }))))
        {
            throw regenError("Face is not parallel to part.", [faceParameter], boundFace);
        }
        endBound = boundFace;
    }
    return evDistance(context, { "side0" : definition.face, "extendSide0" : true, "side1" : endBound, "extendSide1" : true }).distance;
}

/**
 * Sketch edges that become ribs: the rib lines in Manual mode, the extra rib lines in Automatic mode.
 */
function ribEdgeQuery(definition is map) returns Query
{
    const edges = isManual(definition) ? definition.locations : definition.extraLocations;
    return edges == undefined ? qNothing() : edges;
}

/**
 * Takes the part, selection face, toolRad, definition, and depths
 * ({ direction, forward, backward } from the face).
 *
 * Returns resulting part.
 */
function baseOperations(context is Context, id is Id, part, face, toolRad, definition, depths is map)
{
    var toDelete = new box([]);

    const partOffset = definition.thickness + toolRad;

    try
    {
        opExtrude(context, id + "secondExtrude", {
                    "entities" : definition.face,
                    "direction" : depths.direction,
                    "endBound" : BoundingType.BLIND,
                    "endDepth" : depths.forward + partOffset,
                    "startBound" : BoundingType.BLIND,
                    "startDepth" : depths.backward + partOffset
                });
        if (definition.exclude)
        {
            opExtrude(context, id + "excludeExtrude", {
                        "entities" : definition.excludeRegions,
                        "direction" : depths.direction,
                        "endBound" : BoundingType.BLIND,
                        "endDepth" : depths.forward + partOffset,
                        "startBound" : BoundingType.BLIND,
                        "startDepth" : depths.backward + partOffset
                    });

            opBoolean(context, id + "excludeBoolean", {
                        "tools" : qCreatedBy(id + "excludeExtrude", EntityType.BODY),
                        "targets" : qCreatedBy(id + "secondExtrude", EntityType.BODY),
                        "operationType" : BooleanOperationType.SUBTRACTION
                    });
        }
    }
    catch
    {
        throw regenError("Failed to extrude and offset pre-shelled bodies.");
    }

    var capEntities = qUnion([qCapEntity(id + "secondExtrude", CapType.EITHER), startTracking(context, qCapEntity(id + "secondExtrude", CapType.EITHER))]);

    capEntities = qUnion([capEntities, startTracking(context, qCapEntity(id + "secondExtrude", CapType.EITHER, EntityType.FACE))]);

    try
    {
        shellAndEncloseWithConcaveFillet(context, id + "secondShell", {
                    "entities" : qCreatedBy(id + "secondExtrude", EntityType.BODY),
                    "offset" : partOffset
                }, toDelete);
    }
    catch
    {
        throw regenError("Failed to shell and offset bodies.");
    }

    try silent
    {
        const ribs = buildRibs(context, id, face, ribEdgeQuery(definition), definition.autoSegments, depths, definition.ribThickness + (toolRad * 2), partOffset);

        if (!isQueryEmpty(context, ribs))
        {
            opBoolean(context, id + "ribBoolean", {
                        "tools" : ribs,
                        "targets" : qCreatedBy(id + "secondShell", EntityType.BODY),
                        "operationType" : BooleanOperationType.SUBTRACTION
                    });
        }
    }

    try
    {
        // Expand the body by toolRadius
        expandWithFillet(context, id + "expandCombined", {
                    "entities" : qCreatedBy(id + "secondShell", EntityType.BODY),
                    "noEditFaces" : qUnion([capEntities, startTracking(context, capEntities)]),
                    "offset" : toolRad
                });

        // For sheet metal capability, use booleanBodies
        booleanBodies(context, id + "booleanFromPart", {
                    "tools" : qCreatedBy(id + "secondShell", EntityType.BODY),
                    "targets" : part,
                    "operationType" : BooleanOperationType.SUBTRACTION
                });
        // Checks for bridges between bodies that do not follow rib requirements
    }
    catch
    {
        throw regenError("Failed to finalize and boolean resulting bodies.");
    }

    try silent
    {
        // Detects for partial connections between walls
        const toFixEdge = qAdjacent(qUnion(sharpEdgeCheck(context, qCreatedBy(id + "booleanFromPart", EntityType.EDGE)->qParallelEdges(face.normal))), AdjacencyType.EDGE, EntityType.FACE)->qSubtraction(qParallelPlanes(qCreatedBy(id + "booleanFromPart", EntityType.FACE), face));

        if (!isQueryEmpty(context, toFixEdge))
        {
            // debug(context, toFix, DebugColor.RED);
            try silent
            {
                opDeleteFace(context, id + "fixFaces", {
                            "deleteFaces" : toFixEdge,
                            "includeFillet" : false,
                            "capVoid" : false,
                            "leaveOpen" : false
                        });
            }
        }
    }

    opDeleteBodies(context, id + "delete", { "entities" : qUnion(toDelete[]) });

    return part;
}

/**
 * Builds rib bars from sketch edges (lines and arcs) plus generated 2D segments [start, end]
 * in the face plane's coordinates. Returns the rib bodies, or qNothing() if there are none.
 */
function buildRibs(context is Context, id is Id, plane, ribEdges is Query, segments is array, depths is map, thickness, extDist) returns Query
{
    const lines = evaluateQuery(context, ribEdges);
    const length = thickness / 2;
    var toExtrude is array = [];
    var toDelete = new box([]);

    for (var i, rib in lines)
    {
        var sketch = newSketchOnPlane(context, id + (i ~ "ribSketch"), { "sketchPlane" : plane });
        if (isQueryEmpty(context, qGeometry(rib, GeometryType.ARC)))
        {
            const tangentLines = evEdgeTangentLines(context, { "edge" : rib, "parameters" : [0, 1] });
            const startPoint2D = worldToPlane(plane, tangentLines[0].origin);
            const endPoint2D = worldToPlane(plane, tangentLines[1].origin);
            const longDir = normalize(endPoint2D - startPoint2D);
            const thickDir = vector(-longDir[1], longDir[0]);
            var corners = [startPoint2D, startPoint2D, endPoint2D, endPoint2D];
            corners[0] += thickDir * length;
            corners[1] += -thickDir * length;
            corners[2] += -thickDir * length;
            corners[3] += thickDir * length;
            corners = append(corners, corners[0]); // Close the polyline
            skPolyline(sketch, i ~ "rectangle", { "points" : corners });
        }
        else if (!isQueryEmpty(context, qGeometry(rib, GeometryType.ARC)))
        {
            const tangentArc = evEdgeTangentLines(context, { "edge" : rib, "parameters" : [0, 0.5, 1] });
            const arcOrigin = worldToPlane(plane, evCurveDefinition(context, { "edge" : rib }).coordSystem.origin);
            const startPoint2D = worldToPlane(plane, tangentArc[0].origin);
            const startVec = vector((arcOrigin[0] - startPoint2D[0]) / sqrt((arcOrigin[0] - startPoint2D[0]) ^ 2 + (arcOrigin[1] - startPoint2D[1]) ^ 2), (arcOrigin[1] - startPoint2D[1]) / sqrt((arcOrigin[0] - startPoint2D[0]) ^ 2 + (arcOrigin[1] - startPoint2D[1]) ^ 2));
            const midPoint2D = worldToPlane(plane, tangentArc[1].origin);
            const midVec = vector((arcOrigin[0] - midPoint2D[0]) / sqrt((arcOrigin[0] - midPoint2D[0]) ^ 2 + (arcOrigin[1] - midPoint2D[1]) ^ 2), (arcOrigin[1] - midPoint2D[1]) / sqrt((arcOrigin[0] - midPoint2D[0]) ^ 2 + (arcOrigin[1] - midPoint2D[1]) ^ 2));
            const endPoint2D = worldToPlane(plane, tangentArc[2].origin);
            const endVec = vector((arcOrigin[0] - endPoint2D[0]) / sqrt((arcOrigin[0] - endPoint2D[0]) ^ 2 + (arcOrigin[1] - endPoint2D[1]) ^ 2), (arcOrigin[1] - endPoint2D[1]) / sqrt((arcOrigin[0] - endPoint2D[0]) ^ 2 + (arcOrigin[1] - endPoint2D[1]) ^ 2));
            var thickDir = startVec;
            var points = [startPoint2D, startPoint2D, endPoint2D, endPoint2D, midPoint2D, midPoint2D];
            points[0] += thickDir * length;
            points[1] += -thickDir * length;
            thickDir = endVec;
            points[2] += -thickDir * length;
            points[3] += thickDir * length;
            thickDir = midVec;
            points[4] += -thickDir * length;
            points[5] += thickDir * length;
            skLineSegment(sketch, "line1", { "start" : points[0], "end" : points[1] });
            skLineSegment(sketch, "line2", { "start" : points[2], "end" : points[3] });
            skArc(sketch, "arc1", { "start" : points[0], "mid" : points[5], "end" : points[3] });
            skArc(sketch, "arc2", { "start" : points[1], "mid" : points[4], "end" : points[2] });
        }
        skSolve(sketch);
        toExtrude = append(toExtrude, qSketchRegion(id + (i ~ "ribSketch")));
        toDelete[] = append(toDelete[], qCreatedBy(id + (i ~ "ribSketch")));
    }

    // Generated ribs: one sketch per rib, because rectangles sharing a sketch would also
    // turn the pockets enclosed between them into regions.
    for (var j, segment in segments)
    {
        if (norm(segment[1] - segment[0]) < TOLERANCE.zeroLength * meter)
            continue;
        const sketchId = id + ("autoRib" ~ j);
        var sketch = newSketchOnPlane(context, sketchId, { "sketchPlane" : plane });
        skPolyline(sketch, "rectangle", { "points" : ribRectangle(segment[0], segment[1], length) });
        skSolve(sketch);
        toExtrude = append(toExtrude, qSketchRegion(sketchId));
        toDelete[] = append(toDelete[], qCreatedBy(sketchId));
    }

    if (toExtrude == [])
        return qNothing();

    opExtrude(context, id + "ribExtrude", {
                "entities" : qUnion(toExtrude),
                "direction" : depths.direction,
                "endBound" : BoundingType.BLIND,
                "endDepth" : depths.forward + extDist,
                "startBound" : BoundingType.BLIND,
                "startDepth" : depths.backward + extDist
            });

    opDeleteBodies(context, id + "deleteRibSketchs", { "entities" : qUnion(toDelete[]) });

    return qCreatedBy(id + "ribExtrude", EntityType.BODY);
}

/**
 * Closed rectangle around a 2D segment, `halfWidth` either side. The ends are extended by
 * `halfWidth` too, so ribs meeting at a node overlap instead of leaving a notch.
 */
function ribRectangle(start is Vector, end is Vector, halfWidth is ValueWithUnits) returns array
{
    const along = normalize(end - start);
    const across = vector(-along[1], along[0]) * halfWidth;
    const s = start - along * halfWidth;
    const e = end + along * halfWidth;
    return [s + across, s - across, e - across, e + across, s + across];
}

// =====================================================================================
// Automatic ribs
// =====================================================================================

/**
 * Name of the size parameter of the chosen pattern, for error highlighting.
 */
function autoPatternParameter(definition is map) returns string
{
    if (definition.pattern == AutoRibPattern.TRUSS)
        return "trussSpacing";
    if (definition.pattern == AutoRibPattern.WARREN)
        return "trussAngle";
    if (definition.pattern == AutoRibPattern.GRID)
        return "cellSize";
    return "triangleSize";
}

/**
 * Generates rib segments [start, end] (2D, in the face plane) for the chosen pattern.
 */
function autoRibSegments(context is Context, definition is map, plane is Plane, toolRad is ValueWithUnits) returns array
{
    const isTruss = definition.pattern == AutoRibPattern.TRUSS;
    const shape = analyzeFace(context, definition.face, plane, isTruss ? definition.trussSpacing : OUTLINE_SAMPLE_SPACING);
    const frame = patternFrame(shape, isTruss ? 0 * degree : definition.patternAngle);
    const overhang = definition.ribThickness + 2 * toolRad;

    if (isTruss)
        return trussSegments(context, definition, plane, shape, frame, toolRad);
    if (definition.pattern == AutoRibPattern.WARREN)
        return warrenSegments(frame, definition.trussAngle);
    if (definition.pattern == AutoRibPattern.GRID)
        return gridSegments(frame, definition.cellSize, overhang);
    return isogridSegments(frame, definition.triangleSize, overhang);
}

/**
 * Reads the boundary of a planar face in plane coordinates.
 * Full circles with material all around them are holes; every other edge (outer outline,
 * slots, rounded corners, a round plate's rim) is sampled into points at most `spacing` apart.
 *
 * Returns { "holes" : [{ "center", "radius" }], "edgeSamples" : [[point...] per edge],
 *           "points" : all samples, "longDir" : unit direction of the longest straight edge }.
 */
function analyzeFace(context is Context, face is Query, plane is Plane, spacing is ValueWithUnits) returns map
{
    var circleGroups = {};
    var outlineEdges = [];
    var longDir = vector(1, 0);
    var longest = 0 * meter;

    for (var edge in evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE)))
    {
        const edgeLength = evLength(context, { "entities" : edge });
        const isCircular = !isQueryEmpty(context, qUnion([qGeometry(edge, GeometryType.CIRCLE), qGeometry(edge, GeometryType.ARC)]));
        if (isCircular)
        {
            // Group arcs of the same circle, so a hole split into two edges is still one hole.
            const circle = evCurveDefinition(context, { "edge" : edge });
            const center = worldToPlane(plane, circle.coordSystem.origin);
            const key = roundToPrecision(center[0] / millimeter, 3) ~ "," ~ roundToPrecision(center[1] / millimeter, 3) ~ "," ~ roundToPrecision(circle.radius / millimeter, 3);
            var group = circleGroups[key];
            if (group == undefined)
                group = { "center" : center, "radius" : circle.radius, "sweep" : 0, "edges" : [] };
            group.sweep = group.sweep + edgeLength / circle.radius;
            group.edges = append(group.edges, edge);
            circleGroups[key] = group;
        }
        else
        {
            if (!isQueryEmpty(context, qGeometry(edge, GeometryType.LINE)) && edgeLength > longest)
            {
                const ends = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 1] });
                longDir = normalize(worldToPlane(plane, ends[1].origin) - worldToPlane(plane, ends[0].origin));
                longest = edgeLength;
            }
            outlineEdges = append(outlineEdges, edge);
        }
    }

    var holes = [];
    for (var key, group in circleGroups)
    {
        if (group.sweep > 2 * PI - FULL_CIRCLE_TOLERANCE && isHole(context, face, plane, group.center, group.radius))
            holes = append(holes, { "center" : group.center, "radius" : group.radius });
        else
            outlineEdges = concatenateArrays([outlineEdges, group.edges]);
    }

    var edgeSamples = [];
    var points = [];
    for (var edge in outlineEdges)
    {
        const count = max(1, ceil(evLength(context, { "entities" : edge }) / spacing));
        var parameters = [];
        for (var k = 0; k <= count; k += 1)
            parameters = append(parameters, k / count);
        var samples = [];
        for (var line in evEdgeTangentLines(context, { "edge" : edge, "parameters" : parameters }))
            samples = append(samples, worldToPlane(plane, line.origin));
        edgeSamples = append(edgeSamples, samples);
        points = concatenateArrays([points, samples]);
    }

    return { "holes" : holes, "edgeSamples" : edgeSamples, "points" : points, "longDir" : longDir };
}

/**
 * A full circle is a hole if there is material just outside it (a round outer edge has none).
 */
function isHole(context is Context, face is Query, plane is Plane, center is Vector, radius is ValueWithUnits) returns boolean
{
    const probe = center + vector(radius + HOLE_PROBE_DISTANCE, 0 * meter);
    return isOnFace(context, face, plane, probe);
}

function isOnFace(context is Context, face is Query, plane is Plane, point is Vector) returns boolean
{
    return evDistance(context, { "side0" : planeToWorld(plane, point), "side1" : face }).distance < TOLERANCE.zeroLength * meter;
}

/**
 * Pattern axes (u along the long direction rotated by `angle`, v across) and the face's
 * bounding box in those axes.
 */
function patternFrame(shape is map, angle is ValueWithUnits) returns map
{
    const ax = rotate2D(shape.longDir, angle);
    const ay = vector(-ax[1], ax[0]);
    var corners = [];
    for (var p in shape.points)
        corners = append(corners, p);
    for (var hole in shape.holes)
    {
        corners = append(corners, hole.center + (ax + ay) * hole.radius);
        corners = append(corners, hole.center - (ax + ay) * hole.radius);
    }
    var uMin = undefined;
    var uMax = undefined;
    var vMin = undefined;
    var vMax = undefined;
    for (var p in corners)
    {
        const u = dot(p, ax);
        const v = dot(p, ay);
        uMin = uMin == undefined ? u : min(uMin, u);
        uMax = uMax == undefined ? u : max(uMax, u);
        vMin = vMin == undefined ? v : min(vMin, v);
        vMax = vMax == undefined ? v : max(vMax, v);
    }
    if (uMin == undefined)
        throw regenError("Could not read the outline of the face to lighten.", ["face"]);
    return { "ax" : ax, "ay" : ay, "uMin" : uMin, "uMax" : uMax, "vMin" : vMin, "vMax" : vMax };
}

/**
 * The same frame turned 90 degrees, so u runs along what was v.
 */
function turnFrame(frame is map) returns map
{
    return { "ax" : frame.ay, "ay" : -frame.ax,
            "uMin" : frame.vMin, "uMax" : frame.vMax,
            "vMin" : -frame.uMax, "vMax" : -frame.uMin };
}

function fromFrame(frame is map, u is ValueWithUnits, v is ValueWithUnits) returns Vector
{
    return frame.ax * u + frame.ay * v;
}

function rotate2D(direction is Vector, angle is ValueWithUnits) returns Vector
{
    return vector(direction[0] * cos(angle) - direction[1] * sin(angle), direction[0] * sin(angle) + direction[1] * cos(angle));
}

/**
 * Truss around holes (pattern LT-0): nodes at every hole centre (points on a ring around big
 * bores), along the outline and on a staggered grid inside the plate; ribs are the Delaunay
 * triangulation between them. The wall/ring left by the shell step then wraps every hole.
 */
function trussSegments(context is Context, definition is map, plane is Plane, shape is map, frame is map, toolRad is ValueWithUnits) returns array
{
    const spacing = definition.trussSpacing;
    const mergeDistance = 2 * (definition.thickness + toolRad);
    var nodes = [];

    // Holes first so they win when merging nearby points.
    for (var hole in shape.holes)
    {
        if (hole.radius > spacing / 2)
        {
            const ringRadius = hole.radius + definition.thickness;
            const count = ceil(2 * PI * ringRadius / spacing);
            for (var k = 0; k < count; k += 1)
            {
                const a = (2 * PI * k / count) * radian;
                nodes = addNode(nodes, hole.center + vector(cos(a), sin(a)) * ringRadius, mergeDistance).nodes;
            }
        }
        else
        {
            nodes = addNode(nodes, hole.center, mergeDistance).nodes;
        }
    }

    // Outline points; ribs between neighbours on the same edge would just sit on the wall.
    var onWall = {};
    for (var samples in shape.edgeSamples)
    {
        var previous = -1;
        for (var p in samples)
        {
            const added = addNode(nodes, p, mergeDistance);
            nodes = added.nodes;
            if (previous >= 0 && previous != added.index)
                onWall[edgeKey(previous, added.index)] = true;
            previous = added.index;
        }
    }

    // Staggered interior nodes so big empty areas get triangles too.
    const rowStep = spacing * sqrt(3) / 2;
    const uCenter = (frame.uMin + frame.uMax) / 2;
    const vCenter = (frame.vMin + frame.vMax) / 2;
    const rows = ceil((frame.vMax - frame.vMin) / 2 / rowStep) + 1;
    const columns = ceil((frame.uMax - frame.uMin) / 2 / spacing) + 1;
    for (var row = -rows; row <= rows; row += 1)
    {
        const v = vCenter + row * rowStep;
        if (v <= frame.vMin || v >= frame.vMax)
            continue;
        for (var column = -columns; column <= columns; column += 1)
        {
            const u = uCenter + (column + (abs(row) % 2) / 2) * spacing;
            if (u <= frame.uMin || u >= frame.uMax)
                continue;
            const p = fromFrame(frame, u, v);
            if (!isFarFromNodes(nodes, p, spacing * FILL_CLEARANCE) || !isOnFace(context, definition.face, plane, p))
                continue;
            nodes = append(nodes, p);
        }
        if (size(nodes) > MAX_TRUSS_NODES)
            break;
    }

    if (size(nodes) > MAX_TRUSS_NODES)
        throw regenError("Too many truss nodes for this plate. Increase the truss node spacing.", ["trussSpacing"]);

    var segments = [];
    for (var e in delaunayEdges(nodes))
    {
        if (onWall[edgeKey(e[0], e[1])] == undefined)
            segments = append(segments, [nodes[e[0]], nodes[e[1]]]);
    }
    return segments;
}

/**
 * Warren truss (pattern LT-2): a zigzag between the two long sides of the bounding box,
 * fitted to a whole number of bays.
 */
function warrenSegments(frameIn is map, angle is ValueWithUnits) returns array
{
    var frame = frameIn;
    if (frame.vMax - frame.vMin > frame.uMax - frame.uMin)
        frame = turnFrame(frame);
    const height = frame.vMax - frame.vMin;
    const length = frame.uMax - frame.uMin;
    const bays = max(1, round(length / (height / tan(angle))));
    const step = length / bays;
    var segments = [];
    var previous = fromFrame(frame, frame.uMin, frame.vMin);
    for (var k = 1; k <= bays; k += 1)
    {
        const next = fromFrame(frame, frame.uMin + k * step, k % 2 == 1 ? frame.vMax : frame.vMin);
        segments = append(segments, [previous, next]);
        previous = next;
    }
    return segments;
}

/**
 * Grid (pattern LT-3): ribs in both directions, cell size rounded so the plate holds a whole
 * number of equal cells.
 */
function gridSegments(frame is map, cell is ValueWithUnits, overhang is ValueWithUnits) returns array
{
    var segments = [];
    const uLength = frame.uMax - frame.uMin;
    const vLength = frame.vMax - frame.vMin;
    const uCells = max(1, round(uLength / cell));
    const vCells = max(1, round(vLength / cell));
    for (var k = 1; k < uCells; k += 1)
    {
        const u = frame.uMin + k * uLength / uCells;
        segments = append(segments, [fromFrame(frame, u, frame.vMin - overhang), fromFrame(frame, u, frame.vMax + overhang)]);
    }
    for (var k = 1; k < vCells; k += 1)
    {
        const v = frame.vMin + k * vLength / vCells;
        segments = append(segments, [fromFrame(frame, frame.uMin - overhang, v), fromFrame(frame, frame.uMax + overhang, v)]);
    }
    return segments;
}

/**
 * Triangle grid: three families of ribs at 0, 60 and 120 degrees through a common centre,
 * which makes equilateral triangle pockets of side `side`.
 */
function isogridSegments(frame is map, side is ValueWithUnits, overhang is ValueWithUnits) returns array
{
    const pitch = side * sqrt(3) / 2;
    const center = fromFrame(frame, (frame.uMin + frame.uMax) / 2, (frame.vMin + frame.vMax) / 2);
    const reach = sqrt((frame.uMax - frame.uMin) ^ 2 + (frame.vMax - frame.vMin) ^ 2) / 2 + overhang;
    const lines = ceil(reach / pitch);
    var segments = [];
    for (var familyAngle in [0 * degree, 60 * degree, 120 * degree])
    {
        const along = rotate2D(frame.ax, familyAngle);
        const across = vector(-along[1], along[0]);
        for (var k = -lines; k <= lines; k += 1)
        {
            const base = center + across * (k * pitch);
            segments = append(segments, [base - along * reach, base + along * reach]);
        }
    }
    return segments;
}

// ----- Small helpers for the truss ----- //

function edgeKey(a is number, b is number) returns string
{
    return min(a, b) ~ "_" ~ max(a, b);
}

/**
 * Adds `point` unless a node is already within `mergeDistance`.
 * Returns { "nodes", "index" } where index is the new or the existing nearby node.
 */
function addNode(nodes is array, point is Vector, mergeDistance is ValueWithUnits) returns map
{
    for (var i, node in nodes)
    {
        if (norm(node - point) < mergeDistance)
            return { "nodes" : nodes, "index" : i };
    }
    return { "nodes" : append(nodes, point), "index" : size(nodes) };
}

function isFarFromNodes(nodes is array, point is Vector, clearance is ValueWithUnits) returns boolean
{
    for (var node in nodes)
    {
        if (norm(node - point) < clearance)
            return false;
    }
    return true;
}

/**
 * Delaunay triangulation (Bowyer-Watson) of 2D points with length units.
 * Returns the unique edges as index pairs [i, j].
 */
function delaunayEdges(points is array) returns array
{
    const n = size(points);
    if (n < 2)
        return [];
    if (n == 2)
        return [[0, 1]];

    var pts = [];
    for (var p in points)
        pts = append(pts, vector(p[0] / meter, p[1] / meter));

    var lo = pts[0];
    var hi = pts[0];
    for (var p in pts)
    {
        lo = vector(min(lo[0], p[0]), min(lo[1], p[1]));
        hi = vector(max(hi[0], p[0]), max(hi[1], p[1]));
    }
    const mid = (lo + hi) / 2;
    const big = SUPER_TRIANGLE_SCALE * max(max(hi[0] - lo[0], hi[1] - lo[1]), TOLERANCE.zeroLength);
    pts = append(pts, mid + vector(-big, -big));
    pts = append(pts, mid + vector(big, -big));
    pts = append(pts, mid + vector(0, big));

    var triangles = [makeTriangle(pts, n, n + 1, n + 2)];
    for (var i = 0; i < n; i += 1)
    {
        const p = pts[i];
        var kept = [];
        var cavity = {};
        for (var t in triangles)
        {
            if (squaredNorm(p - t.center) < t.radiusSquared)
            {
                for (var e in [[t.v[0], t.v[1]], [t.v[1], t.v[2]], [t.v[2], t.v[0]]])
                {
                    const key = edgeKey(e[0], e[1]);
                    cavity[key] = { "edge" : e, "uses" : (cavity[key] == undefined ? 0 : cavity[key].uses) + 1 };
                }
            }
            else
            {
                kept = append(kept, t);
            }
        }
        for (var key, entry in cavity)
        {
            if (entry.uses == 1)
                kept = append(kept, makeTriangle(pts, entry.edge[0], entry.edge[1], i));
        }
        triangles = kept;
    }

    var seen = {};
    var edges = [];
    for (var t in triangles)
    {
        for (var e in [[t.v[0], t.v[1]], [t.v[1], t.v[2]], [t.v[2], t.v[0]]])
        {
            if (e[0] >= n || e[1] >= n)
                continue;
            const key = edgeKey(e[0], e[1]);
            if (seen[key] == undefined)
            {
                seen[key] = true;
                edges = append(edges, [min(e[0], e[1]), max(e[0], e[1])]);
            }
        }
    }
    return edges;
}

function makeTriangle(pts is array, a is number, b is number, c is number) returns map
{
    const A = pts[a];
    const B = pts[b];
    const C = pts[c];
    const d = 2 * (A[0] * (B[1] - C[1]) + B[0] * (C[1] - A[1]) + C[0] * (A[1] - B[1]));
    if (abs(d) < DEGENERATE_DETERMINANT)
        return { "v" : [a, b, c], "center" : (A + B + C) / 3, "radiusSquared" : inf };
    const a2 = squaredNorm(A);
    const b2 = squaredNorm(B);
    const c2 = squaredNorm(C);
    const center = vector((a2 * (B[1] - C[1]) + b2 * (C[1] - A[1]) + c2 * (A[1] - B[1])) / d,
                          (a2 * (C[0] - B[0]) + b2 * (A[0] - C[0]) + c2 * (B[0] - A[0])) / d);
    return { "v" : [a, b, c], "center" : center, "radiusSquared" : squaredNorm(A - center) };
}

// =====================================================================================
// Shared helpers (original)
// =====================================================================================

/**
 * Sourced from the Featurescript Lighten by Ilya Baran and Morgan Bartlett
 */
function shellAndEncloseWithConcaveFillet(context is Context, id is Id, definition is map, toDelete is box)
{
    // Fillet concave edges in the parts
    var edgesToFillet = [];
    var edges = evaluateQuery(context, qOwnedByBody(definition.entities, EntityType.EDGE));
    for (var edge in edges)
    {
        if (evEdgeConvexity(context, { "edge" : edge }) == EdgeConvexityType.CONCAVE)
            edgesToFillet = append(edgesToFillet, edge);
    }
    if (edgesToFillet != [])
    {
        opFillet(context, id + "fillet", {
                    "entities" : qUnion(edgesToFillet),
                    "radius" : TOLERANCE.booleanDefaultTolerance * meter
                });
    }
    var filletsToEdit = startTracking(context, qCreatedBy(id + "fillet", EntityType.FACE));
    toDelete[] = append(toDelete[], definition.entities);
    // Shell the parts
    opShell(context, id + "shell", {
                "entities" : definition.entities,
                "thickness" : -definition.offset
            });
    // Enclose the parts, one at a time to avoid a boolean
    for (var i, toEnclose in evaluateQuery(context, definition.entities))
    {
        opEnclose(context, id + "enclose" + unstableIdComponent(i), { "entities" : toEnclose });
    }
    // Modify the fillet to the correct radius
    if (edgesToFillet != [])
    {
        try
        {
            opModifyFillet(context, id + "modifyFillet", {
                        "faces" : qIntersection([filletsToEdit, qCreatedBy(id + "enclose")]),
                        "modifyFilletType" : ModifyFilletType.CHANGE_RADIUS,
                        "radius" : definition.offset
                    });
        }
    }
}

/**
 * Sourced from the Featurescript Lighten by Ilya Baran and Morgan Bartlett
 */
function expandWithFillet(context is Context, id is Id, definition is map)
{
    // Fillet convex edges in the parts
    var edgesToFillet = sharpEdgeCheck(context, qSubtraction(qOwnedByBody(definition.entities, EntityType.EDGE), qAdjacent(definition.noEditFaces, AdjacencyType.EDGE, EntityType.EDGE)));
    if (edgesToFillet != [])
    {
        opFillet(context, id + "fillet", {
                    "entities" : qUnion(edgesToFillet),
                    "radius" : TOLERANCE.booleanDefaultTolerance * meter
                });
    }
    var filletsToEdit = qCreatedBy(id + "fillet", EntityType.FACE);
    // Move the part faces outwards by offset
    opOffsetFace(context, id + "offsetFace", {
                "moveFaces" : qSubtraction(qOwnedByBody(definition.entities, EntityType.FACE), definition.noEditFaces),
                "offsetDistance" : definition.offset
            });
    // Modify the fillet to the correct radius
    if (edgesToFillet != [])
    {
        opModifyFillet(context, id + "modifyFillet", {
                    "faces" : filletsToEdit,
                    "modifyFilletType" : ModifyFilletType.CHANGE_RADIUS,
                    "radius" : definition.offset
                });
    }
}

/**
 * Takes edges and checks for sharp edges.
 *
 * Returns array of sharp edges.
 */
export function sharpEdgeCheck(context is Context, edges is Query) returns array
{
    var arr = [];
    for (var edge in evaluateQuery(context, edges))
    {
        const convexity = evEdgeConvexity(context, { "edge" : edge });
        if (convexity != EdgeConvexityType.SMOOTH)
        {
            arr = append(arr, edge);
        }
    }
    return arr;
}

export function projectToPlane(point is Vector, plane is Plane) returns Vector
{
    return worldToPlane(plane, project(plane, point));
}

export function projectToPlane(plane is Plane, point is Vector) returns Vector
{
    return projectToPlane(point, plane);
}

export function checkGeomType(context is Context, face is Query) returns boolean
{
    if (!isQueryEmpty(context, qGeometry(face, GeometryType.PLANE)))
    {
        return true;
    }
    else if (!isQueryEmpty(context, qGeometry(face, GeometryType.CYLINDER)))
    {
        return false;
    }
}
