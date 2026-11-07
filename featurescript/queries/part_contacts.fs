// Read-only query for run_fs_query: which parts touch which, and the holes at each joint.
// For every pair of modifiable solid bodies closer than CONTACT_TOL: both names and part ids,
// the gap, and for each side the diameters of its cylindrical faces (bolt/rivet holes, bores)
// that lie within CONTACT_TOL of the other body — i.e. the fasteners holding the joint.
// Bounding boxes are compared first so large studios don't run evDistance on every pair.
// All values in SI units.
function(context is Context, queries is map)
{
    const CONTACT_TOL = 0.0005 * meter;

    var bodies = [];
    for (var body in evaluateQuery(context, qAllModifiableSolidBodies()))
    {
        bodies = append(bodies, {
            "query" : body,
            "partId" : transientQueriesToStrings(body),
            "name" : getProperty(context, { "entity" : body, "propertyType" : PropertyType.NAME }),
            "box" : evBox3d(context, { "topology" : body, "tight" : false })
        });
    }

    var contacts = [];
    for (var i = 0; i < size(bodies); i += 1)
    {
        for (var j = i + 1; j < size(bodies); j += 1)
        {
            const a = bodies[i];
            const b = bodies[j];

            var boxesMeet = true;
            for (var k = 0; k < 3; k += 1)
            {
                if (a.box.minCorner[k] > b.box.maxCorner[k] + CONTACT_TOL ||
                    b.box.minCorner[k] > a.box.maxCorner[k] + CONTACT_TOL)
                    boxesMeet = false;
            }
            if (!boxesMeet)
                continue;

            const gap = evDistance(context, { "side0" : a.query, "side1" : b.query }).distance;
            if (gap > CONTACT_TOL)
                continue;

            // holes[0]: holes in a near b; holes[1]: holes in b near a
            var holes = [[], []];
            const sides = [[a.query, b.query], [b.query, a.query]];
            for (var s = 0; s < 2; s += 1)
            {
                for (var cyl in evaluateQuery(context, qGeometry(qOwnedByBody(sides[s][0], EntityType.FACE), GeometryType.CYLINDER)))
                {
                    if (evDistance(context, { "side0" : cyl, "side1" : sides[s][1] }).distance <= CONTACT_TOL)
                        holes[s] = append(holes[s], 2 * evSurfaceDefinition(context, { "face" : cyl }).radius);
                }
            }

            contacts = append(contacts, {
                "a" : a.name, "aId" : a.partId,
                "b" : b.name, "bId" : b.partId,
                "gap" : gap,
                "holesInA" : holes[0],
                "holesInB" : holes[1]
            });
        }
    }
    return { "partCount" : size(bodies), "contacts" : contacts };
}
