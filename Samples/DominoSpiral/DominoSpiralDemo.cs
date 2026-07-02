// Domino spiral demo: an Archimedean spiral of dynamic box3d dominoes toppled from the
// center outward. Everything (physics world, dominoes, floor visual, camera motion) is
// built in code — add this component to an empty GameObject in any scene and press Play.
// Unity is pure presentation here: box3d owns the simulation, GameObjects mirror it.
//
// Click-drag any domino to shove things around: picking uses box3d's own raycast (the
// visuals carry no PhysX colliders), and the grabbed body turns kinematic and is driven
// with SetTargetTransform so it pushes the dynamics with real velocity. Release to let
// it go dynamic again (momentum carries, so you can fling it).

using System.Collections.Generic;
using Box3D.Interop;
using UnityEngine;

namespace Box3D.Samples
{
    public sealed class DominoSpiralDemo : MonoBehaviour
    {
        [Header("Spiral")]
        [SerializeField] private float innerRadius = 2f;
        [SerializeField] private float gapBetweenTurns = 1.8f;
        [SerializeField] private float turns = 3.5f;
        [SerializeField] private float spacing = 0.45f;

        [Header("Domino (meters)")]
        [SerializeField] private float height = 1f;
        [SerializeField] private float width = 0.5f;
        [SerializeField] private float thickness = 0.15f;

        [Header("Run")]
        [Tooltip("Seconds after Play before the first domino is pushed. Set negative to disable the auto-topple.")]
        [SerializeField] private float toppleDelay = 1.5f;
        [Tooltip("Speed (m/s) imparted at the first domino's top edge.")]
        [SerializeField] private float pushSpeed = 1.5f;
        [SerializeField] private bool orbitCamera = true;
        [SerializeField] private float orbitDegreesPerSecond = 8f;

        private const float TimeStep = 1f / 60f;
        private const float TableHalfExtent = 28f;

        private Box3DWorld _world;
        private readonly List<Box3DBody> _bodies = new List<Box3DBody>();
        private readonly List<Transform> _views = new List<Transform>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly Dictionary<B3ShapeId, int> _shapeToIndex = new Dictionary<B3ShapeId, int>();
        private GameObject _root;
        private B3QueryFilter _queryFilter;
        private Vector3 _firstTangent;
        private float _accumulator;
        private float _clock;
        private float _orbitAngle;
        private bool _toppled;
        private float _stepMs;
        private float _cameraDistance;

        // drag state
        private int _dragIndex = -1;
        private B3Quat _dragRotation;
        private B3Pos _dragTarget;
        private Vector3 _dragOffset;
        private float _dragPlaneY;

        private void OnEnable() => Build();

        private void OnDisable() => TearDown();

        [ContextMenu("Restart")]
        public void Restart()
        {
            TearDown();
            Build();
        }

        // ---------------- setup ----------------

        private void Build()
        {
            _world = new Box3DWorld(new B3Vec3(0f, -9.81f, 0f));
            _queryFilter = Box3DWorld.DefaultQueryFilter();
            _root = new GameObject("DominoSpiral Views");
            _root.transform.SetParent(transform, false);
            _clock = 0f;
            _accumulator = 0f;
            _orbitAngle = 0f;
            _toppled = false;
            _dragIndex = -1;

            BuildFloor();
            BuildSpiral();

            _cameraDistance = innerRadius + gapBetweenTurns * turns + 6f;
        }

