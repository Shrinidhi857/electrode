using System;
using System.Collections.Generic;
using UnityEngine;

namespace SmartLines
{
    /// <summary>
    /// Line style modes matching Lucidchart / schematic diagramming.
    /// </summary>
    public enum LinePathMode
    {
        Straight,
        Elbow,
        Curved
    }

    /// <summary>
    /// Pure routing and obstacle-avoidance engine.
    /// Separated completely from rendering logic. Calculates collision-free 2D paths
    /// (Straight, Elbow/Orthogonal 90-degree, and Bezier Curved) around 2D colliders.
    /// </summary>
    public static class SmartLineRouter
    {
        // Reusable list buffers to avoid GC allocations during high-frequency routing
        private static readonly List<Vector2> s_PathBuffer = new List<Vector2>(64);
        private static readonly List<Bounds> s_ObstacleBoundsBuffer = new List<Bounds>(16);
        private static readonly Collider2D[] s_ColliderHitBuffer = new Collider2D[32];

        /// <summary>
        /// Calculates the final list of world-space points for the line.
        /// </summary>
        /// <param name="start">Start world coordinate.</param>
        /// <param name="end">End world coordinate.</param>
        /// <param name="startNormal">Outward direction vector from start object (or Vector2.zero).</param>
        /// <param name="endNormal">Outward direction vector from end object (or Vector2.zero).</param>
        /// <param name="mode">Routing mode: Straight, Elbow (90 deg), Curved.</param>
        /// <param name="obstacleMask">LayerMask for colliders to route around.</param>
        /// <param name="obstaclePadding">Clearance distance from obstacle edges.</param>
        /// <param name="curveResolution">Number of interpolation steps for curved mode.</param>
        /// <returns>Read-only list of calculated world vertices.</returns>
        public static List<Vector2> CalculatePath(
            Vector2 start,
            Vector2 end,
            Vector2 startNormal,
            Vector2 endNormal,
            LinePathMode mode,
            LayerMask obstacleMask,
            float obstaclePadding = 0.25f,
            int curveResolution = 24)
        {
            s_PathBuffer.Clear();

            switch (mode)
            {
                case LinePathMode.Straight:
                    CalculateStraightPath(start, end, s_PathBuffer);
                    break;

                case LinePathMode.Elbow:
                    CalculateElbowPath(start, end, startNormal, endNormal, obstacleMask, obstaclePadding, s_PathBuffer);
                    break;

                case LinePathMode.Curved:
                    CalculateCurvedPath(start, end, startNormal, endNormal, obstacleMask, obstaclePadding, curveResolution, s_PathBuffer);
                    break;
            }

            return s_PathBuffer;
        }

        #region Straight Path
        private static void CalculateStraightPath(Vector2 start, Vector2 end, List<Vector2> output)
        {
            output.Add(start);
            output.Add(end);
        }
        #endregion

        #region Elbow (90-Degree Orthogonal Routing)
        /// <summary>
        /// Computes 90-degree orthogonal paths. If obstacles intervene, routes around their bounding perimeters.
        /// </summary>
        private static void CalculateElbowPath(
            Vector2 start,
            Vector2 end,
            Vector2 startNormal,
            Vector2 endNormal,
            LayerMask obstacleMask,
            float padding,
            List<Vector2> output)
        {
            // Initial stub offsets for ports (gives clean horizontal/vertical exit from nodes)
            float stubLength = Mathf.Max(padding, 0.25f);
            Vector2 pStart = start;
            Vector2 pEnd = end;

            if (startNormal != Vector2.zero)
            {
                pStart += startNormal.normalized * stubLength;
            }
            if (endNormal != Vector2.zero)
            {
                pEnd += endNormal.normalized * stubLength;
            }

            // Gather obstacles between pStart and pEnd
            GatherObstacleBounds(start, end, obstacleMask, padding, s_ObstacleBoundsBuffer);

            // If no obstacles intersect, build standard Lucidchart 2-bend or 1-bend orthogonal path
            if (s_ObstacleBoundsBuffer.Count == 0 || !AnySegmentHitsObstacles(pStart, pEnd, s_ObstacleBoundsBuffer))
            {
                output.Add(start);
                if (pStart != start) output.Add(pStart);

                BuildDirectOrthogonalBends(pStart, pEnd, startNormal, endNormal, output);

                if (pEnd != end) output.Add(pEnd);
                output.Add(end);
                return;
            }

            // Obstacles exist: Route around bounding boxes
            output.Add(start);
            if (pStart != start) output.Add(pStart);

            RouteAroundObstaclesOrthogonal(pStart, pEnd, s_ObstacleBoundsBuffer, output);

            if (pEnd != end) output.Add(pEnd);
            output.Add(end);
        }

