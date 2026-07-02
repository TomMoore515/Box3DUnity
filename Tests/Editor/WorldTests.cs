// Functional tests against the real native library through the idiomatic API.
// Mirrors the validated Phase-0 spike: queries, the character-mover solver loop,
// far-from-origin precision, dynamics, and bitwise determinism.

using System;
using Box3D.Interop;
using NUnit.Framework;

namespace Box3D.Tests
{
    public class WorldTests
    {
        private Box3DWorld _world;
        private B3QueryFilter _filter;

        // Mover capsule: r=0.4, centers y 0.4 / 1.2 relative to origin => bottom == origin.y.
        private static readonly B3Capsule Mover = new B3Capsule
        {
            Center1 = new B3Vec3(0f, 0.4f, 0f),
            Center2 = new B3Vec3(0f, 1.2f, 0f),
            Radius = 0.4f,
        };

        [SetUp]
        public void SetUp()
        {
            // debugShapes: inert for simulation/queries; lets the debug-draw tests use this world.
            _world = new Box3DWorld(new B3Vec3(0f, -10f, 0f), debugShapes: true);
            _filter = Box3DWorld.DefaultQueryFilter();

            // Ground: top face at y = +1. Wall: x faces at 4.5 / 5.5, y 0..6.
            _world.CreateStaticBody(B3Pos.Zero).AddBox(50f, 1f, 50f);
            _world.CreateStaticBody(new B3Pos(5, 3, 0)).AddBox(0.5f, 3f, 10f);
        }

        [TearDown]
        public void TearDown() => _world.Dispose();

        [Test]
        public void CastRayClosest_HitsGround()
        {
            var ray = _world.CastRayClosest(new B3Pos(0, 10, 0), new B3Vec3(0, -20, 0), _filter);
            Assert.AreNotEqual(0, ray.Hit);
            Assert.AreEqual(0.45f, ray.Fraction, 1e-3f);
            Assert.AreEqual(1f, ray.Normal.Y, 1e-3f);
            Assert.AreEqual(1.0, ray.Point.Y, 1e-3);
        }

        [Test]
        public void CastMover_StopsOnGroundAndWall()
        {
            float down = _world.CastMover(new B3Pos(0, 5, 0), in Mover, new B3Vec3(0, -10, 0), _filter);
            Assert.AreEqual(0.4f, down, 0.02f, "downward mover cast");

            float wall = _world.CastMover(new B3Pos(0, 1.05, 0), in Mover, new B3Vec3(10, 0, 0), _filter);
            Assert.AreEqual(0.41f, wall, 0.02f, "wall mover cast");
        }

        [Test]
        public void CollideMover_SolvePlanes_Depenetrates()
        {
            // Origin y=0.9 puts the capsule bottom 0.1 into the ground.
            Span<B3PlaneResult> results = stackalloc B3PlaneResult[8];
            int count = _world.CollideMover(new B3Pos(0, 0.9, 0), in Mover, _filter, results);
            Assert.Greater(count, 0, "expected contact planes");

            Span<B3CollisionPlane> planes = stackalloc B3CollisionPlane[8];
            int planeCount = Box3DMover.ToCollisionPlanes(results.Slice(0, count), planes);

            var solved = Box3DMover.SolvePlanes(default, planes.Slice(0, planeCount));
            Assert.That(solved.Delta.Y, Is.InRange(0.05f, 0.15f), "depenetration push");

            var clipped = Box3DMover.ClipVector(new B3Vec3(1f, -1f, 0f), planes.Slice(0, planeCount));
            Assert.AreEqual(1f, clipped.X, 1e-3f);
            Assert.GreaterOrEqual(clipped.Y, -1e-3f, "velocity into the ground should be clipped");
        }

        [Test]
        public void CastCapsuleClosest_ReportsNormalAndPoint()
        {
            Assert.IsTrue(_world.CastCapsuleClosest(new B3Pos(0, 5, 0), in Mover, new B3Vec3(0, -10, 0), _filter, out var hit));
            Assert.AreEqual(0.4f, hit.Fraction, 0.02f);
            Assert.AreEqual(1f, hit.Normal.Y, 1e-2f);
        }

        [Test]
        public void OverlapCapsule_DetectsPenetration()
        {
            Assert.IsTrue(_world.OverlapCapsule(new B3Pos(0, 0.9, 0), in Mover, _filter), "penetrating the ground");
            Assert.IsFalse(_world.OverlapCapsule(new B3Pos(0, 3, 0), in Mover, _filter), "in the air");
        }

        [Test]
        public void MeshShape_RaycastHits()
        {
            // 10x10 grid of 1m cells on the XZ plane at y=0, centered near the origin.
            using var mesh = Box3DMesh.CreateGrid(10, 10, 1f);
            _world.CreateStaticBody(new B3Pos(20, 0, 0)).AddMesh(mesh, new B3Vec3(1f, 1f, 1f));

            var ray = _world.CastRayClosest(new B3Pos(20, 5, 0), new B3Vec3(0, -10, 0), _filter);
            Assert.AreNotEqual(0, ray.Hit, "expected mesh hit");
            Assert.AreEqual(1f, ray.Normal.Y, 1e-3f);
        }