        private void BuildFloor()
        {
            _world.CreateStaticBody(new B3Pos(0, -1, 0)).AddBox(30f, 1f, 30f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            floor.name = "Floor";
            floor.localScale = new Vector3(60f, 2f, 60f);
            floor.position = new Vector3(0f, -1f, 0f);
            Destroy(floor.GetComponent<Collider>()); // visuals only — PhysX plays no part here
            Tint(floor, new Color(0.22f, 0.22f, 0.25f));
            floor.SetParent(_root.transform, true);
        }

        private void BuildSpiral()
        {
            // Archimedean spiral r = r0 + k*theta, stepped by arc length so domino spacing
            // stays constant: ds = sqrt(r^2 + k^2) dtheta.
            float k = gapBetweenTurns / (2f * Mathf.PI);
            float maxTheta = turns * 2f * Mathf.PI;

            var def = Box3DWorld.DefaultBodyDef();
            def.Type = B3BodyType.Dynamic;

            // Rough total count for the color gradient.
            float meanRadius = innerRadius + k * maxTheta * 0.5f;
            int estimated = Mathf.Max(1, Mathf.CeilToInt(meanRadius * maxTheta / spacing));

            float theta = 0f;
            int index = 0;
            while (theta < maxTheta)
            {
                float r = innerRadius + k * theta;
                var position = new Vector3(r * Mathf.Cos(theta), height * 0.5f, r * Mathf.Sin(theta));
                var tangent = new Vector3(
                    k * Mathf.Cos(theta) - r * Mathf.Sin(theta),
                    0f,
                    k * Mathf.Sin(theta) + r * Mathf.Cos(theta)).normalized;
                var rotation = Quaternion.LookRotation(tangent, Vector3.up);

                if (index == 0)
                    _firstTangent = tangent;

                def.Position = position.ToB3Pos();
                def.Rotation = rotation.ToB3();
                var body = _world.CreateBody(in def);
                var shape = body.AddBox(width * 0.5f, height * 0.5f, thickness * 0.5f);
                _shapeToIndex[shape.Id] = index;
                _bodies.Add(body);

                var color = Color.HSVToRGB(index / (float)estimated % 1f, 0.65f, 0.95f);
                var view = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
                view.name = $"Domino {index}";
                view.localScale = new Vector3(width, height, thickness);
                view.SetPositionAndRotation(position, rotation);
                Destroy(view.GetComponent<Collider>());
                Tint(view, color);
                view.SetParent(_root.transform, true);
                _views.Add(view);
                _colors.Add(color);

                theta += spacing / Mathf.Sqrt(r * r + k * k);
                index++;
            }
        }

        private static void Tint(Transform view, Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color); // URP Lit
            block.SetColor("_Color", color);     // built-in fallback
            view.GetComponent<Renderer>().SetPropertyBlock(block);
        }

        private void TearDown()
        {
            _world?.Dispose();
            _world = null;
            _bodies.Clear();
            _views.Clear();
            _colors.Clear();
            _shapeToIndex.Clear();
            _dragIndex = -1;
            if (_root != null)
                Destroy(_root);
        }

        // ---------------- run ----------------

        private void Update()
        {
            if (_world == null)
                return;

            _clock += Time.deltaTime;
            if (!_toppled && toppleDelay >= 0f && _clock >= toppleDelay)
            {
                Topple();
                _toppled = true;
            }

            HandleDrag();

            // Fixed-step accumulator; cap the backlog so a hitch doesn't spiral.
            _accumulator += Time.deltaTime;
            int steps = 0;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (_accumulator >= TimeStep && steps < 4)
            {
                if (_dragIndex >= 0)
                {
                    // Re-target every step: the body reaches the target after the first
                    // step, so later steps in the same frame see ~zero residual velocity.
                    _bodies[_dragIndex].SetTargetTransform(
                        new B3WorldTransform { P = _dragTarget, Q = _dragRotation }, TimeStep);
                }

                _world.Step(TimeStep);
                _accumulator -= TimeStep;
                steps++;
            }
            timer.Stop();
            if (steps > 0)
                _stepMs = (float)timer.Elapsed.TotalMilliseconds / steps;
            if (_accumulator > TimeStep)
                _accumulator = TimeStep;

            for (int i = 0; i < _bodies.Count; i++)
            {
                var t = _bodies[i].Transform;
                _views[i].SetPositionAndRotation(t.P.ToVector3(), t.Q.ToQuaternion());
            }
        }

