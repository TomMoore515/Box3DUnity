using Box3D.Interop;
using UnityEngine;

namespace Box3D
{
    /// <summary>Conversions between Unity math types and box3d interop types.</summary>
    public static class B3Convert
    {
        public static B3Vec3 ToB3(this Vector3 v) => new B3Vec3(v.x, v.y, v.z);

        public static Vector3 ToVector3(this B3Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        public static B3Pos ToB3Pos(this Vector3 v) => new B3Pos(v.x, v.y, v.z);

        /// <summary>Lossy far from the origin: B3Pos is double, Vector3 is float.</summary>
        public static Vector3 ToVector3(this B3Pos p) => new Vector3((float)p.X, (float)p.Y, (float)p.Z);

        public static B3Quat ToB3(this Quaternion q) => new B3Quat { V = new B3Vec3(q.x, q.y, q.z), S = q.w };

        public static Quaternion ToQuaternion(this B3Quat q) => new Quaternion(q.V.X, q.V.Y, q.V.Z, q.S);
    }
}
