// Ride Car Ordinary Pose Resolver Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation;
using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideCarOrdinaryPoseResolverTests {
  [Test]
  public void Resolve_ComposesExactDistancesCursorsSamplesAndFinitePose() {
    var traversal = new TrackCircuitTraversal(LongStadium());
    var geometry = Geometry();
    var locateCalls = 0;
    var sampleCalls = 0;
    var operations = new RideCarOrdinaryPoseResolverOperations(
      (owner, distance) => {
        locateCalls++;
        return owner.AtCircuitArcLength(distance);
      },
      cursor => {
        sampleCalls++;
        return cursor.Sample();
      });

    var result = RideCarOrdinaryPoseResolver.Resolve(
      traversal,
      7f,
      geometry,
      reversed: false,
      hasRearGeometry: true,
      operations);
    var expectedFront = traversal.AtCircuitArcLength(6f);
    var expectedRear = traversal.AtCircuitArcLength(4f);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.Traversal, Is.SameAs(traversal));
      Assert.That(result.Circuit, Is.SameAs(traversal.Circuit));
      Assert.That(result.Geometry, Is.SameAs(geometry));
      Assert.That(result.BaseDistance, Is.EqualTo(7f));
      Assert.That(result.Reversed, Is.False);
      Assert.That(result.HasRearGeometry, Is.True);
      Assert.That(result.ContactDistances.FrontDistance, Is.EqualTo(6f));
      Assert.That(result.ContactDistances.RearDistance, Is.EqualTo(4f));
      Assert.That(result.FrontCursor, Is.EqualTo(expectedFront));
      Assert.That(result.RearCursor, Is.EqualTo(expectedRear));
      Assert.That(result.FrontContact, Is.EqualTo(expectedFront.Sample()));
      Assert.That(result.RearContact, Is.EqualTo(expectedRear.Sample()));
      Assert.That(result.FrontContact.CircuitPiece,
        Is.SameAs(traversal.Circuit.Pieces[result.FrontContact.PieceIndex]));
      Assert.That(result.RearContact.CircuitPiece,
        Is.SameAs(traversal.Circuit.Pieces[result.RearContact.PieceIndex]));
      Assert.That(result.Pose.Circuit, Is.SameAs(traversal.Circuit));
      Assert.That(result.Pose.FrontContact, Is.EqualTo(result.FrontContact));
      Assert.That(result.Pose.RearContact, Is.EqualTo(result.RearContact));
      Assert.That(result.Transform, Is.EqualTo(result.Pose.Transform));
      Assert.That(result.Transform.GetDeterminant(), Is.EqualTo(1f).Within(0.00001f));
      Assert.That(locateCalls, Is.EqualTo(2));
      Assert.That(sampleCalls, Is.EqualTo(2));
      AssertFinite(result.Pose);
    }
  }

  [Test]
  public void Resolve_ReversedUsesNativeSignedDistancesAndFinitePose() {
    var traversal = new TrackCircuitTraversal(LongStadium());

    var result = RideCarOrdinaryPoseResolver.Resolve(
      traversal,
      7f,
      Geometry(),
      reversed: true,
      hasRearGeometry: true);

    using (Assert.EnterMultipleScope()) {
      Assert.That(result.ContactDistances.FrontDistance, Is.EqualTo(4f));
      Assert.That(result.ContactDistances.RearDistance, Is.EqualTo(6f));
      Assert.That(result.FrontCursor,
        Is.EqualTo(traversal.AtCircuitArcLength(4f)));
      Assert.That(result.RearCursor,
        Is.EqualTo(traversal.AtCircuitArcLength(6f)));
      Assert.That(result.Pose.Reversed, Is.True);
      Assert.That(result.Transform.GetDeterminant(), Is.EqualTo(1f).Within(0.00001f));
      AssertFinite(result.Pose);
    }
  }

  [Test]
  public void Resolve_PreservesOneNativeWrapAndRejectsMoreThanOneBeforeTraversal() {
    var traversal = new TrackCircuitTraversal(LongStadium());
    var circuitLength = traversal.Circuit.Length;

    var wrapped = RideCarOrdinaryPoseResolver.Resolve(
      traversal,
      circuitLength + 1.5f,
      Geometry(),
      reversed: false,
      hasRearGeometry: true);

    using (Assert.EnterMultipleScope()) {
      Assert.That(wrapped.ContactDistances.FrontDistance,
        Is.EqualTo(0.5f).Within(0.0001f));
      Assert.That(wrapped.ContactDistances.RearDistance,
        Is.EqualTo(circuitLength - 1.5f).Within(0.0001f));
      Assert.That(wrapped.FrontCursor,
        Is.EqualTo(traversal.AtCircuitArcLength(
          wrapped.ContactDistances.FrontDistance)));
      Assert.That(wrapped.RearCursor,
        Is.EqualTo(traversal.AtCircuitArcLength(
          wrapped.ContactDistances.RearDistance)));
      AssertFinite(wrapped.Pose);
    }

    var locateCalls = 0;
    var sampleCalls = 0;
    var operations = CountingOperations(
      () => locateCalls++,
      () => sampleCalls++);
    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarOrdinaryPoseResolver.Resolve(
        traversal,
        (2f * circuitLength) + 2f,
        Geometry(),
        reversed: false,
        hasRearGeometry: true,
        operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message,
        Does.Contain("requires more than one circuit correction"));
      Assert.That(locateCalls, Is.Zero);
      Assert.That(sampleCalls, Is.Zero);
    }
  }

  [Test]
  public void Resolve_MissingRearGeometryFailsClosedBeforeTraversal() {
    var traversal = new TrackCircuitTraversal(LongStadium());
    var locateCalls = 0;
    var sampleCalls = 0;
    var operations = CountingOperations(
      () => locateCalls++,
      () => sampleCalls++);

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarOrdinaryPoseResolver.Resolve(
        traversal,
        7f,
        Geometry(),
        reversed: false,
        hasRearGeometry: false,
        operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("rear geometry is unavailable"));
      Assert.That(locateCalls, Is.Zero);
      Assert.That(sampleCalls, Is.Zero);
    }
  }

  [Test]
  public void Resolve_RejectsCursorFromForeignTraversalBeforeSampling() {
    var circuit = LongStadium();
    var traversal = new TrackCircuitTraversal(circuit);
    var foreignTraversal = new TrackCircuitTraversal(circuit);
    var sampleCalls = 0;
    var operations = new RideCarOrdinaryPoseResolverOperations(
      (_, distance) => foreignTraversal.AtCircuitArcLength(distance),
      cursor => {
        sampleCalls++;
        return cursor.Sample();
      });

    var exception = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarOrdinaryPoseResolver.Resolve(
        traversal,
        7f,
        Geometry(),
        reversed: false,
        hasRearGeometry: true,
        operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(exception!.Message, Does.Contain("different traversal or circuit"));
      Assert.That(sampleCalls, Is.Zero);
    }
  }

  [Test]
  public void Resolve_RejectsForeignAndMutatedSampleIdentities() {
    var traversal = new TrackCircuitTraversal(LongStadium());
    var foreignTraversal = new TrackCircuitTraversal(LongStadium());
    var foreignOperations = new RideCarOrdinaryPoseResolverOperations(
      RideCarOrdinaryPoseResolverOperations.Default.Locate,
      cursor => foreignTraversal
        .AtCircuitArcLength(Convert.ToSingle(cursor.CircuitArcLength))
        .Sample());
    var mutatedOperations = new RideCarOrdinaryPoseResolverOperations(
      RideCarOrdinaryPoseResolverOperations.Default.Locate,
      cursor => cursor.Sample() with {
        CircuitArcLength = cursor.Sample().CircuitArcLength + 0.25f,
      });

    var foreignException = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarOrdinaryPoseResolver.Resolve(
        traversal,
        7f,
        Geometry(),
        reversed: false,
        hasRearGeometry: true,
        foreignOperations)));
    var mutatedException = Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarOrdinaryPoseResolver.Resolve(
        traversal,
        7f,
        Geometry(),
        reversed: false,
        hasRearGeometry: true,
        mutatedOperations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(foreignException!.Message,
        Does.Contain("sample changed its exact traversal or cursor identity"));
      Assert.That(mutatedException!.Message,
        Does.Contain("sample changed its exact traversal or cursor identity"));
    }
  }

  [Test]
  public void Resolve_RejectsNonFiniteMalformedAndIncompleteInputs() {
    var traversal = new TrackCircuitTraversal(LongStadium());
    var geometry = Geometry();
    var malformedGeometry = geometry with {
      CarFrontPosition = new Vector3(float.NaN, 0f, 0f),
    };
    var incompleteOperations = RideCarOrdinaryPoseResolverOperations.Default with {
      Sample = null!,
    };

    Assert.Throws<ArgumentNullException>(new Action(() =>
      RideCarOrdinaryPoseResolver.Resolve(
        null!,
        7f,
        geometry,
        reversed: false,
        hasRearGeometry: true)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      RideCarOrdinaryPoseResolver.Resolve(
        traversal,
        float.NaN,
        geometry,
        reversed: false,
        hasRearGeometry: true)));
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideCarOrdinaryPoseResolver.Resolve(
        traversal,
        7f,
        malformedGeometry,
        reversed: false,
        hasRearGeometry: true)));
    Assert.Throws<ArgumentException>(new Action(() =>
      RideCarOrdinaryPoseResolver.Resolve(
        traversal,
        7f,
        geometry,
        reversed: false,
        hasRearGeometry: true,
        incompleteOperations)));
  }

  private static RideCarOrdinaryPoseResolverOperations CountingOperations(
    Action onLocate,
    Action onSample
  ) => new(
    (traversal, distance) => {
      onLocate();
      return traversal.AtCircuitArcLength(distance);
    },
    cursor => {
      onSample();
      return cursor.Sample();
    });

  private static RideCarLongitudinalGeometry Geometry() {
    var carRear = Vector3.Zero;
    var carFront = new Vector3(4f, 0f, 0f);
    var frontWheel = new Vector3(3f, 0f, 0f);
    var rearWheel = new Vector3(1f, 0f, 0f);
    return new(
      carFront,
      carRear,
      frontWheel,
      rearWheel,
      Vector3.UnitX,
      4f,
      3f,
      1f,
      2f,
      2f,
      2f);
  }

  private static TrackCircuit LongStadium() {
    const float tangentScale = 3f;
    return new TrackCircuit([
      CircuitPiece("bottom", new(-5f, 0f, 0f), new(5f, 0f, 0f),
        Vector3.UnitX * tangentScale, Vector3.UnitX * tangentScale),
      CircuitPiece("lower-right", new(5f, 0f, 0f), new(7f, 0f, 2f),
        Vector3.UnitX * tangentScale, Vector3.UnitZ * tangentScale),
      CircuitPiece("right", new(7f, 0f, 2f), new(7f, 0f, 8f),
        Vector3.UnitZ * tangentScale, Vector3.UnitZ * tangentScale),
      CircuitPiece("upper-right", new(7f, 0f, 8f), new(5f, 0f, 10f),
        Vector3.UnitZ * tangentScale, -Vector3.UnitX * tangentScale),
      CircuitPiece("top", new(5f, 0f, 10f), new(-5f, 0f, 10f),
        -Vector3.UnitX * tangentScale, -Vector3.UnitX * tangentScale),
      CircuitPiece("upper-left", new(-5f, 0f, 10f), new(-7f, 0f, 8f),
        -Vector3.UnitX * tangentScale, -Vector3.UnitZ * tangentScale),
      CircuitPiece("left", new(-7f, 0f, 8f), new(-7f, 0f, 2f),
        -Vector3.UnitZ * tangentScale, -Vector3.UnitZ * tangentScale),
      CircuitPiece("lower-left", new(-7f, 0f, 2f), new(-5f, 0f, 0f),
        -Vector3.UnitZ * tangentScale, Vector3.UnitX * tangentScale),
    ]);
  }

  private static TrackCircuitPiece CircuitPiece(
    string id,
    Vector3 start,
    Vector3 end,
    Vector3 startTangent,
    Vector3 endTangent
  ) {
    var startGauge = Vector3.UnitY * 0.5f;
    var endGauge = Vector3.UnitY * 0.5f;
    return new(id, new TrackPiece(TrackPieceGeometry.FromHandAuthored([
      new RailControlPair(
        0f,
        start - startGauge,
        startTangent,
        start + startGauge,
        startTangent,
        0f),
      new RailControlPair(
        1f,
        end - endGauge,
        endTangent,
        end + endGauge,
        endTangent,
        0f),
    ])));
  }

  private static void AssertFinite(RideCarStaticPose pose) {
    using (Assert.EnterMultipleScope()) {
      AssertVectorFinite(pose.ContactMidpoint);
      AssertVectorFinite(pose.Forward);
      AssertVectorFinite(pose.Right);
      AssertVectorFinite(pose.Up);
      Assert.That(float.IsFinite(pose.Orientation.X), Is.True);
      Assert.That(float.IsFinite(pose.Orientation.Y), Is.True);
      Assert.That(float.IsFinite(pose.Orientation.Z), Is.True);
      Assert.That(float.IsFinite(pose.Orientation.W), Is.True);
      Assert.That(MatrixValues(pose.Transform), Has.All.Matches<float>(float.IsFinite));
    }
  }

  private static void AssertVectorFinite(Vector3 value) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(float.IsFinite(value.X), Is.True);
      Assert.That(float.IsFinite(value.Y), Is.True);
      Assert.That(float.IsFinite(value.Z), Is.True);
    }
  }

  private static float[] MatrixValues(Matrix4x4 value) => [
    value.M11, value.M12, value.M13, value.M14,
    value.M21, value.M22, value.M23, value.M24,
    value.M31, value.M32, value.M33, value.M34,
    value.M41, value.M42, value.M43, value.M44,
  ];
}
