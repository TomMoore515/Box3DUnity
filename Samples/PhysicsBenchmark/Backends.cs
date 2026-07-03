using System;
using System.Diagnostics;
using Box3D.Interop;
using UnityEngine;

namespace Box3D.Benchmark
{
    public enum QueryKind { Raycast, CapsuleCast, CheckCapsule, Depenetrate }

    public struct BenchResult
    {
        public string Engine;
        public QueryKind Kind;
        public long Ops;
        public double Millis;
        public double OpsPerSec;
        public double NsPerOp;
        public int HitsPerBatch;   // fairness signal: should agree across engines
        public double P50Ns, P95Ns, P99Ns;
        public long Checksum;      // keeps the timed loop from being optimized away
    }

    /// <summary>
    /// Single-thread throughput + latency for one query kind on one backend. (Folded in here from a
    /// separate BenchmarkRunner.cs that Unity's importer refused to add to this assembly.)
    /// </summary>
    public static class BenchmarkRunner
    {
        public static BenchResult RunSingle(string engine, QueryKind kind, Func<int, bool> op,
            int queryCount, int repeats)
        {
            // 1. Warmup — one full pass, untimed. Its hit count represents one batch.
            int hitsPerBatch = 0;
            for (int i = 0; i < queryCount; i++)
                if (op(i)) hitsPerBatch++;

            // 2. Throughput — one timer around every pass.
            long checksum = 0;
            var sw = Stopwatch.StartNew();
            for (int r = 0; r < repeats; r++)
                for (int i = 0; i < queryCount; i++)
                    if (op(i)) checksum++;
            sw.Stop();

            long ops = (long)queryCount * repeats;
            double seconds = sw.Elapsed.TotalSeconds;

            // 3. Latency — per-op timing for percentiles. Timer overhead (~tens of ns) inflates the
            // fastest ops most; throughput above is the headline, percentiles show distribution shape.
            var samples = new double[queryCount];
            double nsPerTick = 1e9 / Stopwatch.Frequency;
            for (int i = 0; i < queryCount; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                op(i);
                long t1 = Stopwatch.GetTimestamp();
                samples[i] = (t1 - t0) * nsPerTick;
            }
            Array.Sort(samples);

            return new BenchResult
            {
                Engine = engine,
                Kind = kind,
                Ops = ops,
                Millis = seconds * 1000.0,
                OpsPerSec = seconds > 0 ? ops / seconds : 0,
                NsPerOp = seconds > 0 ? seconds * 1e9 / ops : 0,
                HitsPerBatch = hitsPerBatch,
                P50Ns = Percentile(samples, 0.50),
                P95Ns = Percentile(samples, 0.95),
                P99Ns = Percentile(samples, 0.99),
                Checksum = checksum,
            };
        }

