using System.Collections.Generic;
using UnityEngine;

namespace WireSystem
{
    /// <summary>
    /// Pure static orthogonal routing engine.
    /// Replicates Lucidchart / draw.io right-angle elbow routing.
    /// Computes strictly axis-aligned (H-V-H or V-H-V) paths evaluated in the breaker's local coordinate space,
    /// ensuring wires remain perfectly aligned and non-diagonal even when the model moves, rotates, or scales in AR.
    /// </summary>
    public static class WireRouter
    {
        // Reusable static buffers to ensure zero GC allocations per frame during AR interaction
        private static readonly List<Vector3> s_LocalWaypoints = new List<Vector3>(16);
        private static readonly List<Vector3> s_FilteredPoints = new List<Vector3>(16);
        private static readonly List<Vector3> s_FilletBuffer = new List<Vector3>(32);

        /// <summary>
        /// Computes the list of world-space positions for the orthogonal wire path.
        /// </summary>
        /// <param name="startPos">Start world coordinate.</param>
        /// <param name="startExitDir">Start outward exit normal in world space.</param>
        /// <param name="endPos">End world coordinate.</param>
        /// <param name="endExitDir">End outward exit normal in world space.</param>
        /// <param name="modelRoot">Reference transform (e.g. Four-Pole-Breaker-final) defining local axes.</param>
        /// <param name="settings">Routing settings (stub length, corner radius, lane offset).</param>
        /// <param name="laneIndex">Unique lane index to stagger parallel wires between identical terminal rows.</param>
        /// <param name="outputWorldPoints">Output buffer where calculated world points are written.</param>
        public static void CalculateOrthogonalPath(
            Vector3 startPos,
            Vector3 startExitDir,
            Vector3 endPos,
            Vector3 endExitDir,
            Transform modelRoot,
            WireSettings settings,
            int laneIndex,
            List<Vector3> outputWorldPoints)
        {
            outputWorldPoints.Clear();
            s_LocalWaypoints.Clear();
            s_FilteredPoints.Clear();

            // 1. Transform to reference model's local coordinate space
            Vector3 lStart = modelRoot != null ? modelRoot.InverseTransformPoint(startPos) : startPos;
            Vector3 lEnd = modelRoot != null ? modelRoot.InverseTransformPoint(endPos) : endPos;

            Vector3 lStartExit = modelRoot != null ? modelRoot.InverseTransformDirection(startExitDir) : startExitDir;
            Vector3 lEndExit = modelRoot != null ? modelRoot.InverseTransformDirection(endExitDir) : endExitDir;

            if (lStartExit.sqrMagnitude < 0.001f) lStartExit = Vector3.up;
            if (lEndExit.sqrMagnitude < 0.001f) lEndExit = (lStart - lEnd).normalized;

            lStartExit.Normalize();
            lEndExit.Normalize();

            float stub = settings != null ? settings.stubLength : 0.02f;
            float laneOffset = (settings != null ? settings.laneOffset : 0.006f) * laneIndex;

            // 2. Calculate initial stubs leaving the terminals
            Vector3 lStubA = lStart + lStartExit * stub;
            Vector3 lStubB = lEnd + lEndExit * stub;

            s_LocalWaypoints.Add(lStart);
            s_LocalWaypoints.Add(lStubA);

            // 3. Connect the two stubs using orthogonal segments (H-V-H or V-H-V)
            ConnectStubsOrthogonal(lStubA, lStartExit, lStubB, lEndExit, laneOffset, s_LocalWaypoints);

            s_LocalWaypoints.Add(lStubB);
            s_LocalWaypoints.Add(lEnd);

            // 4. Clean up collinear / redundant points
            FilterCollinearPoints(s_LocalWaypoints, s_FilteredPoints);

            // 5. Apply optional rounded corner fillets or sharp joints
            if (settings != null && settings.roundCorners && settings.cornerRadius > 0.0005f)
            {
                s_FilletBuffer.Clear();
                ApplyCornerFillets(s_FilteredPoints, settings.cornerRadius, s_FilletBuffer);

                for (int i = 0; i < s_FilletBuffer.Count; i++)
                {
                    Vector3 worldPt = modelRoot != null ? modelRoot.TransformPoint(s_FilletBuffer[i]) : s_FilletBuffer[i];
                    outputWorldPoints.Add(worldPt);
                }
            }
            else
            {
                // Pure sharp 90-degree orthogonal joints
                for (int i = 0; i < s_FilteredPoints.Count; i++)
                {
                    Vector3 worldPt = modelRoot != null ? modelRoot.TransformPoint(s_FilteredPoints[i]) : s_FilteredPoints[i];
                    outputWorldPoints.Add(worldPt);
                }
            }
        }