        private void Topple()
        {
            var first = _bodies[0];
            var center = first.Position;
            var top = new B3Pos(center.X, center.Y + height * 0.4f, center.Z);
            var impulse = (_firstTangent * (first.Mass * pushSpeed)).ToB3();
            first.ApplyLinearImpulse(impulse, top);
        }

        // ---------------- mouse drag ----------------

        private void HandleDrag()
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            if (_dragIndex < 0)
            {
                if (!PointerPressedThisFrame())
                    return;

                var ray = cam.ScreenPointToRay(PointerPosition());
                var hit = _world.CastRayClosest(ray.origin.ToB3Pos(), (ray.direction * 200f).ToB3(), _queryFilter);
                if (hit.Hit == 0 || !_shapeToIndex.TryGetValue(hit.ShapeId, out int index))
                    return;

                var body = _bodies[index];
                var p = body.Position;

                _dragIndex = index;
                _dragRotation = body.Rotation;
                _dragTarget = p;
                _dragPlaneY = Mathf.Max((float)p.Y, thickness);
                _dragOffset = TryPlanePoint(ray, _dragPlaneY, out var grabPoint)
                    ? p.ToVector3() - grabPoint
                    : Vector3.zero;

                body.SetType(B3BodyType.Kinematic);
                Tint(_views[index], Color.white);
                return;
            }

            if (!PointerHeld())
            {
                ReleaseDrag();
                return;
            }

            var dragRay = cam.ScreenPointToRay(PointerPosition());
            if (TryPlanePoint(dragRay, _dragPlaneY, out var point))
            {
                var target = point + _dragOffset;
                target.x = Mathf.Clamp(target.x, -TableHalfExtent, TableHalfExtent);
                target.z = Mathf.Clamp(target.z, -TableHalfExtent, TableHalfExtent);
                _dragTarget = target.ToB3Pos();
            }
        }

        private void ReleaseDrag()
        {
            var body = _bodies[_dragIndex];
            body.SetType(B3BodyType.Dynamic);
            body.SetAwake(true);
            Tint(_views[_dragIndex], _colors[_dragIndex]);
            _dragIndex = -1;
        }

        /// <summary>Intersect a screen ray with the horizontal drag plane y = planeY.</summary>
        private static bool TryPlanePoint(Ray ray, float planeY, out Vector3 point)
        {
            point = default;
            if (Mathf.Abs(ray.direction.y) < 1e-4f)
                return false;

            float t = (planeY - ray.origin.y) / ray.direction.y;
            if (t < 0f || t > 500f)
                return false;

            point = ray.GetPoint(t);
            return true;
        }

        // Works with either input backend: the Input System package when active
        // (BOX3D_INPUTSYSTEM comes from the asmdef version define), else legacy input.

        private static Vector2 PointerPosition()
        {
#if BOX3D_INPUTSYSTEM && ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null ? (Vector2)mouse.position.ReadValue() : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return (Vector2)Input.mousePosition;
#else
            return Vector2.zero;
#endif
        }

        private static bool PointerPressedThisFrame()
        {
#if BOX3D_INPUTSYSTEM && ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }

        private static bool PointerHeld()
        {
#if BOX3D_INPUTSYSTEM && ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null && mouse.leftButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(0);
#else
            return false;
#endif
        }

        // ---------------- camera & overlay ----------------

        private void LateUpdate()
        {
            if (!orbitCamera || Camera.main == null)
                return;

            // Hold the camera still while dragging so the cursor-to-world mapping is stable.
            if (_dragIndex < 0)
                _orbitAngle += Time.deltaTime * orbitDegreesPerSecond * Mathf.Deg2Rad;

            var eye = new Vector3(
                Mathf.Cos(_orbitAngle) * _cameraDistance,
                _cameraDistance * 0.65f,
                Mathf.Sin(_orbitAngle) * _cameraDistance);
            Camera.main.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(-eye + Vector3.up * 2f));
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(10, 10, 560, 22),
                $"box3d {Box3DWorld.NativeVersion} — {_bodies.Count} dominoes — step {_stepMs:0.00} ms — click-drag a domino to shove things around");
        }
    }
}
