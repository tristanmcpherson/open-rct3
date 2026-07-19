// NativeCameraAcceptanceTests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using System.Numerics;
using NUnit.Framework;
using OpenCobra.GDK;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class NativeCameraAcceptanceTests {
  private const float FramingDistanceMargin = 1.8f;

  [TestCase(624, 381)]
  [TestCase(1920, 1009)]
  public void FramedCamera_ContainsRectangularHeightRangeAcrossNativeWindowSizes(
    int viewportWidth,
    int viewportHeight) {
    // The buildable dimensions produce a 99x119 OOB-inclusive grid, representative of a tall,
    // asymmetric loaded map. The height range crosses zero so the check covers both valleys and hills.
    var terrain = new Terrain(width: 89, height: 109);
    var park = new Park(terrain);
    var camera = FrameOnPark(park);
    camera.Update(Convert.ToSingle(viewportWidth) / viewportHeight);

    var (min, max) = terrain.Bounds;
    foreach (var z in new[] { -10f, 15f }) {
      foreach (var corner in new[] {
        new Vector3(min.X, min.Y, z),
        new Vector3(max.X, min.Y, z),
        new Vector3(min.X, max.Y, z),
        new Vector3(max.X, max.Y, z),
      }) {
        var ndc = ProjectToNdc(camera, corner);
        Assert.That(ndc.X, Is.InRange(-1f, 1f), $"corner {corner} X out of view");
        Assert.That(ndc.Y, Is.InRange(-1f, 1f), $"corner {corner} Y out of view");
        Assert.That(ndc.Z, Is.InRange(-1f, 1f), $"corner {corner} Z out of view");
      }
    }
  }

  private static Camera FrameOnPark(Park park) {
    var camera = new Camera();
    var bounds = park.BuildableBounds;
    var center = new Vector3(
      (bounds.Min.X + bounds.Max.X) / 2f,
      (bounds.Min.Y + bounds.Max.Y) / 2f,
      0f
    );
    var diagonal = Vector2.Distance(bounds.Min, bounds.Max);
    camera.Frame(center, diagonal * FramingDistanceMargin);
    return camera;
  }

  private static Vector3 ProjectToNdc(Camera camera, Vector3 worldPos) {
    Assert.That(camera.Value, Is.Not.Null);
    var clip = Vector4.Transform(new Vector4(worldPos, 1f), camera.Value!.Value);
    return new Vector3(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
  }
}
