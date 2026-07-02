// ABI parity: every interop struct must have exactly the size reported by a C checker
// compiled from the box3d v0.1.0 headers with BOX3D_DOUBLE_PRECISION (see Native~/UPSTREAM.md).
// A mismatch here means memory corruption at the P/Invoke boundary — never ship past a red run.

using System.Runtime.InteropServices;
using Box3D.Interop;
using NUnit.Framework;

namespace Box3D.Tests
{
    public class InteropLayoutTests
    {
        private static void AssertSize<T>(int expected) where T : struct
            => Assert.AreEqual(expected, Marshal.SizeOf<T>(), $"{typeof(T).Name} ABI size mismatch");

        [Test] public void Vec3_Is12() => AssertSize<B3Vec3>(12);
        [Test] public void Pos_Is24_DoublePrecision() => AssertSize<B3Pos>(24);
        [Test] public void Quat_Is16() => AssertSize<B3Quat>(16);
        [Test] public void Transform_Is28() => AssertSize<B3Transform>(28);
        [Test] public void WorldTransform_Is40() => AssertSize<B3WorldTransform>(40);
        [Test] public void Plane_Is16() => AssertSize<B3Plane>(16);
        [Test] public void Aabb_Is24() => AssertSize<B3AABB>(24);
        [Test] public void Matrix3_Is36() => AssertSize<B3Matrix3>(36);
        [Test] public void WorldId_Is4() => AssertSize<B3WorldId>(4);
        [Test] public void BodyId_Is8() => AssertSize<B3BodyId>(8);
        [Test] public void ShapeId_Is8() => AssertSize<B3ShapeId>(8);
        [Test] public void Sphere_Is16() => AssertSize<B3Sphere>(16);
        [Test] public void Capsule_Is28() => AssertSize<B3Capsule>(28);
        [Test] public void HullData_Is136() => AssertSize<B3HullData>(136);
        [Test] public void BoxHull_Is440() => AssertSize<B3BoxHull>(440);
        [Test] public void MeshDef_Is40() => AssertSize<B3MeshDef>(40);
        [Test] public void ShapeProxy_Is16() => AssertSize<B3ShapeProxy>(16);
        [Test] public void Filter_Is24() => AssertSize<B3Filter>(24);
        [Test] public void QueryFilter_Is32() => AssertSize<B3QueryFilter>(32);
        [Test] public void SurfaceMaterial_Is40() => AssertSize<B3SurfaceMaterial>(40);
        [Test] public void MotionLocks_Is6() => AssertSize<B3MotionLocks>(6);
        [Test] public void WorldDef_Is144() => AssertSize<B3WorldDef>(144);
        [Test] public void Capacity_Is20() => AssertSize<B3Capacity>(20);
        [Test] public void BodyDef_Is120() => AssertSize<B3BodyDef>(120);
        [Test] public void ShapeDef_Is112() => AssertSize<B3ShapeDef>(112);
        [Test] public void RayResult_Is80() => AssertSize<B3RayResult>(80);
        [Test] public void PlaneResult_Is28() => AssertSize<B3PlaneResult>(28);
        [Test] public void CollisionPlane_Is28() => AssertSize<B3CollisionPlane>(28);
        [Test] public void PlaneSolverResult_Is16() => AssertSize<B3PlaneSolverResult>(16);
        [Test] public void TreeStats_Is8() => AssertSize<B3TreeStats>(8);
        [Test] public void Version_Is12() => AssertSize<B3Version>(12);
        [Test] public void RayCastInput_Is28() => AssertSize<B3RayCastInput>(28);
        [Test] public void DebugDraw_Is136() => AssertSize<B3DebugDraw>(136);
        [Test] public void DebugShape_Is24() => AssertSize<B3DebugShape>(24);
        [Test] public void ShapeCastInput_Is40() => AssertSize<B3ShapeCastInput>(40);
        [Test] public void CastOutput_Is48() => AssertSize<B3CastOutput>(48);

        [Test]
        public void NativeLibrary_Loads_And_Reports_Version()
        {
            var v = B3Api.b3GetVersion();
            Assert.AreEqual(0, v.Major, "expected box3d 0.x");
            Assert.GreaterOrEqual(v.Minor, 1);
        }
    }
}