        static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 0) return 0;
            int idx = (int)(p * (sorted.Length - 1));
            if (idx < 0) idx = 0;
            if (idx >= sorted.Length) idx = sorted.Length - 1;
            return sorted[idx];
        }

        // Multi-thread lane (next iteration): box3d fans the query array across worker threads
        // (lock-free, no stepping); PhysX must use RaycastCommand/CapsulecastCommand, not a loop of
        // Physics.Raycast off-thread. CheckCapsule/Depenetrate have no PhysX batch form. See README.
    }

    /// <summary>
    /// A collision-query backend over one engine. Every op returns a hit flag: the runner sums it
    /// into a checksum (so the optimizer can't elide the query) and reports the per-batch hit count
    /// as the cross-engine fairness signal.
    /// </summary>
    public interface IQueryBackend : IDisposable
    {
        string Name { get; }

        /// <summary>False for PhysX: its scene queries are main-thread-bound. The multi-thread lane
        /// must route those through the batched command API (RaycastCommand/CapsulecastCommand)
        /// instead of calling these directly off-thread.</summary>
        bool ThreadSafeQueries { get; }

        bool Raycast(in RayQuery q);
        bool CapsuleCast(in CapsuleQuery q);
        bool CheckCapsule(in PointQuery q);
        bool Depenetrate(in PointQuery q);
    }

    /// <summary>
    /// box3d backend. The world is engine-free and its queries are safe from any thread while nothing
    /// steps it — which is the case here (a query-only world). One unified API covers all four ops.
    /// </summary>
    public sealed class Box3DBackend : IQueryBackend
    {
        const int MaxPlanes = 16;

        readonly Box3DWorld _world;
        readonly B3QueryFilter _filter;

        public Box3DBackend(Box3DWorld world)
        {
            _world = world;
            _filter = Box3DWorld.DefaultQueryFilter();
        }

        public string Name => "box3d";
        public bool ThreadSafeQueries => true;

        public bool Raycast(in RayQuery q)
        {
            B3RayResult r = _world.CastRayClosest(q.Origin.ToB3Pos(), (q.Dir * q.Dist).ToB3(), _filter);
            return r.Hit != 0;
        }

        public bool CapsuleCast(in CapsuleQuery q)
        {
            // ignoreInitialContact: match PhysX sweep semantics — a cast does not report shapes the
            // capsule already overlaps (those belong to Depenetrate). Without this box3d counts initial
            // contacts as hits and the hits/batch column diverges from PhysX.
            ToRelative(q.P1, q.P2, q.Radius, out B3Pos origin, out B3Capsule capsule);
            return _world.CastCapsuleClosest(origin, in capsule, (q.Dir * q.Dist).ToB3(), _filter, out _,
                ignoreInitialContact: true);
        }

        public bool CheckCapsule(in PointQuery q)
        {
            ToRelative(q.P1, q.P2, q.Radius, out B3Pos origin, out B3Capsule capsule);
            return _world.OverlapCapsule(origin, in capsule, _filter);
        }

        public bool Depenetrate(in PointQuery q)
        {
            ToRelative(q.P1, q.P2, q.Radius, out B3Pos origin, out B3Capsule capsule);

            Span<B3PlaneResult> contacts = stackalloc B3PlaneResult[MaxPlanes];
            int n = _world.CollideMover(origin, in capsule, _filter, contacts);
            if (n == 0) return false;

            Span<B3CollisionPlane> planes = stackalloc B3CollisionPlane[MaxPlanes];
            int pc = Box3DMover.ToCollisionPlanes(contacts.Slice(0, n), planes);
            // Run the full solve so the timed work matches what a controller pays; the returned push
            // is unused here. "Hit" = the capsule had world contacts to resolve, which mirrors PhysX's
            // overlap+ComputePenetration notion. (Counting only non-zero pushes would undercount vs
            // PhysX because box3d's solver zeroes sub-margin contacts.)
            Box3DMover.SolvePlanes(default, planes.Slice(0, pc));
            return n > 0;
        }

        // box3d query proxies are relative to a double-precision origin so precision holds far from
        // the world origin; anchor at the capsule segment midpoint (mirrors Box3dCollisionWorld).
        static void ToRelative(Vector3 p1, Vector3 p2, float radius, out B3Pos origin, out B3Capsule capsule)
        {
            Vector3 mid = (p1 + p2) * 0.5f;
            origin = mid.ToB3Pos();
            capsule = new B3Capsule
            {
                Center1 = (p1 - mid).ToB3(),
                Center2 = (p2 - mid).ToB3(),
                Radius = radius,
            };
        }

        public void Dispose() => _world.Dispose();
    }

    /// <summary>
    /// PhysX backend — Unity scene queries. Main-thread only; <see cref="ThreadSafeQueries"/> is
    /// false. There is no <c>ComputePenetration</c> batch-job equivalent, so the depenetrate path
    /// (OverlapCapsule + ComputePenetration per overlap) is structurally main-thread — that gap is
    /// part of the story, not a flaw in the harness.
    /// </summary>
    public sealed class PhysxBackend : IQueryBackend
    {
        readonly GameObject _fieldRoot;
        readonly Collider[] _overlap = new Collider[32];
        readonly GameObject _probeGo;
        readonly CapsuleCollider _probe;

        public PhysxBackend(GameObject fieldRoot)
        {
            _fieldRoot = fieldRoot;

            // A capsule collider used only as the "B" shape for ComputePenetration. It must be ACTIVE
            // so PhysX cooks the shape (an inactive collider is uncooked → ComputePenetration no-ops and
            // Depenetrate silently returns 0). Parked far from the field so it never shows up in the
            // field's overlap queries; ComputePenetration takes an explicit pose, so its transform is
            // irrelevant to the result.
            _probeGo = new GameObject("Box3D.Benchmark.PhysxProbe");
            _probeGo.transform.position = new Vector3(1e6f, 0f, 0f);
            _probe = _probeGo.AddComponent<CapsuleCollider>();
            _probe.direction = 1; // Y axis
        }

        public string Name => "PhysX";
        public bool ThreadSafeQueries => false;

        public bool Raycast(in RayQuery q)
            => UnityEngine.Physics.Raycast(q.Origin, q.Dir, q.Dist);

        public bool CapsuleCast(in CapsuleQuery q)
            => UnityEngine.Physics.CapsuleCast(q.P1, q.P2, q.Radius, q.Dir, q.Dist);

        public bool CheckCapsule(in PointQuery q)
            => UnityEngine.Physics.CheckCapsule(q.P1, q.P2, q.Radius);

        public bool Depenetrate(in PointQuery q)
        {
            int n = UnityEngine.Physics.OverlapCapsuleNonAlloc(q.P1, q.P2, q.Radius, _overlap);
            if (n == 0) return false;

            Vector3 center = (q.P1 + q.P2) * 0.5f;
            _probe.radius = q.Radius;
            _probe.height = Vector3.Distance(q.P1, q.P2) + q.Radius * 2f;

            bool any = false;
            for (int i = 0; i < n; i++)
            {
                Collider c = _overlap[i];
                if (UnityEngine.Physics.ComputePenetration(
                        _probe, center, Quaternion.identity,
                        c, c.transform.position, c.transform.rotation,
                        out _, out float dist) && dist > 0f)
                    any = true;
            }
            return any;
        }

        public void Dispose()
        {
            if (_probeGo != null) UnityEngine.Object.Destroy(_probeGo);
            if (_fieldRoot != null) UnityEngine.Object.Destroy(_fieldRoot);
        }
    }
}
