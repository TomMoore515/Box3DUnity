using UnityEngine;

namespace Box3D.Benchmark
{
    public struct RayQuery
    {
        public Vector3 Origin;
        public Vector3 Dir;     // unit
        public float Dist;
    }

    public struct CapsuleQuery
    {
        public Vector3 P1, P2;  // segment endpoints (world)
        public float Radius;
        public Vector3 Dir;     // unit
        public float Dist;
    }

    public struct PointQuery
    {
        public Vector3 P1, P2;  // segment endpoints (world)
        public float Radius;
    }

    /// <summary>
    /// Seeded query batches. Origins are drawn from the same volume the boxes fill and aimed in a
    /// random direction, so a batch is a realistic mix of hits and misses. The runner reports the
    /// hit count per engine — if box3d and PhysX disagree on hit count over identical geometry and
    /// identical queries, the comparison is not apples-to-apples and the numbers are meaningless.
    /// </summary>
    public static class QuerySet
    {
        public static RayQuery[] Rays(int count, float volumeHalf, float maxDist, ulong seed)
        {
            var rng = new Rng(seed);
            var q = new RayQuery[count];
            for (int i = 0; i < count; i++)
                q[i] = new RayQuery { Origin = rng.InCube(volumeHalf), Dir = rng.UnitVector(), Dist = maxDist };
            return q;
        }

        public static CapsuleQuery[] Capsules(int count, float volumeHalf, float radius, float height,
            float maxDist, ulong seed)
        {
            var rng = new Rng(seed);
            var q = new CapsuleQuery[count];
            float half = Mathf.Max(0f, height * 0.5f - radius);
            for (int i = 0; i < count; i++)
            {
                Vector3 c = rng.InCube(volumeHalf);
                q[i] = new CapsuleQuery
                {
                    P1 = c + Vector3.up * half,
                    P2 = c - Vector3.up * half,
                    Radius = radius,
                    Dir = rng.UnitVector(),
                    Dist = maxDist,
                };
            }
            return q;
        }

        public static PointQuery[] Points(int count, float volumeHalf, float radius, float height, ulong seed)
        {
            var rng = new Rng(seed);
            var q = new PointQuery[count];
            float half = Mathf.Max(0f, height * 0.5f - radius);
            for (int i = 0; i < count; i++)
            {
                Vector3 c = rng.InCube(volumeHalf);
                q[i] = new PointQuery { P1 = c + Vector3.up * half, P2 = c - Vector3.up * half, Radius = radius };
            }
            return q;
        }
    }
}
