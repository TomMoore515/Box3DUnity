# Box3D for Unity

![Box3D](https://img.shields.io/badge/Package-Box3D-blueviolet?style=for-the-badge)
![Unity](https://img.shields.io/badge/Engine-Unity-orange?style=for-the-badge)
![C#](https://img.shields.io/badge/Language-C%23-blue?style=for-the-badge)

C# bindings and a Unity integration layer for [Box3D](https://github.com/erincatto/box3d),
Erin Catto's 3D physics engine. Unofficial — not affiliated with the upstream project.

- **Package:** `com.luthien.box3d-unity` (0.2.0)
- **Upstream:** box3d **v0.1.0**, built with the **double-precision (large world) ABI**
- **Platforms:** Windows x64 and Linux x64 (shipped, glibc ≥ 2.35). Others welcome via contribution.
- **License:** MIT (this package and Box3D itself)

## Installation

### Via Unity Package Manager (Git URL)

1. Open **Window > Package Manager**
2. Click the **+** button in the top-left
3. Select **Add package from git URL...**
4. Enter:
   ```
   https://github.com/TomMoore515/Box3DUnity.git
   ```
   To pin a specific release, append a tag — e.g. `...Box3DUnity.git#v0.2.0`.

### Manual Installation

Copy this folder into your project's `Assets` (or `Packages`) directory.

## Layers

| Assembly | Engine refs | What it is |
| --- | --- | --- |
| `Box3D.Interop` | none | Raw P/Invoke: blittable structs mirroring the C ABI, function names verbatim from `box3d.h` — diffs directly against upstream headers. |
| `Box3D` | none | Idiomatic API: `Box3DWorld`, `Box3DBody`/`Box3DShape` handles, `Box3DMesh`, the `Box3DMover` character-mover solver, allocation-free queries over `Span<T>`. |
| `Box3D.Unity` | UnityEngine | Thin adapter: `Vector3`/`Quaternion` conversions, and the debug-draw renderer (`Box3DDebugLineMesh` + a vertex-color line shader). |

The engine-free core means the same assemblies drive headless servers, `dotnet test`
suites, and Unity clients identically.

## Quick start

```csharp
using Box3D;
using Box3D.Interop;

using var world = new Box3DWorld(gravity: new B3Vec3(0f, -9.8f, 0f));
world.CreateStaticBody(B3Pos.Zero).AddBox(50f, 1f, 50f);   // ground, top at y=1

var filter = Box3DWorld.DefaultQueryFilter();

// Raycast
var ray = world.CastRayClosest(new B3Pos(0, 10, 0), new B3Vec3(0, -20, 0), filter);

// Kinematic character mover (Quake-style collide & slide primitives)
var capsule = new B3Capsule { Center1 = new B3Vec3(0, 0.4f, 0), Center2 = new B3Vec3(0, 1.2f, 0), Radius = 0.4f };
float fraction = world.CastMover(new B3Pos(0, 5, 0), in capsule, new B3Vec3(0, -10, 0), filter);

Span<B3PlaneResult> contacts = stackalloc B3PlaneResult[8];
int n = world.CollideMover(new B3Pos(0, 0.9, 0), in capsule, filter, contacts);
Span<B3CollisionPlane> planes = stackalloc B3CollisionPlane[8];
n = Box3DMover.ToCollisionPlanes(contacts.Slice(0, n), planes);
var push = Box3DMover.SolvePlanes(default, planes.Slice(0, n));          // depenetration
var vel  = Box3DMover.ClipVector(new B3Vec3(1, -1, 0), planes.Slice(0, n)); // ClipVelocity

// Dynamics
var ball = world.CreateBody(B3BodyType.Dynamic, new B3Pos(0, 3, 0));
ball.AddSphere(new B3Sphere { Radius = 0.5f });
world.Step(1f / 60f);
```

## API coverage

**Bound:** world lifecycle/stepping; world queries (closest ray, raw ray/shape cast with
closest-hit helper, overlap, AABB overlap, capsule mover cast + collide); the plane solver
(`b3SolvePlanes` / `b3ClipVector`); bodies (create/destroy, transforms, target transforms,
velocities, forces/impulses, wake/sleep); shapes (sphere, capsule, box hull, triangle mesh);
mesh building (from triangle soup, box, grid); world debug draw (`b3World_Draw` /
`b3DefaultDebugDraw` — see below).

**Not yet bound:** joints, contact/sensor/body events, compound shapes, height fields,
convex hulls from point clouds, the external task-system hooks, world tuning setters, the
recorder/replay API (`b3RecPlayer_*`). The raw layer is additive — contributions only need
a `B3Api` entry and, where useful, a core wrapper.

## Debug draw

box3d draws shape geometry **only through the user debug-shape mechanism**: hooks installed in
`b3WorldDef` at world creation bake a drawable handle per shape (lazily, on the first
`b3World_Draw`), and every draw hands those handles back through `b3DebugDraw.DrawShapeFcn`.
The engine never decomposes shapes into the primitive callbacks itself — those serve
contacts/joints/bounds on stepped worlds. This package wires the whole pipeline:

- Create the world with `new Box3DWorld(gravity, debugShapes: true)` (or
  `Box3DWorld.EnableDebugShapes(ref def)` on a caller-built def). Costless until the first draw;
  a world created *without* the hooks draws no shapes.
- `world.Draw(snapshot, options)` captures into an engine-free `Box3DDebugSnapshot`: baked
  shape wireframes posed by their body transforms, tessellated to world-space line segments
  (double-precision endpoints). With no explicit `DrawingBounds`, everything draws (upstream's
  default would cull beyond ±100 m).
- `Box3DDebugLineMesh` (Box3D.Unity) uploads the snapshot as one vertex-colored line mesh and
  submits it to **all** cameras (Scene view, Game view, player builds) via
  `Graphics.RenderMesh` — one draw call per pass, so it scales to very large worlds where
  per-line gizmo drawing would not.

```csharp
using var world = new Box3DWorld(default, debugShapes: true);
// ... add bodies/shapes ...

var snapshot = new Box3DDebugSnapshot();
world.Draw(snapshot, Box3DDebugDrawOptions.Default);   // capture — allocates; do it on demand, not per frame

var lines = new Box3DDebugLineMesh();
lines.Build(snapshot);                                  // double→float; pass an origin shift far from 0
var material = Box3DDebugLineMesh.CreateLineMaterial(); // depthTest:false for an x-ray pass

// each frame while visible:
lines.Render(material);
```

Threading: treat `Draw` like `Step`, not like a query — the first draw mutates the world
(lazy per-shape handle baking), so it needs exclusive access.

Bake coverage: spheres and capsules tessellate exactly; **hulls bake their local-space AABB**
— exact for box hulls, an envelope for arbitrary hulls (the half-edge walk needs hull
internals the public headers don't define); mesh / compound / height-field shapes draw
nothing yet.

## ABI notes (important)

- The shipped native library is built with `BOX3D_DOUBLE_PRECISION`: world positions
  (`B3Pos`) are doubles, everything else floats. This is a compile-time ABI choice —
  the structs in `Box3D.Interop` match **only** this build. Box3D renames
  `b3CreateWorld` to `b3CreateWorldDoublePrecision` specifically so a mismatched
  binary fails at load instead of corrupting memory.
- All `b3*Def` structs carry an internal validation cookie: always start from
  `Box3DWorld.Default*Def()` (never `new`/`default`).
- C `bool` is 1 byte; the interop layer uses `byte` to stay blittable.
- Every interop struct is covered by an ABI size-parity test (`Tests/Editor/InteropLayoutTests`)
  against a checker compiled from the real headers.

## Threading

World queries are safe from any thread while no thread is stepping that world; `Step`
requires exclusive access. Queries are re-entrant and allocation-free (results gather
into caller-provided spans through stack-based contexts).

## Performance

box3d is used here as a **collision-query service** (rays, casts, overlaps, the mover), not a
dynamics engine, so the numbers that matter are query throughput and — the reason this binding
exists — *where* those queries can run. The `Samples/PhysicsBenchmark` sample loads an identical
field of 10,000 static boxes into a box3d world and a Unity PhysX collider scene and hits both with
the same seeded query batches; every row carries a hit-count validity gate (identical geometry +
queries must return identical hits). Figures below are editor/Mono on a i9 13900K (32 logical cores),
indicative only — build an IL2CPP player for real numbers.

**Single thread** — PhysX's mature C++ broadphase leads on raw casts; overlap is a tie; world build
strongly favours box3d (from data vs instantiating colliders):

| Query | box3d | PhysX |
| --- | ---: | ---: |
| Raycast | 0.9M/s | **1.4M/s** |
| CapsuleCast | 0.56M/s | **0.92M/s** |
| Overlap (`CheckCapsule`) | 2.0M/s | **2.2M/s** |
| World build (10k boxes) | **17 ms** (from data) | 95 ms (GameObject colliders) |

**Multi thread** — the real reason to reach for box3d. Its queries are thread-safe from any thread
while nothing steps the world, so they fan across cores. PhysX's *only* parallel query path is the
batched job API (`RaycastCommand` / `CapsulecastCommand`):

| Query | box3d (N threads) | PhysX parallel | Note |
| --- | ---: | ---: | --- |
| Raycast | 10M/s | **24M/s** | PhysX `RaycastCommand` wins independent casts |
| CapsuleCast | 4M/s | **19M/s** | PhysX `CapsulecastCommand` wins |
| Overlap | **7M/s** | 2.2M/s | PhysX has **no** parallel overlap — main thread only |
| Depenetrate | **7M/s** | 1.7M/s | PhysX has **no** parallel form — main thread only |

Read honestly:

- Where Unity ships a batched job command (ray, capsule cast), **PhysX's parallel path is faster** —
  a tuned SIMD C++ batch. Don't claim a blanket multithread win.
- box3d's edge is that **every** query type is thread-safe, including overlap and depenetration, which
  have no PhysX parallel form at all — and that box3d needs **no Unity job system and no main thread**,
  so the same queries run on a headless server, a `dotnet` process, or your own thread pool. Batched
  commands also cannot express a *sequential* collide-and-slide loop (each sweep depends on the last),
  so independent-query throughput is the wrong metric for a character controller.
- **Cross-platform determinism** is a capability, not a speed: box3d's Windows and Linux builds produce
  bit-identical results, which Unity/PhysX does not guarantee — decisive for
  client-predicted / server-authoritative netcode. IL2CPP cannot buy this back for PhysX.
- **Editor/Mono caveat:** box3d's callback-based queries (capsule / overlap / mover) hit reverse-P/Invoke
  contention past ~8 threads in the editor; only the callback-free raycast scales to full core count
  there. IL2CPP largely removes this bottleneck, so the numbers above are not indicative of an optimal player.

Bottom line: choose box3d when you need collision **off the main thread across all query types**, **from
data with no scene**, or **deterministic across platforms**. Choose PhysX when you need the fastest
single-thread or job-batched casts and don't need those three properties.

## Samples

`Samples/DominoSpiral` — a spiral domino train simulated by box3d with Unity as pure
presentation. Add `DominoSpiralDemo` to an empty GameObject in any scene with a camera
and press Play; it builds the world, dominoes, floor, and camera motion in code and
topples the chain after a short delay (`Restart` via the component context menu).
Click-drag any domino to shove things around — picking uses box3d's raycast and the
grabbed body is driven kinematically via `SetTargetTransform`, so it pushes with real
velocity and can be flung. Works with both input backends (Input System or legacy).

`Samples/PhysicsBenchmark` — the box3d-vs-PhysX query benchmark behind the [Performance](#performance)
section. Self-contained and seeded: it builds one box field into both engines and times ray / capsule
cast / overlap / depenetrate, single-thread and across worker threads, writing a Markdown report.
Gated on a `BOX3D_BENCHMARK` scripting define (so it compiles into nothing until you opt in) and
gated, on the PhysX side, on the built-in Physics module being enabled — see the sample's own README.
Add `PhysicsBenchmark` to a GameObject in an empty scene and press Play.

Note: samples are a regular runtime assembly (MonoBehaviours cannot live in
editor-only assemblies), so exclude `Samples/` from production builds — delete the
folder, or gate the asmdef with a project-specific define constraint (as `PhysicsBenchmark` does).

## Rebuilding the native library

See `Native~/UPSTREAM.md` for the pinned upstream commit, CMake flags, and per-platform
build recipes (`build-windows.bat`, `build-linux.sh`).

## Tests

`Tests/Editor` contains the ABI layout parity suite and functional tests (queries,
mover solver loop, mesh shapes, far-from-origin precision, dynamics, bitwise determinism,
debug-draw capture).
Run via the Unity Test Runner (EditMode). Natives ship for Windows x64 and Linux x64, so
the functional tests run in a Windows or Linux editor.

## License

MIT License - see [LICENSE](LICENSE.md) for details.