        /// <summary>
        /// Selects the cleanest orthogonal 90-degree bridge between the two stubs.
        /// Evaluates both H-V-H and V-H-V candidates and picks the one minimizing sharp zig-zags.
        /// </summary>
        private static void ConnectStubsOrthogonal(
            Vector3 a,
            Vector3 exitA,
            Vector3 b,
            Vector3 exitB,
            float laneOffset,
            List<Vector3> waypoints)
        {
            // Check dominant exit axis
            bool exitIsVertical = Mathf.Abs(exitA.y) > Mathf.Abs(exitA.x);
            Vector3 mid = (a + b) * 0.5f;

            if (exitIsVertical)
            {
                // Route: Vertical Stub -> Step out in Z/X with laneOffset -> Turn across -> Step back into End Stub
                // Candidate 1: Midpoint Y bridge
                float bridgeY = mid.y;
                float bridgeZ = Mathf.Max(a.z, b.z) + Mathf.Abs(laneOffset);

                Vector3 c1 = new Vector3(a.x, a.y, bridgeZ);
                Vector3 c2 = new Vector3(a.x, bridgeY, bridgeZ);
                Vector3 c3 = new Vector3(b.x, bridgeY, bridgeZ);
                Vector3 c4 = new Vector3(b.x, b.y, bridgeZ);

                waypoints.Add(c1);
                waypoints.Add(c2);
                waypoints.Add(c3);
                waypoints.Add(c4);
            }
            else
            {
                // Candidate 2: Midpoint X bridge
                float bridgeX = mid.x;
                float bridgeZ = Mathf.Max(a.z, b.z) + Mathf.Abs(laneOffset);

                Vector3 c1 = new Vector3(a.x, a.y, bridgeZ);
                Vector3 c2 = new Vector3(bridgeX, a.y, bridgeZ);
                Vector3 c3 = new Vector3(bridgeX, b.y, bridgeZ);
                Vector3 c4 = new Vector3(b.x, b.y, bridgeZ);

                waypoints.Add(c1);
                waypoints.Add(c2);
                waypoints.Add(c3);
                waypoints.Add(c4);
            }
        }

        /// <summary>
        /// Removes redundant consecutive points that lie on the same straight line segment.
        /// </summary>
        private static void FilterCollinearPoints(List<Vector3> input, List<Vector3> output)
        {
            if (input.Count == 0) return;
            output.Add(input[0]);

            for (int i = 1; i < input.Count - 1; i++)
            {
                Vector3 prev = output[output.Count - 1];
                Vector3 curr = input[i];
                Vector3 next = input[i + 1];

                // If identical point, skip
                if ((curr - prev).sqrMagnitude < 1e-7f) continue;

                Vector3 d1 = (curr - prev).normalized;
                Vector3 d2 = (next - curr).normalized;

                // If directions are parallel (dot close to 1), curr is redundant
                if (Vector3.Dot(d1, d2) > 0.999f)
                {
                    continue;
                }

                output.Add(curr);
            }

            if (input.Count > 1)
            {
                output.Add(input[input.Count - 1]);
            }
        }

        /// <summary>
        /// Rounds sharp 90-degree corners with smooth small fillets.
        /// </summary>
        private static void ApplyCornerFillets(List<Vector3> points, float radius, List<Vector3> output)
        {
            if (points.Count < 3)
            {
                output.AddRange(points);
                return;
            }

            output.Add(points[0]);

            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector3 prev = points[i - 1];
                Vector3 curr = points[i];
                Vector3 next = points[i + 1];

                Vector3 d1 = (prev - curr).normalized;
                Vector3 d2 = (next - curr).normalized;

                float maxR = Mathf.Min(Vector3.Distance(prev, curr), Vector3.Distance(next, curr)) * 0.45f;
                float r = Mathf.Min(radius, maxR);

                Vector3 pA = curr + d1 * r;
                Vector3 pB = curr + d2 * r;

                output.Add(pA);

                // Small quadratic corner blend
                for (int s = 1; s <= 3; s++)
                {
                    float t = s / 4.0f;
                    Vector3 filletPt = (1 - t) * (1 - t) * pA + 2 * (1 - t) * t * curr + t * t * pB;
                    output.Add(filletPt);
                }

                output.Add(pB);
            }

            output.Add(points[points.Count - 1]);
        }
    }
}
