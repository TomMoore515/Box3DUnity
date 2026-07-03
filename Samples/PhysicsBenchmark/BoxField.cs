using UnityEngine;

namespace Box3D.Benchmark
{
    /// <summary>One axis-aligned box in the shared field.</summary>
    public struct FieldBox
    {
        public Vector3 Center;
        public Vector3 HalfExtents;
    }

    /// <summary>
    /// A deterministic field of axis-aligned static boxes — the shared geometry both engines load,
    /// so the two worlds hold the same shapes at the same places. Axis-aligned by design: it keeps
    /// the comparison about broadphase + query cost, not about how each engine handles rotated hulls
    /// (a legitimate but separate question). Synthetic box soup is honest to defend as a broadphase
    /// stress test; swap <see cref="Generate"/> for a structured layout if a reviewer wants realism.
    /// </summary>
    public static class BoxField
    {
        public static FieldBox[] Generate(int count, float volumeHalf, float minHalf, float maxHalf, ulong seed)
        {
            var rng = new Rng(seed);
            var boxes = new FieldBox[count];
            for (int i = 0; i < count; i++)
            {
                boxes[i] = new FieldBox
                {
                    Center = rng.InCube(volumeHalf),
                    HalfExtents = new Vector3(
                        rng.Range(minHalf, maxHalf),
                        rng.Range(minHalf, maxHalf),
                        rng.Range(minHalf, maxHalf)),
                };
            }
            return boxes;
        }

        /// <summary>
        /// Build a box3d query world from the field — no scene, no GameObjects, no main thread
        /// required. This is the headless-server build path the ADR-0004 rationale rests on.
        /// </summary>
        public static Box3DWorld BuildBox3D(FieldBox[] boxes)
        {
            var world = new Box3DWorld();   // query-only world, no gravity
            var def = Box3DWorld.DefaultShapeDef();
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                world.CreateStaticBody(b.Center.ToB3Pos())
                     .AddBox(b.HalfExtents.x, b.HalfExtents.y, b.HalfExtents.z, in def);
            }
            return world;
        }

        /// <summary>
        /// Build a PhysX collider scene from the same field: one static <see cref="BoxCollider"/>
        /// GameObject per box under a returned root (caller destroys it). Requires the main thread and
        /// a live scene — which is exactly why the whole PhysX lane runs in play mode. The build time
        /// here is itself a data point: box3d builds from data, PhysX needs instantiated colliders.
        /// </summary>
        public static GameObject BuildPhysx(FieldBox[] boxes)
        {
            var root = new GameObject("Box3D.Benchmark.PhysxField");
            var rootT = root.transform;
            for (int i = 0; i < boxes.Length; i++)
            {
                var b = boxes[i];
                var go = new GameObject("box");
                go.transform.SetParent(rootT, false);
                go.transform.position = b.Center;
                var col = go.AddComponent<BoxCollider>();
                col.size = b.HalfExtents * 2f;
            }
            // Queries read collider transforms from PhysX's cache; sync once after the build.
            UnityEngine.Physics.SyncTransforms();
            return root;
        }
    }
}