        /// <summary>
        /// Standard orthogonal Z/S or L bend connecting two points without obstacles.
        /// </summary>
        private static void BuildDirectOrthogonalBends(
            Vector2 start,
            Vector2 end,
            Vector2 startNormal,
            Vector2 endNormal,
            List<Vector2> output)
        {
            bool preferHorizontal = Mathf.Abs(startNormal.x) > 0.5f || Mathf.Abs(endNormal.x) > 0.5f;

            if (preferHorizontal)
            {
                // Route Horizontal -> Vertical -> Horizontal (Z shape)
                float midX = (start.x + end.x) * 0.5f;
                output.Add(new Vector2(midX, start.y));
                output.Add(new Vector2(midX, end.y));
            }
            else
            {
                // Route Vertical -> Horizontal -> Vertical
                float midY = (start.y + end.y) * 0.5f;
                output.Add(new Vector2(start.x, midY));
                output.Add(new Vector2(end.x, midY));
            }
        }

        /// <summary>
        /// Generates detour waypoints around detected bounding obstacles.
        /// </summary>
        private static void RouteAroundObstaclesOrthogonal(
            Vector2 start,
            Vector2 end,
            List<Bounds> obstacles,
            List<Vector2> output)
        {
            // Find primary blocking obstacle
            Bounds primary = obstacles[0];
            foreach (var b in obstacles)
            {
                if (SegmentIntersectsBounds(start, end, b))
                {
                    primary = b;
                    break;
                }
            }

            // Decide whether to detour above/below or left/right
            float distAbove = Mathf.Abs(primary.max.y - Mathf.Min(start.y, end.y));
            float distBelow = Mathf.Abs(primary.min.y - Mathf.Max(start.y, end.y));
            float distRight = Mathf.Abs(primary.max.x - Mathf.Min(start.x, end.x));
            float distLeft = Mathf.Abs(primary.min.x - Mathf.Max(start.x, end.x));

            bool bypassVertical = (distAbove + distBelow) < (distRight + distLeft);

            if (bypassVertical)
            {
                float bypassY = (start.y + end.y) * 0.5f > primary.center.y ? primary.max.y : primary.min.y;
                output.Add(new Vector2(start.x, bypassY));
                output.Add(new Vector2(end.x, bypassY));
            }
            else
            {
                float bypassX = (start.x + end.x) * 0.5f > primary.center.x ? primary.max.x : primary.min.x;
                output.Add(new Vector2(bypassX, start.y));
                output.Add(new Vector2(bypassX, end.y));
            }
        }
        #endregion

        #region Curved Path (Smooth Bezier)
        private static void CalculateCurvedPath(
            Vector2 start,
            Vector2 end,
            Vector2 startNormal,
            Vector2 endNormal,
            LayerMask obstacleMask,
            float padding,
            int resolution,
            List<Vector2> output)
        {
            // 1. First get elbow/anchor waypoints to determine shape
            List<Vector2> elbowPoints = new List<Vector2>();
            CalculateElbowPath(start, end, startNormal, endNormal, obstacleMask, padding, elbowPoints);

            if (elbowPoints.Count <= 2)
            {
                // Simple Cubic Bezier with tangent controls based on port normals
                float distance = Vector2.Distance(start, end);
                float tangentStrength = distance * 0.45f;

                Vector2 tangentA = start + (startNormal != Vector2.zero ? startNormal.normalized : (end - start).normalized) * tangentStrength;
                Vector2 tangentB = end + (endNormal != Vector2.zero ? endNormal.normalized : (start - end).normalized) * tangentStrength;

                for (int i = 0; i <= resolution; i++)
                {
                    float t = i / (float)resolution;
                    output.Add(EvaluateCubicBezier(start, tangentA, tangentB, end, t));
                }
            }
            else
            {
                // Multi-point smooth Catmull-Rom or composite curve through elbow corners
                SmoothMultiPointCurve(elbowPoints, resolution, output);
            }
        }

