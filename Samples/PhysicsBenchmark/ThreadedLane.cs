using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Collections;
using UnityEngine;

namespace Box3D.Benchmark
{
    /// <summary>One throughput measurement at a given degree of parallelism.</summary>
    public struct ThreadScalingResult
    {
        public QueryKind Kind;
        public int Threads;        // >0 = box3d worker threads; -1 = PhysX job-system batch; 1 with Batch=false = main thread
        public bool Batch;         // true = PhysX *Command batch (job system)
        public double OpsPerSec;
        public long Hits;          // fairness signal vs the single-thread run
    }

    /// <summary>
    /// The multi-thread lane. box3d fans the query array across worker threads — the world is never
    /// stepped, so queries are lock-free and safe from any thread (each thread owns its stack for the
    /// query contexts). PhysX has no thread-safe scene-query call; its only parallel path is the
    /// batched job API (<see cref="RaycastCommand"/> / <see cref="CapsulecastCommand"/>), which we use
    /// so the comparison is fair. CheckCapsule / Depenetrate have no batch form at all — there box3d
    /// scales and PhysX stays on the main thread.
    /// </summary>
    public static class ThreadedLane
    {
        /// <summary>Powers of two up to the core cap, plus the cap itself (e.g. 1,2,4,8,16,32).</summary>
        public static int[] ThreadCounts(int max)
        {
            int cap = max > 0 ? max : Environment.ProcessorCount;
            var list = new List<int>();
            for (int t = 1; t < cap; t *= 2) list.Add(t);
            if (list.Count == 0 || list[list.Count - 1] != cap) list.Add(cap);
            return list.ToArray();
        }

        /// <summary>
        /// box3d: partition the query array into <paramref name="threadCount"/> contiguous slices, each
        /// slice swept <paramref name="repeats"/> times on its own thread. Total ops are constant across
        /// thread counts, so ops/sec vs the 1-thread run is the scaling curve. Thread creation is outside
        /// the timed region; wall-clock spans start→join.
        /// </summary>
        public static ThreadScalingResult RunBox3DThreaded(QueryKind kind, Func<int, bool> op,
            int queryCount, int repeats, int threadCount)
        {
            var threads = new Thread[threadCount];
            var localHits = new long[threadCount];
            int chunk = (queryCount + threadCount - 1) / threadCount;

            for (int t = 0; t < threadCount; t++)
            {
                int ti = t;
                int start = ti * chunk;
                int end = Math.Min(start + chunk, queryCount);
                threads[ti] = new Thread(() =>
                {
                    long h = 0;
                    for (int r = 0; r < repeats; r++)
                        for (int i = start; i < end; i++)
                            if (op(i)) h++;
                    localHits[ti] = h;
                });
            }

            var sw = Stopwatch.StartNew();
            for (int t = 0; t < threadCount; t++) threads[t].Start();
            for (int t = 0; t < threadCount; t++) threads[t].Join();
            sw.Stop();

            long ops = (long)queryCount * repeats;
            long hits = 0;
            for (int t = 0; t < threadCount; t++) hits += localHits[t];
            return new ThreadScalingResult
            {
                Kind = kind, Threads = threadCount, Batch = false,
                OpsPerSec = sw.Elapsed.TotalSeconds > 0 ? ops / sw.Elapsed.TotalSeconds : 0,
                Hits = hits,
            };
        }

        static QueryParameters DefaultParams()
            => new QueryParameters(UnityEngine.Physics.DefaultRaycastLayers, false, QueryTriggerInteraction.UseGlobal, false);

        /// <summary>PhysX raycasts via the batched job API — PhysX distributed across job worker threads.</summary>
        public static ThreadScalingResult RunPhysxRaycastBatch(RayQuery[] rays, int repeats)
        {
            int n = rays.Length;
            var qp = DefaultParams();
            var commands = new NativeArray<RaycastCommand>(n, Allocator.TempJob);
            var results = new NativeArray<RaycastHit>(n, Allocator.TempJob);
            for (int i = 0; i < n; i++)
                commands[i] = new RaycastCommand(rays[i].Origin, rays[i].Dir, qp, rays[i].Dist);

            RaycastCommand.ScheduleBatch(commands, results, 64, 1).Complete();   // warmup
            var sw = Stopwatch.StartNew();
            for (int r = 0; r < repeats; r++)
                RaycastCommand.ScheduleBatch(commands, results, 64, 1).Complete();
            sw.Stop();

            long hits = 0;
            for (int i = 0; i < n; i++) if (results[i].collider != null) hits++;
            commands.Dispose(); results.Dispose();

            long ops = (long)n * repeats;
            return new ThreadScalingResult
            {
                Kind = QueryKind.Raycast, Threads = -1, Batch = true,
                OpsPerSec = sw.Elapsed.TotalSeconds > 0 ? ops / sw.Elapsed.TotalSeconds : 0,
                Hits = hits,
            };
        }

        /// <summary>PhysX capsule casts via the batched job API.</summary>
        public static ThreadScalingResult RunPhysxCapsuleBatch(CapsuleQuery[] caps, int repeats)
        {
            int n = caps.Length;
            var qp = DefaultParams();
            var commands = new NativeArray<CapsulecastCommand>(n, Allocator.TempJob);
            var results = new NativeArray<RaycastHit>(n, Allocator.TempJob);
            for (int i = 0; i < n; i++)
                commands[i] = new CapsulecastCommand(caps[i].P1, caps[i].P2, caps[i].Radius, caps[i].Dir, qp, caps[i].Dist);

            CapsulecastCommand.ScheduleBatch(commands, results, 64, 1).Complete();   // warmup
            var sw = Stopwatch.StartNew();
            for (int r = 0; r < repeats; r++)
                CapsulecastCommand.ScheduleBatch(commands, results, 64, 1).Complete();
            sw.Stop();

            long hits = 0;
            for (int i = 0; i < n; i++) if (results[i].collider != null) hits++;
            commands.Dispose(); results.Dispose();

            long ops = (long)n * repeats;
            return new ThreadScalingResult
            {
                Kind = QueryKind.CapsuleCast, Threads = -1, Batch = true,
                OpsPerSec = sw.Elapsed.TotalSeconds > 0 ? ops / sw.Elapsed.TotalSeconds : 0,
                Hits = hits,
            };
        }
    }
}
