// Read-only query for run_fs_query / plate_report.
// For every modifiable solid body: name, part id, volume, and — measured against its largest
// flat face — thickness, length, width, that face's id (for add_lighten_feature) and the
// diameters of holes running through the plate. All values in SI units.
function(context is Context, queries is map)
{
    var bodies = [];
    for (var body in evaluateQuery(context, qAllModifiableSolidBodies()))
    {
        var entry = {
            "partId" : transientQueriesToStrings(body),
            "name" : getProperty(context, { "entity" : body, "propertyType" : PropertyType.NAME }),
            "volume" : evVolume(context, { "entities" : body })
        };

        var largest = undefined;
        var largestArea = 0 * meter ^ 2;
        for (var face in evaluateQuery(context, qGeometry(qOwnedByBody(body, EntityType.FACE), GeometryType.PLANE)))
        {
            const area = evArea(context, { "entities" : face });
            if (area > largestArea)
            {
                largestArea = area;
                largest = face;
            }
        }

        if (largest != undefined)
        {
            const facePlane = evPlane(context, { "face" : largest });
            const bounds = evBox3d(context, { "topology" : body, "cSys" : planeToCSys(facePlane), "tight" : true });
            entry.faceId = transientQueriesToStrings(largest);
            entry.faceArea = largestArea;
            entry.thickness = bounds.maxCorner[2] - bounds.minCorner[2];
            const dx = bounds.maxCorner[0] - bounds.minCorner[0];
            const dy = bounds.maxCorner[1] - bounds.minCorner[1];
            entry.length = max(dx, dy);
            entry.width = min(dx, dy);

            var holes = [];
            for (var cyl in evaluateQuery(context, qGeometry(qOwnedByBody(body, EntityType.FACE), GeometryType.CYLINDER)))
            {
                const surface = evSurfaceDefinition(context, { "face" : cyl });
                if (parallelVectors(surface.coordSystem.zAxis, facePlane.normal))
                    holes = append(holes, 2 * surface.radius);
            }
            entry.holes = holes;
        }
        bodies = append(bodies, entry);
    }
    return bodies;
}
