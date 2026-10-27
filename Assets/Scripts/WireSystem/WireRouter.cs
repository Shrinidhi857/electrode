using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static orthogonal (Lucidchart-style) 3D wire router.
/// Computes strictly axis-aligned (90-degree elbows) paths between terminals or to a free drag target.
/// </summary>
public static class WireRouter
{
    /// <summary>
    /// Computes a list of axis-aligned 3D points routing from start to end.
    /// </summary>
    /// <param name="start">Start terminal world position.</param>
    /// <param name="startDir">Start terminal exit direction.</param>
    /// <param name="end">End terminal world position (or free drag target).</param>
    /// <param name="endDir">End terminal exit direction (Vector3.zero for free dragging end).</param>
    /// <param name="stub">Distance to travel straight out of a terminal before turning.</param>
    /// <returns>List of axis-aligned waypoints with collinear and duplicate points removed.</returns>
    public static List<Vector3> Route(Vector3 start, Vector3 startDir, Vector3 end, Vector3 endDir, float stub)
    {
        List<Vector3> rawPoints = new List<Vector3>(16);

        // 1. Snap exit directions to nearest world axis (+/- X, +/- Y, or +/- Z)
        Vector3 sDir = SnapToWorldAxis(startDir);
        if (sDir == Vector3.zero) sDir = Vector3.up;

        Vector3 sStub = start + sDir * stub;
        rawPoints.Add(start);
        rawPoints.Add(sStub);

        // Case (d): endDir == Vector3.zero means a free end while dragging, with no end stub
        if (endDir == Vector3.zero)
        {
            RouteFreeEnd(sStub, sDir, end, stub, rawPoints);
            return FilterCollinearPoints(rawPoints);
        }

        Vector3 eDir = SnapToWorldAxis(endDir);
        if (eDir == Vector3.zero) eDir = -sDir;
        Vector3 eStub = end + eDir * stub;

        // Evaluate orientation between the two exit directions
        float dot = Vector3.Dot(sDir, eDir);
        Vector3 delta = eStub - sStub;
        float projS = Vector3.Dot(delta, sDir);

        GetPerpendicularAxes(sDir, out Vector3 perp1, out Vector3 perp2);

        if (dot < -0.9f && projS > 0.001f)
        {
            // Case (b): Terminals facing each other -> elbow at the midpoint
            RouteFacingMidpoint(sStub, sDir, eStub, projS, perp1, perp2, rawPoints);
        }
        else if (dot > 0.9f || (dot < -0.9f && projS <= 0.001f))
        {
            // Case (c): Same side or target behind -> U-shaped route out past both terminals, crosses over, comes back
            RouteUShaped(sStub, sDir, eStub, eDir, dot > 0.9f, stub, perp1, perp2, rawPoints);
        }
        else
        {
            // Case (a): Different axes -> 3 segments, forward along start axis first if target is in front, else perp axis first
            RouteDifferentAxes(sStub, sDir, eStub, eDir, projS, rawPoints);
        }

        rawPoints.Add(eStub);
        rawPoints.Add(end);

        return FilterCollinearPoints(rawPoints);
    }

    /// <summary>
    /// Snaps a 3D vector to the single nearest world axis (+/- X, +/- Y, or +/- Z).
    /// </summary>
    public static Vector3 SnapToWorldAxis(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return Vector3.zero;

        float absX = Mathf.Abs(dir.x);
        float absY = Mathf.Abs(dir.y);
        float absZ = Mathf.Abs(dir.z);

        if (absX >= absY && absX >= absZ)
        {
            return new Vector3(Mathf.Sign(dir.x), 0f, 0f);
        }
        if (absY >= absX && absY >= absZ)
        {
            return new Vector3(0f, Mathf.Sign(dir.y), 0f);
        }
        return new Vector3(0f, 0f, Mathf.Sign(dir.z));
    }

    private static void GetPerpendicularAxes(Vector3 axis, out Vector3 perp1, out Vector3 perp2)
    {
        if (Mathf.Abs(axis.x) > 0.5f)
        {
            perp1 = Vector3.up;
            perp2 = Vector3.forward;
        }
        else if (Mathf.Abs(axis.y) > 0.5f)
        {
            perp1 = Vector3.right;
            perp2 = Vector3.forward;
        }
        else
        {
            perp1 = Vector3.right;
            perp2 = Vector3.up;
        }
    }

    /// <summary>
    /// Case (d): Routes to a free dragging point in 3D space with no end stub.
    /// </summary>
    private static void RouteFreeEnd(Vector3 sStub, Vector3 sDir, Vector3 target, float stub, List<Vector3> points)
    {
        Vector3 delta = target - sStub;
        float projS = Vector3.Dot(delta, sDir);

        GetPerpendicularAxes(sDir, out Vector3 perp1, out Vector3 perp2);

        if (projS > 0.001f)
        {
            // Target is in front along exit direction
            Vector3 p1 = sStub + sDir * projS;
            Vector3 p2 = p1 + perp1 * Vector3.Dot(target - p1, perp1);
            points.Add(p1);
            points.Add(p2);
            points.Add(target);
        }
        else
        {
            // Target is behind or to the side of exit stub
            Vector3 p1 = sStub + perp1 * Vector3.Dot(target - sStub, perp1);
            Vector3 p2 = p1 + perp2 * Vector3.Dot(target - p1, perp2);
            points.Add(p1);
            points.Add(p2);
            points.Add(target);
        }
    }

