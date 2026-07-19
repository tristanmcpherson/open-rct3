// CameraFramingTests
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using OpenCobra.GDK;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class CameraFramingTests {
  private const float Epsilon = 0.001f;

  // Uses the same shared framing calculation as Game.cs: center on the full OOB-inclusive terrain's
  // actual XYZ bounds, then use its 3D diagonal plus the validated safety margin as the distance.
  private static Camera FrameTerrain(Terrain terrain, float aspectRatio = 16f / 9f) {
    var camera = new Camera();
    var framing = TerrainCameraFraming.Calculate(terrain);
    camera.Frame(framing.Target, framing.Distance);
    camera.Update(aspectRatio);
    return camera;
  }

  private static Vector4 ProjectToClip(Camera camera, Vector3 worldPos) {
    Assert.That(camera.Value, Is.Not.Null);
    return Vector4.Transform(new Vector4(worldPos, 1f), camera.Value!.Value);
  }

  private static bool IsInsideClipSpace(Vector4 clip) =>
    clip.W > 0f
    && clip.X >= -clip.W && clip.X <= clip.W
    && clip.Y >= -clip.W && clip.Y <= clip.W
    && clip.Z >= -clip.W && clip.Z <= clip.W;

  private static void AssertInsideClipSpace(
    Camera camera,
    Vector3 worldPos,
    string? context = null) {
    var clip = ProjectToClip(camera, worldPos);
    var label = context == null ? $"corner {worldPos}" : $"{context} corner {worldPos}";
    Assert.That(clip.W, Is.GreaterThan(0f), $"{label} is behind the camera");
    Assert.That(clip.X, Is.InRange(-clip.W, clip.W), $"{label} X out of view");
    Assert.That(clip.Y, Is.InRange(-clip.W, clip.W), $"{label} Y out of view");
    Assert.That(clip.Z, Is.InRange(-clip.W, clip.W), $"{label} Z out of view");
  }

  // The corners of the *rendered mesh*, not just the buildable area: TerrainMeshBuilder renders the
  // full OOB-inclusive grid (see Terrain.cs / CornerPosition), which extends beyond BuildableBounds on
  // every side. Framing needs to keep this larger extent on-screen, not just the buildable area.
  private static Vector3[] FullMeshCorners(Terrain terrain, float minZ = 0f, float maxZ = 0f) {
    var (min, max) = terrain.Bounds;
    var minCorners = new[] {
      new Vector3(min.X, min.Y, minZ),
      new Vector3(max.X, min.Y, minZ),
      new Vector3(min.X, max.Y, minZ),
      new Vector3(max.X, max.Y, minZ),
    };
    if (minZ == maxZ) return minCorners;

    return [
      .. minCorners,
      new Vector3(min.X, min.Y, maxZ),
      new Vector3(max.X, min.Y, maxZ),
      new Vector3(min.X, max.Y, maxZ),
      new Vector3(max.X, max.Y, maxZ),
    ];
  }

  [Test]
  public void Calculate_CentersOnFullTerrainBoundsAndUsesThreeDimensionalDiagonal() {
    var minHeight = Terrain.WorldZToCornerHeight(-5f);
    var maxHeight = Terrain.WorldZToCornerHeight(15f);
    var terrain = new Terrain(width: 4, height: 2, initialHeight: minHeight);
    terrain.SetCornerHeight(
      terrain.Width - 1,
      terrain.Height - 1,
      TerrainCornerSlot.NorthEast,
      maxHeight
    );

    var framing = TerrainCameraFraming.Calculate(terrain);

    var (minXY, maxXY) = terrain.Bounds;
    var min = new Vector3(minXY, Terrain.CornerHeightToWorldZ(minHeight));
    var max = new Vector3(maxXY, Terrain.CornerHeightToWorldZ(maxHeight));
    Assert.That(
      Vector3.Distance(framing.Target, (min + max) * 0.5f),
      Is.EqualTo(0f).Within(Epsilon)
    );
    Assert.That(TerrainCameraFraming.DistanceMargin, Is.EqualTo(1.1f));
    Assert.That(
      framing.Distance,
      Is.EqualTo(Vector3.Distance(min, max) * TerrainCameraFraming.DistanceMargin)
        .Within(Epsilon)
    );
  }

  [Test]
  public void DefaultCamera_DoesNotFrameTheDefaultPark() {
    // Regression guard for the original bug: Camera's un-framed default (a small fixed offset from the
    // origin, sized for a toy scene) leaves the actual default park's buildable-area corners entirely
    // outside the view frustum.
    var park = new Park();
    var camera = new Camera();
    camera.Update(aspectRatio: 16f / 9f);

    var (min, max) = park.BuildableBounds;
    var corners = new[] {
      new Vector3(min.X, min.Y, 0),
      new Vector3(max.X, min.Y, 0),
      new Vector3(min.X, max.Y, 0),
      new Vector3(max.X, max.Y, 0),
    };

    var allOnScreen = corners.All(c => IsInsideClipSpace(ProjectToClip(camera, c)));
    Assert.That(allOnScreen, Is.False);
  }

  [Test]
  public void FramedCamera_KeepsDefaultParkRenderedMeshCornersOnScreen() {
    var terrain = new Terrain();
    var camera = FrameTerrain(terrain);

    foreach (var corner in FullMeshCorners(terrain)) {
      // Regression guard: a fixed far clip plane (previously hardcoded at 1000) doesn't scale with the
      // framing distance Game.cs computes from the terrain's actual size. Camera.Update now derives
      // the far plane from the eye-to-target distance itself (see Camera.cs), so the same projection
      // remains valid as the shared framing helper scales across park sizes.
      AssertInsideClipSpace(camera, corner);
    }
  }

  [Test]
  public void FramedCamera_KeepsSmallerCustomParkRenderedMeshCornersOnScreen() {
    // Same check against a much smaller map, to confirm the framing scales rather than being tuned to
    // one specific map size.
    var terrain = new Terrain(width: 16, height: 16);
    var camera = FrameTerrain(terrain);

    foreach (var corner in FullMeshCorners(terrain))
      AssertInsideClipSpace(camera, corner);
  }

  [Test]
  public void FramedCamera_KeepsLargerCustomParkRenderedMeshCornersOnScreen() {
    // A map larger than the default proves the far plane truly scales with framing distance rather than
    // happening to clear a fixed constant that was merely large enough for the 128x128 default.
    var terrain = new Terrain(width: 512, height: 512);
    var camera = FrameTerrain(terrain);

    foreach (var corner in FullMeshCorners(terrain))
      AssertInsideClipSpace(camera, corner);
  }

  [TestCase("Go With the Flow", 99, 119, -10f, 15f, 624, 381)]
  [TestCase("Go With the Flow", 99, 119, -10f, 15f, 1920, 1009)]
  [TestCase("Valley of Kings", 90, 90, -4f, 112.907f, 624, 381)]
  [TestCase("Valley of Kings", 90, 90, -4f, 112.907f, 1920, 1009)]
  public void FramedCamera_KeepsRepresentativeLoadedMapBoundsInsideClipSpace(
    string mapName,
    int terrainWidth,
    int terrainHeight,
    float minZ,
    float maxZ,
    int viewportWidth,
    int viewportHeight) {
    // This extends the square, flat, 16:9 cases above with deterministic camera-math coverage for
    // representative loaded-map envelopes and observed window aspect ratios. Native resize and window
    // behavior remain manual acceptance concerns; this test does not create or drive a live window.
    var borderTiles = Park.OutOfBoundsBorder * 2;
    var minHeight = Terrain.WorldZToCornerHeight(minZ);
    var maxHeight = Terrain.WorldZToCornerHeight(maxZ);
    var terrain = new Terrain(
      terrainWidth - borderTiles,
      terrainHeight - borderTiles,
      minHeight
    );
    terrain.SetCornerHeight(
      terrain.Width - 1,
      terrain.Height - 1,
      TerrainCornerSlot.NorthEast,
      maxHeight
    );
    var aspectRatio = Convert.ToSingle(viewportWidth) / viewportHeight;
    var camera = FrameTerrain(terrain, aspectRatio);
    var actualMinZ = Terrain.CornerHeightToWorldZ(minHeight);
    var actualMaxZ = Terrain.CornerHeightToWorldZ(maxHeight);

    foreach (var corner in FullMeshCorners(terrain, actualMinZ, actualMaxZ))
      AssertInsideClipSpace(camera, corner, mapName);
  }
}
