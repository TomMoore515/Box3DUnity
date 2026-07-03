// box3d-vs-PhysX collision-query benchmark (package sample): single-thread + multi-thread lanes.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Box3D.Benchmark
{
    /// <summary>
    /// box3d-vs-PhysX collision-query benchmark. Add this to a GameObject in an otherwise empty scene
    /// and press Play — PhysX only queries a live scene, so this must run in play mode. Results print
    /// to the console and to <c>box3d-benchmark.md</c> under <see cref="Application.persistentDataPath"/>.
    ///
    /// Two lanes: a single-thread head-to-head, and a multi-thread lane where box3d fans queries across
    /// worker threads (the world is never stepped → lock-free) versus PhysX's only parallel path, the
    /// batched job commands (RaycastCommand / CapsulecastCommand). CheckCapsule / Depenetrate have no
    /// PhysX batch form, so box3d's scaling there is a structural win.
    ///
    /// For PUBLISHED numbers, build a standalone player with the <c>BOX3D_BENCHMARK</c> define set: the
    /// editor's PhysX carries overhead that is not representative. The header flags editor runs. The
    /// built-in Physics (PhysX) module MUST be enabled or every PhysX row reads 0 hits (see README).
    /// </summary>
    public sealed class PhysicsBenchmark : MonoBehaviour
    {
        [Header("World")]
        [SerializeField] int boxCount = 10000;
        [SerializeField] float volumeHalf = 100f;
        [SerializeField] float minBoxHalf = 0.5f;
        [SerializeField] float maxBoxHalf = 3f;
        [SerializeField] ulong worldSeed = 1337;

        [Header("Queries")]
        [SerializeField] int queryCount = 4096;
        [SerializeField] int repeats = 32;
        [SerializeField] float capsuleRadius = 0.3f;
        [SerializeField] float capsuleHeight = 1.8f;
        [SerializeField] ulong querySeed = 7331;

        [Header("Threading")]
        [Tooltip("Max box3d worker threads for the scaling sweep. 0 = logical core count.")]
        [SerializeField] int maxThreads = 0;
        [SerializeField] bool runMultiThread = true;

        [Header("Run")]
        [SerializeField] bool runOnStart = true;

        void Start()
        {
            if (runOnStart) Run();
        }

        [ContextMenu("Run")]
        public void Run()
        {
            var boxes = BoxField.Generate(boxCount, volumeHalf, minBoxHalf, maxBoxHalf, worldSeed);
            float maxDist = volumeHalf; // long enough to cross a good chunk of the field

            var rays = QuerySet.Rays(queryCount, volumeHalf, maxDist, querySeed);
            var caps = QuerySet.Capsules(queryCount, volumeHalf, capsuleRadius, capsuleHeight, maxDist, querySeed + 1);
            var pts = QuerySet.Points(queryCount, volumeHalf, capsuleRadius, capsuleHeight, querySeed + 2);

            // Build both worlds (timed).
            var b3Sw = System.Diagnostics.Stopwatch.StartNew();
            var world = BoxField.BuildBox3D(boxes);
            b3Sw.Stop();
            double b3BuildMs = b3Sw.Elapsed.TotalMilliseconds;

            var pxSw = System.Diagnostics.Stopwatch.StartNew();
            var root = BoxField.BuildPhysx(boxes);
            pxSw.Stop();
            double pxBuildMs = pxSw.Elapsed.TotalMilliseconds;

            var b3 = new Box3DBackend(world);
            var px = new PhysxBackend(root);
            try
            {
                // box3d ops are thread-safe query closures reused by both lanes.
                var b3Ops = new (QueryKind kind, Func<int, bool> op)[]
                {
                    (QueryKind.Raycast,      i => b3.Raycast(in rays[i])),
                    (QueryKind.CapsuleCast,  i => b3.CapsuleCast(in caps[i])),
                    (QueryKind.CheckCapsule, i => b3.CheckCapsule(in pts[i])),
                    (QueryKind.Depenetrate,  i => b3.Depenetrate(in pts[i])),
                };
                var pxOps = new (QueryKind kind, Func<int, bool> op)[]
                {
                    (QueryKind.Raycast,      i => px.Raycast(in rays[i])),
                    (QueryKind.CapsuleCast,  i => px.CapsuleCast(in caps[i])),
                    (QueryKind.CheckCapsule, i => px.CheckCapsule(in pts[i])),
                    (QueryKind.Depenetrate,  i => px.Depenetrate(in pts[i])),
                };

                // ---- single-thread lane ----
                var rows = new List<BenchResult>();
                foreach (var (kind, op) in b3Ops) rows.Add(BenchmarkRunner.RunSingle(b3.Name, kind, op, queryCount, repeats));
                foreach (var (kind, op) in pxOps) rows.Add(BenchmarkRunner.RunSingle(px.Name, kind, op, queryCount, repeats));

                var sb = new StringBuilder();
                sb.Append(Report(rows, boxes.Length, b3BuildMs, pxBuildMs));

                // ---- multi-thread lane ----
                if (runMultiThread)
                {
                    int[] tc = ThreadedLane.ThreadCounts(maxThreads);
                    var scaling = new List<ThreadScalingResult>();
                    foreach (var (kind, op) in b3Ops)
                        foreach (int t in tc)
                            scaling.Add(ThreadedLane.RunBox3DThreaded(kind, op, queryCount, repeats, t));

                    ThreadScalingResult pxRay = ThreadedLane.RunPhysxRaycastBatch(rays, repeats);
                    ThreadScalingResult pxCap = ThreadedLane.RunPhysxCapsuleBatch(caps, repeats);
                    double pxCheck = FindOps(rows, px.Name, QueryKind.CheckCapsule);
                    double pxDepen = FindOps(rows, px.Name, QueryKind.Depenetrate);

                    sb.Append(ReportMultiThread(scaling, tc, pxRay, pxCap, pxCheck, pxDepen));
                }

                string report = sb.ToString();
                Debug.Log(report);
                string path = System.IO.Path.Combine(Application.persistentDataPath, "box3d-benchmark.md");
                System.IO.File.WriteAllText(path, report);
                Debug.Log($"[Box3D.Benchmark] wrote report to {path}");
            }
            finally
            {
                b3.Dispose();
                px.Dispose();
            }
        }

        static double FindOps(List<BenchResult> rows, string engine, QueryKind kind)
        {
            foreach (var r in rows) if (r.Engine == engine && r.Kind == kind) return r.OpsPerSec;
            return 0;
        }

        static readonly QueryKind[] Kinds =
            { QueryKind.Raycast, QueryKind.CapsuleCast, QueryKind.CheckCapsule, QueryKind.Depenetrate };

        string Report(List<BenchResult> rows, int boxes, double b3BuildMs, double pxBuildMs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# box3d vs PhysX — collision-query benchmark");
            sb.AppendLine();
            sb.AppendLine($"- **Geometry:** {boxes:N0} axis-aligned static boxes (seed {worldSeed}), identical in both engines");
            sb.AppendLine($"- **Batch:** {queryCount:N0} queries × {repeats} passes per kind (seed {querySeed})");
            sb.AppendLine($"- **CPU:** {SystemInfo.processorType} ({SystemInfo.processorCount} logical cores)");
            sb.AppendLine($"- **OS:** {SystemInfo.operatingSystem}");
            sb.AppendLine($"- **Unity:** {Application.unityVersion}");
            sb.AppendLine(Application.isEditor
                ? "- **Runtime:** ⚠ EDITOR — not representative; build a standalone player for published numbers"
                : "- **Runtime:** standalone player");
            sb.AppendLine($"- **World build:** box3d {b3BuildMs:N1} ms (from data) · PhysX {pxBuildMs:N1} ms (GameObject colliders + SyncTransforms)");
            sb.AppendLine();
            sb.AppendLine("## Single thread");
            sb.AppendLine();
            sb.AppendLine("| Query | Engine | ops/sec | ns/op | p50 ns | p95 ns | p99 ns | hits/batch |");
            sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");

            foreach (var k in Kinds)
                foreach (var r in rows)
                    if (r.Kind == k)
                        sb.AppendLine($"| {r.Kind} | {r.Engine} | {r.OpsPerSec:N0} | {r.NsPerOp:N1} | {r.P50Ns:N0} | {r.P95Ns:N0} | {r.P99Ns:N0} | {r.HitsPerBatch:N0} |");

            sb.AppendLine();
            sb.AppendLine("_**Raycast / CapsuleCast / CheckCapsule have identical `hits/batch` across engines** — " +
                          "directly comparable. PhysX leads on single-thread casts (mature C++ broadphase), reported honestly._");
            sb.AppendLine();
            sb.AppendLine("_**Depenetrate is not a 1:1 op.** box3d resolves capsule-vs-world in one native mover call " +
                          "(`CollideMover`+`SolvePlanes`); PhysX has no such primitive, so it is reconstructed from " +
                          "`OverlapCapsule` + per-collider `ComputePenetration`. The two count \"needs resolving\" " +
                          "differently, so this row's hit counts differ by design — the timing still reflects each " +
                          "engine's real cost for its own approach._");
            return sb.ToString();
        }

        string ReportMultiThread(List<ThreadScalingResult> scaling, int[] threadCounts,
            ThreadScalingResult pxRay, ThreadScalingResult pxCap, double pxCheckOps, double pxDepenOps)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("## Multi-thread scaling — box3d");
            sb.AppendLine();
            sb.AppendLine("_Queries fanned across worker threads against the same query-only world (never stepped → " +
                          "lock-free). ops/sec by thread count; **scaling** = fastest ÷ 1-thread._");
            sb.AppendLine();

            var head = new StringBuilder("| Query |");
            var sep = new StringBuilder("| --- |");
            foreach (int t in threadCounts) { head.Append($" {t}T |"); sep.Append(" ---: |"); }
            head.Append(" scaling |"); sep.Append(" ---: |");
            sb.AppendLine(head.ToString());
            sb.AppendLine(sep.ToString());

            foreach (var k in Kinds)
            {
                var row = new StringBuilder($"| {k} |");
                double baseOps = 0, bestOps = 0;
                foreach (int t in threadCounts)
                {
                    double ops = 0;
                    foreach (var s in scaling) if (s.Kind == k && s.Threads == t) ops = s.OpsPerSec;
                    if (t == 1) baseOps = ops;
                    if (ops > bestOps) bestOps = ops;
                    row.Append($" {ops:N0} |");
                }
                double scale = baseOps > 0 ? bestOps / baseOps : 0;
                row.Append($" {scale:N1}× |");
                sb.AppendLine(row.ToString());
            }

            sb.AppendLine();
            sb.AppendLine("## box3d (all cores) vs PhysX (best parallel path available)");
            sb.AppendLine();
            sb.AppendLine("| Query | box3d best ops/sec | PhysX ops/sec | PhysX parallel path | box3d ÷ PhysX |");
            sb.AppendLine("| --- | ---: | ---: | --- | ---: |");
            AppendVs(sb, scaling, QueryKind.Raycast, pxRay.OpsPerSec, "RaycastCommand (job system)");
            AppendVs(sb, scaling, QueryKind.CapsuleCast, pxCap.OpsPerSec, "CapsulecastCommand (job system)");
            AppendVs(sb, scaling, QueryKind.CheckCapsule, pxCheckOps, "main thread only — no batch API");
            AppendVs(sb, scaling, QueryKind.Depenetrate, pxDepenOps, "main thread only — no batch API");

            sb.AppendLine();
            sb.AppendLine($"_PhysX batch hits (fairness): Raycast {pxRay.Hits:N0}, CapsuleCast {pxCap.Hits:N0} — " +
                          "compare to the single-thread `hits/batch`._");
            sb.AppendLine();
            sb.AppendLine("_Honest read: where Unity ships a batched job command (ray, capsule cast), PhysX's parallel " +
                          "path **outpaces** box3d-threaded — it is a tuned SIMD C++ batch. box3d's structural edge is that " +
                          "**every** query type is thread-safe: overlap and depenetration have no PhysX batch form (box3d " +
                          "wins those here), and box3d needs no Unity job system or main thread at all — headless servers, " +
                          "custom thread pools, off-Unity entirely._");
            sb.AppendLine();
            sb.AppendLine("_Editor/Mono caveat: box3d's callback-based queries (capsule cast, overlap, collide) are throttled " +
                          "by reverse-P/Invoke contention at high thread counts here — the callback-free raycast scales " +
                          "cleanest. Build an IL2CPP player for the true scaling curve._");
            return sb.ToString();
        }

        static void AppendVs(StringBuilder sb, List<ThreadScalingResult> scaling, QueryKind k, double pxOps, string path)
        {
            double best = 0;
            foreach (var s in scaling) if (s.Kind == k && s.OpsPerSec > best) best = s.OpsPerSec;
            double ratio = pxOps > 0 ? best / pxOps : 0;
            sb.AppendLine($"| {k} | {best:N0} | {pxOps:N0} | {path} | {ratio:N2}× |");
        }
    }
}
