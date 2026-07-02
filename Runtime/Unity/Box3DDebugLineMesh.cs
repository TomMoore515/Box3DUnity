using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Box3D
{
    /// <summary>
    /// Renders a <see cref="Box3DDebugSnapshot"/> as a single GPU line mesh
    /// (<see cref="MeshTopology.Lines"/>, vertex-colored, 32-bit indices). Build once for static
    /// geometry and call <see cref="Render"/> every frame while visible — the per-frame cost is one
    /// draw call, so this scales to city-sized worlds where per-line gizmo/immediate drawing would
    /// not, and it works identically in the editor (all cameras, Scene view included) and in player
    /// builds. For per-frame content (contact markers etc.) just rebuild each frame — the lists and
    /// mesh are reused, so rebuilding small snapshots does not churn allocations.
    /// </summary>
    public sealed class Box3DDebugLineMesh : IDisposable
    {
        readonly List<Vector3> _vertices = new List<Vector3>(2048);
        readonly List<Color32> _colors = new List<Color32>(2048);
        readonly List<int> _indices = new List<int>(2048);
        Mesh _mesh;

        public int SegmentCount { get; private set; }

        /// <summary>
        /// (Re)build the mesh from the snapshot. <paramref name="originShift"/> is subtracted from
        /// every point before the double→float conversion — pass a nearby anchor (e.g. the camera
        /// or region origin) when drawing far from the world origin, and add it back via the
        /// object-to-world matrix in <see cref="Render"/>.
        /// </summary>
        public void Build(Box3DDebugSnapshot snapshot, Vector3 originShift = default)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            _vertices.Clear();
            _colors.Clear();
            _indices.Clear();

            var segments = snapshot.Segments;
            for (int i = 0; i < segments.Count; i++)
            {
                Box3DDebugSnapshot.Segment s = segments[i];
                _vertices.Add(new Vector3(
                    (float)(s.A.X - originShift.x), (float)(s.A.Y - originShift.y), (float)(s.A.Z - originShift.z)));
                _vertices.Add(new Vector3(
                    (float)(s.B.X - originShift.x), (float)(s.B.Y - originShift.y), (float)(s.B.Z - originShift.z)));

                Color32 c = ToColor32(s.Rgb, s.Alpha);
                _colors.Add(c);
                _colors.Add(c);

                _indices.Add(i * 2);
                _indices.Add(i * 2 + 1);
            }

            if (_mesh == null)
            {
                _mesh = new Mesh
                {
                    name = "Box3D Debug Lines",
                    hideFlags = HideFlags.HideAndDontSave,
                    indexFormat = IndexFormat.UInt32,
                };
                _mesh.MarkDynamic();
            }

            _mesh.Clear();
            _mesh.indexFormat = IndexFormat.UInt32;
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colors);
            _mesh.SetIndices(_indices, MeshTopology.Lines, 0);
            _mesh.RecalculateBounds();
            SegmentCount = segments.Count;
        }

        /// <summary>
        /// Submit the mesh for this frame, to all cameras (Game and Scene view). Call once per frame
        /// from any Update-phase code. <paramref name="objectToWorld"/> re-applies the origin shift
        /// passed to <see cref="Build"/>; identity when none was used.
        /// </summary>
        public void Render(Material material, Matrix4x4? objectToWorld = null)
        {
            if (_mesh == null || SegmentCount == 0)
                return;

            Matrix4x4 matrix = objectToWorld ?? Matrix4x4.identity;
            var rp = new RenderParams(material)
            {
                worldBounds = TransformedBounds(_mesh.bounds, matrix),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
            };
            Graphics.RenderMesh(rp, _mesh, 0, matrix);
        }

        public void Dispose()
        {
            if (_mesh == null)
                return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_mesh);
            else UnityEngine.Object.DestroyImmediate(_mesh);
            _mesh = null;
            SegmentCount = 0;
        }

        /// <summary>
        /// A material for these meshes: unlit vertex color, transparent, no ZWrite. The shader ships
        /// in the package's Resources so it survives build stripping. <paramref name="depthTest"/>
        /// false gives an x-ray pass (draws through geometry); <paramref name="tint"/> multiplies
        /// vertex colors — use its alpha to fade a whole pass.
        /// </summary>
        public static Material CreateLineMaterial(bool depthTest = true, Color? tint = null)
        {
            Shader shader = Resources.Load<Shader>("Box3DDebugLine");
            if (shader == null)
                shader = Shader.Find("Box3D/DebugLine");
            if (shader == null)
                throw new InvalidOperationException("Box3D/DebugLine shader not found (expected in Box3D.Unity Resources).");

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_ZTest", (int)(depthTest ? CompareFunction.LessEqual : CompareFunction.Always));
            material.SetColor("_Tint", tint ?? Color.white);
            return material;
        }

        static Color32 ToColor32(uint rgb, float alpha) => new Color32(
            (byte)(rgb >> 16 & 0xFF),
            (byte)(rgb >> 8 & 0xFF),
            (byte)(rgb & 0xFF),
            (byte)(Mathf.Clamp01(alpha) * 255f));

        static Bounds TransformedBounds(Bounds local, in Matrix4x4 matrix)
        {
            if (matrix.isIdentity)
                return local;
            Vector3 center = matrix.MultiplyPoint3x4(local.center);
            return new Bounds(center, local.size);
        }
    }
}
