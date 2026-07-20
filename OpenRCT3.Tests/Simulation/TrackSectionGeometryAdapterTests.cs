// Track Section Geometry Adapter Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class TrackSectionGeometryAdapterTests {
  [Test]
  public void CreateCarGeometry_PreservesExactBezierShapeAsNormalizedHermiteRails() {
    var left = CreateRail("left", -1f, [1f, 3f]);
    var right = CreateRail("right", 1f, [2f, 6f]);

    var geometry = TrackSectionGeometryAdapter.CreateCarGeometry(
      CreateSectionLink(left, right));
    var piece = new TrackPiece(geometry);

    using (Assert.EnterMultipleScope()) {
      Assert.That(geometry.AuthoringMode, Is.EqualTo(TrackPieceAuthoringMode.HandAuthored));
      Assert.That(
        geometry.ControlPoints.Select(point => point.Parameter),
        Is.EqualTo(new[] { 0f, 0.5f, 1f }));
      Assert.That(
        geometry.ControlPoints.Select(point => point.LeftTangent),
        Is.EqualTo(Enumerable.Repeat(new Vector3(6f, 0.6f, 3f), 3)));
      Assert.That(
        geometry.ControlPoints.Select(point => point.RightTangent),
        Is.EqualTo(Enumerable.Repeat(new Vector3(6f, 0.6f, 3f), 3)));
      Assert.That(geometry.ControlPoints.All(point => point.BankRadians == 0f), Is.True);
      AssertVector(
        piece.EvaluateRail(RailSide.Left, 0.25f).Position,
        new Vector3(2f, -0.85f, 3f));
      AssertVector(
        piece.EvaluateRail(RailSide.Right, 0.75f).Position,
        new Vector3(10f, 1.75f, 7f));
    }
  }

  [Test]
  public void CreateCarGeometry_ReversedSwapsRailsAndNegatesReverseDerivatives() {
    var left = CreateRail("left", -1f, [1f, 3f]);
    var right = CreateRail("right", 1f, [2f, 6f]);
    var forward = TrackSectionGeometryAdapter.CreateCarGeometry(left, right);
    var reversed = TrackSectionGeometryAdapter.CreateCarGeometry(
      left,
      right,
      reversed: true);
    var forwardPiece = new TrackPiece(forward);
    var reversedPiece = new TrackPiece(reversed);

    foreach (var parameter in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f }) {
      var opposite = 1f - parameter;
      var expectedLeft = forwardPiece.EvaluateRail(RailSide.Right, opposite);
      var expectedRight = forwardPiece.EvaluateRail(RailSide.Left, opposite);
      var actualLeft = reversedPiece.EvaluateRail(RailSide.Left, parameter);
      var actualRight = reversedPiece.EvaluateRail(RailSide.Right, parameter);

      using (Assert.EnterMultipleScope()) {
        AssertVector(actualLeft.Position, expectedLeft.Position);
        AssertVector(actualRight.Position, expectedRight.Position);
        AssertVector(actualLeft.Tangent, -expectedLeft.Tangent);
        AssertVector(actualRight.Tangent, -expectedRight.Tangent);
      }
    }
  }

  [Test]
  public void CreateCarGeometry_RejectsCyclicRails() {
    var left = CreateRail("left", -1f, [1f, 3f]) with { Cyclic = true };
    var right = CreateRail("right", 1f, [2f, 6f]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionGeometryAdapter.CreateCarGeometry(left, right)));

    Assert.That(exception!.Message, Does.Contain("left car spline 'left' is cyclic"));
  }

  [Test]
  public void CreateCarGeometry_DoesNotTreatApproximateArcLengthsAsSplineParameters() {
    var left = CreateRail("left", -1f, [1f, 3f]);
    var right = CreateRail("right", 1f, [3f, 5f]);

    var geometry = TrackSectionGeometryAdapter.CreateCarGeometry(left, right);

    Assert.That(
      geometry.ControlPoints.Select(point => point.Parameter),
      Is.EqualTo(new[] { 0f, 0.5f, 1f }));
  }

  [Test]
  public void CreateCarGeometry_RejectsInteriorDerivativesThatWouldRequireAveraging() {
    var left = CreateRail("left", -1f, [1f, 3f]);
    var changedNodes = left.Nodes.ToArray();
    changedNodes[1] = changedNodes[1] with {
      NextControlOffset = new Vector3(2f, 0f, 0f),
    };
    left = left with { Nodes = changedNodes };
    var right = CreateRail("right", 1f, [2f, 6f]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionGeometryAdapter.CreateCarGeometry(left, right)));

    Assert.That(exception!.Message, Does.Contain(
      "node 1 has incompatible incoming and outgoing derivatives"));
  }

  [Test]
  public void CreateCarGeometry_AcceptsSerializedInteriorDerivativeRoundoff() {
    var left = CreateRail("left", -1f, [1f, 3f]);
    var changedNodes = left.Nodes.ToArray();
    changedNodes[1] = changedNodes[1] with {
      NextControlOffset = new Vector3(MathF.BitIncrement(1f), 0.5f, 0.1f),
    };
    left = left with { Nodes = changedNodes };
    var right = CreateRail("right", 1f, [2f, 6f]);

    Assert.DoesNotThrow(new Action(() =>
      TrackSectionGeometryAdapter.CreateCarGeometry(left, right)));
  }

  [TestCase(0f)]
  [TestCase(-1f)]
  [TestCase(float.NaN)]
  public void CreateCarGeometry_RejectsNonPositiveOrNonFiniteSegmentLengths(float length) {
    var left = CreateRail("left", -1f, [length, 3f], totalLength: 4f);
    var right = CreateRail("right", 1f, [2f, 6f]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionGeometryAdapter.CreateCarGeometry(left, right)));

    Assert.That(exception!.Message, Does.Contain("segment 0 length is not finite and positive"));
  }

  [Test]
  public void CreateCarGeometry_RejectsNonFiniteTotalBeforeConstructingGeometry() {
    var left = CreateRail(
      "left",
      -1f,
      [1f, 3f],
      totalLength: float.PositiveInfinity);
    var right = CreateRail("right", 1f, [2f, 6f]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionGeometryAdapter.CreateCarGeometry(left, right)));

    Assert.That(exception!.Message, Does.Contain(
      "total length is not finite and positive"));
  }

  [Test]
  public void CreateCarGeometry_AcceptsSerializedInverseLengthRoundoff() {
    var left = CreateRail("left", -1f, [1f, 3f]);
    left = left with {
      InverseTotalLength = MathF.BitIncrement(left.InverseTotalLength),
    };
    var right = CreateRail("right", 1f, [2f, 6f]);

    Assert.DoesNotThrow(new Action(() =>
      TrackSectionGeometryAdapter.CreateCarGeometry(left, right)));
  }

  [Test]
  public void CreateCarGeometry_RejectsMateriallyInconsistentInverseLength() {
    var left = CreateRail("left", -1f, [1f, 3f]);
    left = left with {
      InverseTotalLength = left.InverseTotalLength * 1.001f,
    };
    var right = CreateRail("right", 1f, [2f, 6f]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionGeometryAdapter.CreateCarGeometry(left, right)));

    Assert.That(exception!.Message, Does.Contain("inverse total length is inconsistent"));
  }

  [Test]
  public void CreateCarGeometry_AcceptsSerializedTotalLengthRoundoff() {
    var left = CreateRail(
      "left",
      -1f,
      [1f, 3f],
      totalLength: MathF.BitDecrement(4f));
    var right = CreateRail("right", 1f, [2f, 6f]);

    Assert.DoesNotThrow(new Action(() =>
      TrackSectionGeometryAdapter.CreateCarGeometry(left, right)));
  }

  [Test]
  public void CreateCarGeometry_RejectsMateriallyInconsistentTotalLength() {
    var left = CreateRail("left", -1f, [1f, 3f], totalLength: 4.1f);
    var right = CreateRail("right", 1f, [2f, 6f]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionGeometryAdapter.CreateCarGeometry(left, right)));

    Assert.That(exception!.Message, Does.Contain("segment lengths do not exactly sum"));
  }

  [Test]
  public void CreateCarGeometry_RejectsNonFiniteControlData() {
    var left = CreateRail("left", -1f, [1f, 3f]);
    var changedNodes = left.Nodes.ToArray();
    changedNodes[0] = changedNodes[0] with {
      Position = new Vector3(float.NaN, -1f, 0f),
    };
    left = left with { Nodes = changedNodes };
    var right = CreateRail("right", 1f, [2f, 6f]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionGeometryAdapter.CreateCarGeometry(left, right)));

    Assert.That(exception!.Message, Does.Contain("node 0 contains non-finite data"));
  }

  [Test]
  public void CreateCarGeometry_RejectsUnresolvedTksCarSpline() {
    var left = CreateRail("left", -1f, [1f, 3f]);
    var right = CreateRail("right", 1f, [2f, 6f]);
    var link = CreateSectionLink(left, right, resolveLeft: false);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionGeometryAdapter.CreateCarGeometry(link)));

    Assert.That(exception!.Message, Does.Contain("CarLeft link is unresolved"));
  }

  [Test]
  public void CreateCarGeometry_RejectsDuplicateTksCarSplineRoles() {
    var left = CreateRail("left", -1f, [1f, 3f]);
    var right = CreateRail("right", 1f, [2f, 6f]);
    var link = CreateSectionLink(left, right, duplicateLeft: true);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionGeometryAdapter.CreateCarGeometry(link)));

    Assert.That(exception!.Message, Does.Contain("duplicate CarLeft links"));
  }

  [Test]
  public void CreateCarGeometry_RejectsOversizedNodeListBeforeIndexingOrAllocating() {
    var left = CreateRail("left", -1f, [1f, 3f]) with {
      Nodes = new CountOnlyReadOnlyList<SplineNode>(1_000_001),
    };
    var right = CreateRail("right", 1f, [2f, 6f]);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      TrackSectionGeometryAdapter.CreateCarGeometry(left, right)));

    Assert.That(exception!.Message, Does.Contain(
      "node count 1000001 is outside the supported range"));
  }

  private static Spline CreateRail(
    string name,
    float lateral,
    IReadOnlyList<float> lengths,
    float? totalLength = null
  ) {
    var nodes = new[] {
      new SplineNode(
        new Vector3(0f, 2f, lateral),
        Vector3.Zero,
        new Vector3(1f, 0.5f, 0.1f)),
      new SplineNode(
        new Vector3(4f, 4f, lateral + 0.3f),
        new Vector3(-1f, -0.5f, -0.1f),
        new Vector3(1f, 0.5f, 0.1f)),
      new SplineNode(
        new Vector3(16f, 10f, lateral + 1.2f),
        new Vector3(-1f, -0.5f, -0.1f),
        Vector3.Zero),
    };
    var segments = lengths.Select(length =>
      new SplineSegment(length, new byte[14])).ToArray();
    var total = totalLength ?? lengths.Aggregate(0f, (sum, length) => sum + length);
    return new Spline(
      name,
      Cyclic: false,
      total,
      1f / total,
      MaximumY: 0f,
      nodes,
      segments);
  }

  private static TrackSectionResourceLink CreateSectionLink(
    Spline left,
    Spline right,
    bool resolveLeft = true,
    bool duplicateLeft = false
  ) {
    var section = CreateSection();
    var leftSource = new SplineResourceSource(
      new OvlFile(left.Name, FileType.Spline, "fixture.common.ovl"),
      left);
    var rightSource = new SplineResourceSource(
      new OvlFile(right.Name, FileType.Spline, "fixture.common.ovl"),
      right);
    var links = new List<TrackSectionSplineLink> {
      new(
        TrackSectionSplineRole.CarLeft,
        null,
        section.CarSplines.Left,
        resolveLeft ? leftSource : null),
      new(
        TrackSectionSplineRole.CarRight,
        null,
        section.CarSplines.Right,
        rightSource),
    };
    if (duplicateLeft)
      links.Add(new(
        TrackSectionSplineRole.CarLeft,
        null,
        section.CarSplines.Left,
        leftSource));
    return new TrackSectionResourceLink(
      new TrackSectionResourceSource(
        new OvlFile(section.Name, FileType.TrackSection, "fixture.unique.ovl"),
        section),
      new TrackSectionSceneryLink(section.SceneryItem, null),
      links);
  }

  private static TrackSection CreateSection() => new(
    "section",
    TrackSectionVersion.Vanilla,
    "section",
    "scenery:sid",
    new TrackSectionEndpoint(0, 0, 0, 0, 0, null),
    new TrackSectionEndpoint(0, 0, 0, 0, 0, null),
    SpecialCurves: 0,
    Direction: 0,
    new TrackSectionSplinePair("left:spl", "right:spl"),
    new TrackSectionSplinePair("join-left:spl", "join-right:spl"),
    ExtraSplines: null,
    WaterSplines: null,
    Speeds: [],
    new TrackSectionAnimations(-1, -1, -1, -1, -1, -1, -1, -1, -1, null, null),
    new TrackSectionOptions(0, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 0f, 0f, 0, null),
    new TrackSectionBaseUnknowns(0, 0, 0, 0, 0, 0, 0, 0, 0),
    Expansion: null,
    Wild: null);

  private static void AssertVector(Vector3 actual, Vector3 expected) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.0001f));
      Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.0001f));
      Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.0001f));
    }
  }

  private sealed class CountOnlyReadOnlyList<T>(int count) : IReadOnlyList<T> {
    public int Count => count;
    public T this[int index] => throw new InvalidOperationException(
      $"Indexer must not be read for oversized fixture at {index}.");
    public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException(
      "Enumerator must not be read for oversized fixture.");
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
  }
}