        [Test]
        public void FarFromOrigin_QueriesStayPrecise()
        {
            _world.CreateStaticBody(new B3Pos(10000, 0, 10000)).AddBox(50f, 1f, 50f);

            var ray = _world.CastRayClosest(new B3Pos(10000, 10, 10000), new B3Vec3(0, -20, 0), _filter);
            Assert.AreNotEqual(0, ray.Hit);
            Assert.AreEqual(1.0, ray.Point.Y, 1e-3);

            float near = _world.CastMover(new B3Pos(0, 5, 0), in Mover, new B3Vec3(0, -10, 0), _filter);
            float far = _world.CastMover(new B3Pos(10000, 5, 10000), in Mover, new B3Vec3(0, -10, 0), _filter);
            Assert.AreEqual(near, far, 1e-4f, "mover cast should not degrade 10 km out");
        }

        [Test]
        public void StandaloneCasts_HitPosedCapsule()
        {
            // A capsule posed like a standing character at (10, 0, 5): feet to head along +Y.
            var target = new B3Capsule
            {
                Center1 = new B3Vec3(10f, 0.3f, 5f),
                Center2 = new B3Vec3(10f, 1.5f, 5f),
                Radius = 0.3f,
            };

            // Ray from 5 m out along -X hits the surface 0.3 m short of the axis.
            var origin = new B3Vec3(15f, 0.9f, 5f);
            Assert.IsTrue(Box3DGeometry.RayCastCapsule(in target, origin, new B3Vec3(-10f, 0f, 0f), out var ray));
            Assert.AreEqual(0.47f, ray.Fraction, 1e-3f, "ray fraction: (5 - 0.3) / 10");
            Assert.AreEqual(1f, ray.Normal.X, 1e-3f, "surface normal faces the ray");

            // A thick projectile (r = 0.15) stops its center one combined radius from the axis.
            Assert.IsTrue(Box3DGeometry.CastSphereAgainstCapsule(in target, origin, 0.15f, new B3Vec3(-10f, 0f, 0f), out var swept));
            Assert.AreEqual(0.455f, swept.Fraction, 2e-3f, "sphere-cast fraction: (5 - 0.45) / 10");

            // A ray passing a full diameter to the side misses.
            var missOrigin = new B3Vec3(15f, 0.9f, 5.9f);
            Assert.IsFalse(Box3DGeometry.RayCastCapsule(in target, missOrigin, new B3Vec3(-10f, 0f, 0f), out _));
        }

        [Test]
        public void DebugDraw_CapturesBoxAsSegments_WithHalfExtentEnvelope()
        {
            using var world = new Box3DWorld(default, debugShapes: true);
            world.CreateStaticBody(new B3Pos(10, 2, -5)).AddBox(1f, 2f, 3f);

            var snapshot = new Box3DDebugSnapshot();
            world.Draw(snapshot, Box3DDebugDrawOptions.Default);

            Assert.Greater(snapshot.Segments.Count, 0, "expected the box to tessellate into segments");

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (var s in snapshot.Segments)
            {
                minX = Math.Min(minX, Math.Min(s.A.X, s.B.X)); maxX = Math.Max(maxX, Math.Max(s.A.X, s.B.X));
                minY = Math.Min(minY, Math.Min(s.A.Y, s.B.Y)); maxY = Math.Max(maxY, Math.Max(s.A.Y, s.B.Y));
                minZ = Math.Min(minZ, Math.Min(s.A.Z, s.B.Z)); maxZ = Math.Max(maxZ, Math.Max(s.A.Z, s.B.Z));
            }

            // The drawn envelope must match the created box: center (10, 2, -5), half extents (1, 2, 3).
            // A factor-2 failure here means DrawBox extents are full-size, not half — fix AddBox, not this test.
            Assert.AreEqual(9.0, minX, 0.05, "min X"); Assert.AreEqual(11.0, maxX, 0.05, "max X");
            Assert.AreEqual(0.0, minY, 0.05, "min Y"); Assert.AreEqual(4.0, maxY, 0.05, "max Y");
            Assert.AreEqual(-8.0, minZ, 0.05, "min Z"); Assert.AreEqual(-2.0, maxZ, 0.05, "max Z");
        }

        [Test]
        public void DebugDraw_ClearsSnapshotBetweenCaptures()
        {
            var snapshot = new Box3DDebugSnapshot();
            _world.Draw(snapshot, Box3DDebugDrawOptions.Default);
            int first = snapshot.Segments.Count;
            Assert.Greater(first, 0);

            _world.Draw(snapshot, Box3DDebugDrawOptions.Default);
            Assert.AreEqual(first, snapshot.Segments.Count, "re-capturing must not accumulate");
        }

        [Test]
        public void Dynamics_SphereRests_And_IsDeterministic()
        {
            var rest1 = SimulateFallingSphere();
            Assert.AreEqual(1.5, rest1.Y, 0.05, "sphere should rest on the ground");

            var rest2 = SimulateFallingSphere();
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(rest1.X), BitConverter.DoubleToInt64Bits(rest2.X));
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(rest1.Y), BitConverter.DoubleToInt64Bits(rest2.Y));
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(rest1.Z), BitConverter.DoubleToInt64Bits(rest2.Z));
        }

        private static B3Pos SimulateFallingSphere()
        {
            using var world = new Box3DWorld(new B3Vec3(0f, -10f, 0f));
            world.CreateStaticBody(B3Pos.Zero).AddBox(50f, 1f, 50f);

            var ball = world.CreateBody(B3BodyType.Dynamic, new B3Pos(0.3, 3, 0.2));
            ball.AddSphere(new B3Sphere { Center = default, Radius = 0.5f });

            for (int i = 0; i < 120; i++)
                world.Step(1f / 60f);

            return ball.Position;
        }
    }
}
