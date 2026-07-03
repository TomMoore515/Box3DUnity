using UnityEngine;

namespace Box3D.Benchmark
{
    /// <summary>
    /// Deterministic, allocation-free PRNG (SplitMix64). The same seed produces the same stream on
    /// every platform and every thread, so geometry and query sets are reproducible and each worker
    /// thread can own a seeded copy without contention. Reproducibility is the whole point for
    /// published numbers — a benchmark nobody can re-run is marketing, not evidence.
    /// </summary>
    public struct Rng
    {
        ulong _state;

        public Rng(ulong seed) => _state = seed;

        public ulong NextULong()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (NextULong() >> 40) * (1f / (1 << 24));

        public float Range(float min, float max) => min + (max - min) * NextFloat();

        /// <summary>A point uniformly inside the axis-aligned cube of the given half-extent.</summary>
        public Vector3 InCube(float half) =>
            new Vector3(Range(-half, half), Range(-half, half), Range(-half, half));

        /// <summary>A uniformly distributed unit direction on the sphere.</summary>
        public Vector3 UnitVector()
        {
            float z = Range(-1f, 1f);
            float t = Range(0f, 2f * Mathf.PI);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(r * Mathf.Cos(t), r * Mathf.Sin(t), z);
        }
    }
}