        private static Vector2 EvaluateCubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            Vector2 p = uuu * p0;
            p += 3f * uu * t * p1;
            p += 3f * u * tt * p2;
            p += ttt * p3;
            return p;
        }

        private static void SmoothMultiPointCurve(List<Vector2> points, int resolution, List<Vector2> output)
        {
            if (points == null || points.Count < 2) return;

            // Round sharp corners of elbow waypoints
            output.Add(points[0]);
            float cornerRadius = 0.35f;

            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector2 prev = points[i - 1];
                Vector2 curr = points[i];
                Vector2 next = points[i + 1];

                Vector2 d1 = (prev - curr).normalized;
                Vector2 d2 = (next - curr).normalized;

                float maxR = Mathf.Min(Vector2.Distance(prev, curr), Vector2.Distance(next, curr)) * 0.45f;
                float r = Mathf.Min(cornerRadius, maxR);

                Vector2 pA = curr + d1 * r;
                Vector2 pB = curr + d2 * r;

                output.Add(pA);

                // Small quadratic corner blend
                int subSteps = Mathf.Max(4, resolution / points.Count);
                for (int s = 1; s < subSteps; s++)
                {
                    float t = s / (float)subSteps;
                    Vector2 quad = (1f - t) * (1f - t) * pA + 2f * (1f - t) * t * curr + t * t * pB;
                    output.Add(quad);
                }

                output.Add(pB);
            }

            output.Add(points[points.Count - 1]);
        }
        #endregion

        #region Collision & Obstacle Helpers
        private static void GatherObstacleBounds(
            Vector2 start,
            Vector2 end,
            LayerMask obstacleMask,
            float padding,
            List<Bounds> boundsList)
        {
            boundsList.Clear();
            if (obstacleMask.value == 0) return;

            Vector2 min = Vector2.Min(start, end) - Vector2.one * padding;
            Vector2 max = Vector2.Max(start, end) + Vector2.one * padding;
            Vector2 size = max - min;
            Vector2 center = min + size * 0.5f;

            int hitCount = Physics2D.OverlapBoxNonAlloc(center, size, 0f, s_ColliderHitBuffer, obstacleMask);
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D col = s_ColliderHitBuffer[i];
                if (col == null || col.isTrigger) continue;

                Bounds b = col.bounds;
                b.Expand(padding * 2f);
                boundsList.Add(b);
            }
        }

        private static bool AnySegmentHitsObstacles(Vector2 p1, Vector2 p2, List<Bounds> obstacles)
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                if (SegmentIntersectsBounds(p1, p2, obstacles[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// Liang-Barsky / Slab method for testing line segment vs 2D AABB intersection.
        /// </summary>
        public static bool SegmentIntersectsBounds(Vector2 p1, Vector2 p2, Bounds b)
        {
            float t0 = 0f;
            float t1 = 1f;
            Vector2 d = p2 - p1;

            float[] p = { -d.x, d.x, -d.y, d.y };
            float[] q = { p1.x - b.min.x, b.max.x - p1.x, p1.y - b.min.y, b.max.y - p1.y };

            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(p[i]) < 1e-6f)
                {
                    if (q[i] < 0f) return false;
                }
                else
                {
                    float t = q[i] / p[i];
                    if (p[i] < 0f)
                    {
                        if (t > t1) return false;
                        if (t > t0) t0 = t;
                    }
                    else
                    {
                        if (t < t0) return false;
                        if (t < t1) t1 = t;
                    }
                }
            }

            return t0 <= t1;
        }
        #endregion
    }
}