    /// <summary>
    /// Case (b): Facing each other -> midpoint elbow.
    /// </summary>
    private static void RouteFacingMidpoint(
        Vector3 sStub,
        Vector3 sDir,
        Vector3 eStub,
        float projS,
        Vector3 perp1,
        Vector3 perp2,
        List<Vector3> points)
    {
        float midDist = projS * 0.5f;
        Vector3 midPlanePoint = sStub + sDir * midDist;

        Vector3 p1 = midPlanePoint + perp1 * Vector3.Dot(eStub - midPlanePoint, perp1);
        Vector3 p2 = p1 + perp2 * Vector3.Dot(eStub - p1, perp2);

        points.Add(midPlanePoint);
        points.Add(p1);
        points.Add(p2);
    }

    /// <summary>
    /// Case (c): Same side or target behind -> U-shaped route out past both terminals, crosses over, comes back.
    /// </summary>
    private static void RouteUShaped(
        Vector3 sStub,
        Vector3 sDir,
        Vector3 eStub,
        Vector3 eDir,
        bool sameDirection,
        float stub,
        Vector3 perp1,
        Vector3 perp2,
        List<Vector3> points)
    {
        if (sameDirection)
        {
            // Both terminals exit in the same direction (e.g. both facing +Y)
            float sCoord = Vector3.Dot(sStub, sDir);
            float eCoord = Vector3.Dot(eStub, sDir);
            float clearCoord = Mathf.Max(sCoord, eCoord) + stub;

            Vector3 p1 = sStub + sDir * (clearCoord - sCoord);
            Vector3 p2 = p1 + perp1 * Vector3.Dot(eStub - p1, perp1);
            Vector3 p3 = p2 + perp2 * Vector3.Dot(eStub - p2, perp2);

            points.Add(p1);
            points.Add(p2);
            points.Add(p3);
        }
        else
        {
            // Facing opposite directions but target is behind start terminal (e.g. sDir=+Y, eDir=-Y but eStub is below sStub)
            float stepOutDist = stub;
            Vector3 p1 = sStub + sDir * stepOutDist;

            // Pick dominant perpendicular difference to step clear around the sides
            float d1 = Vector3.Dot(eStub - sStub, perp1);
            float d2 = Vector3.Dot(eStub - sStub, perp2);
            Vector3 sideAxis = Mathf.Abs(d1) >= Mathf.Abs(d2) ? perp1 : perp2;
            float sideDelta = Mathf.Abs(d1) >= Mathf.Abs(d2) ? d1 : d2;
            if (Mathf.Abs(sideDelta) < 0.001f) sideDelta = stub;

            Vector3 p2 = p1 + sideAxis * sideDelta;
            Vector3 p3 = p2 + sDir * (Vector3.Dot(eStub, sDir) - Vector3.Dot(p2, sDir));

            points.Add(p1);
            points.Add(p2);
            points.Add(p3);
        }
    }

    /// <summary>
    /// Case (a): Different axes -> 3 segments, forward along start axis first if target is in front, else perp axis first.
    /// </summary>
    private static void RouteDifferentAxes(
        Vector3 sStub,
        Vector3 sDir,
        Vector3 eStub,
        Vector3 eDir,
        float projS,
        List<Vector3> points)
    {
        Vector3 thirdAxis = Vector3.Cross(sDir, eDir).normalized;

        if (projS > 0.001f)
        {
            // Target is in front along start axis: go forward along start axis first
            Vector3 p1 = sStub + sDir * projS;
            Vector3 p2 = p1 + thirdAxis * Vector3.Dot(eStub - p1, thirdAxis);

            points.Add(p1);
            points.Add(p2);
        }
        else
        {
            // Target is behind: go along perpendicular axis first
            Vector3 p1 = sStub + thirdAxis * Vector3.Dot(eStub - sStub, thirdAxis);
            Vector3 p2 = p1 + eDir * Vector3.Dot(eStub - p1, eDir);

            points.Add(p1);
            points.Add(p2);
        }
    }

    /// <summary>
    /// Removes redundant consecutive duplicate and collinear points.
    /// </summary>
    public static List<Vector3> FilterCollinearPoints(List<Vector3> points)
    {
        if (points == null || points.Count < 2)
        {
            return points != null ? new List<Vector3>(points) : new List<Vector3>();
        }

        // 1. Remove consecutive identical points
        List<Vector3> noDups = new List<Vector3>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            if (noDups.Count == 0 || (points[i] - noDups[noDups.Count - 1]).sqrMagnitude > 1e-8f)
            {
                noDups.Add(points[i]);
            }
        }

        if (noDups.Count < 3)
        {
            return noDups;
        }

        // 2. Remove collinear intermediate points
        List<Vector3> result = new List<Vector3>(noDups.Count);
        result.Add(noDups[0]);

        for (int i = 1; i < noDups.Count - 1; i++)
        {
            Vector3 prev = result[result.Count - 1];
            Vector3 curr = noDups[i];
            Vector3 next = noDups[i + 1];

            Vector3 d1 = (curr - prev).normalized;
            Vector3 d2 = (next - curr).normalized;

            // If d1 and d2 are virtually parallel in the same direction, curr is redundant
            if (Vector3.Dot(d1, d2) > 0.999f)
            {
                continue;
            }

            result.Add(curr);
        }

        result.Add(noDups[noDups.Count - 1]);
        return result;
    }
}
