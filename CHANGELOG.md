# Changelog

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-07-03

### Added
- `PhysicsBenchmark` sample: a seeded, self-contained box3d-vs-Unity-PhysX collision-query
  benchmark (ray / capsule cast / overlap / depenetrate, single-thread and multi-thread),
  with a hit-count validity gate and a Markdown report. Gated on a `BOX3D_BENCHMARK` scripting
  define.
- README **Performance** section summarising the benchmark: PhysX leads single-thread casts;
  box3d's edge is universal off-main-thread thread-safety (all query types), build-from-data,
  and cross-platform determinism.

### Fixed
- `Box3D.Samples` was an editor-only assembly (`includePlatforms: ["Editor"]`), so its
  `DominoSpiralDemo` MonoBehaviour could not be added as a scene component — contradicting the
  sample's own instructions. It is now a regular runtime assembly.

## [0.2.0] - 2026-07-02

### Added
- World debug draw. Bound `b3World_Draw` / `b3DefaultDebugDraw` through the upstream
  `createDebugShape` / `destroyDebugShape` hooks, an engine-free `Box3DDebugSnapshot`
  capture, and a `Box3DDebugLineMesh` renderer (one vertex-colored line mesh submitted to
  all cameras via `Graphics.RenderMesh`) with a bundled line shader. Enable per world with
  `new Box3DWorld(gravity, debugShapes: true)`.
- Standalone (world-free) geometry casts via `Box3DGeometry`: safe wrappers over the raw
  ray / shape-cast primitives (sphere, capsule, hull) for stateless queries.

### Notes
- Debug-shape baking tessellates spheres and capsules exactly and bakes hulls to their
  local-space AABB (exact for box hulls, an envelope otherwise); mesh, compound, and
  height-field shapes draw nothing yet.
- `Draw` mutates the world on first call (lazy per-shape handle baking), so treat it like
  `Step`, not like a query.

## [0.1.0] - 2026-07-01

### Added
- Initial release. Three assemblies: `Box3D.Interop` (raw P/Invoke, engine-free, struct
  layouts mirroring the C ABI), `Box3D` (idiomatic API — `Box3DWorld`,
  `Box3DBody`/`Box3DShape` handles, `Box3DMesh`, the `Box3DMover` collide-and-slide solver,
  allocation-free queries over `Span<T>`), and `Box3D.Unity` (`Vector3`/`Quaternion`
  conversions).
- Bound surface: world lifecycle and stepping; queries (closest ray, ray/shape cast with
  closest-hit helper, overlap, AABB overlap, capsule mover cast + collide); the plane solver
  (`b3SolvePlanes` / `b3ClipVector`); bodies (create/destroy, transforms, target transforms,
  velocities, forces/impulses, wake/sleep); shapes (sphere, capsule, box hull, triangle
  mesh); mesh building (triangle soup, box, grid).
- Native binaries for Windows x64 and Linux x64, built against box3d **v0.1.0** with the
  double-precision (large-world) ABI.
- Tests: ABI size-parity suite plus functional tests (EditMode).
- `DominoSpiral` sample.

[0.2.0]: https://github.com/TomMoore515/Box3DUnity/releases/tag/v0.2.0
[0.1.0]: https://github.com/TomMoore515/Box3DUnity/releases/tag/v0.1.0
