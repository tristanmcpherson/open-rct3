// Ride Car Ordinary Pose Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>
/// Exact ordinary-mode contact distances, traversal identities, samples, and finite body pose.
/// </summary>
internal sealed record RideCarOrdinaryPose(
  TrackCircuitTraversal Traversal,
  TrackCircuit Circuit,
  float BaseDistance,
  RideCarLongitudinalGeometry Geometry,
  bool Reversed,
  bool HasRearGeometry,
  RideCarOrdinaryContactDistances ContactDistances,
  TrackCircuitCursor FrontCursor,
  TrackCircuitCursor RearCursor,
  TrackCircuitSample FrontContact,
  TrackCircuitSample RearContact,
  RideCarStaticPose Pose
) {
  public Matrix4x4 Transform => Pose.Transform;
}

/// <summary>Bounded seams for proving exact traversal and sample ownership.</summary>
/// <remarks>
/// The default operations call the ordinary traversal directly. Alternate operations are internal
/// so focused tests can prove that foreign cursors or samples fail closed before pose construction.
/// </remarks>
internal sealed record RideCarOrdinaryPoseResolverOperations(
  Func<TrackCircuitTraversal, float, TrackCircuitCursor> Locate,
  Func<TrackCircuitCursor, TrackCircuitSample> Sample
) {
  public static RideCarOrdinaryPoseResolverOperations Default { get; } = new(
    (traversal, distance) => traversal.AtCircuitArcLength(distance),
    cursor => cursor.Sample());
}

/// <summary>Composes native ordinary contact distances into one exact finite circuit pose.</summary>
/// <remarks>
/// <see cref="RideCarOrdinaryContactDistanceResolver"/> preserves Complete Edition's single
/// circuit correction at <c>0x00A55E8E</c> and <c>0x00A56412</c>. This resolver locates those two
/// corrected distances on the supplied traversal, validates the exact cursor and sample identities,
/// and delegates only the rigid transform to <see cref="RideCarStaticPoseBuilder"/>. A car without
/// rear geometry has one contact rather than a body chord, so it fails closed instead of inventing a
/// tangent or a second contact. The workflow resolves exactly two contact identities and has no
/// input-dependent loop.
/// </remarks>
internal static class RideCarOrdinaryPoseResolver {
  public static RideCarOrdinaryPose Resolve(
    TrackCircuitTraversal traversal,
    float baseDistance,
    RideCarLongitudinalGeometry geometry,
    bool reversed,
    bool hasRearGeometry
  ) => Resolve(
    traversal,
    baseDistance,
    geometry,
    reversed,
    hasRearGeometry,
    RideCarOrdinaryPoseResolverOperations.Default);

  internal static RideCarOrdinaryPose Resolve(
    TrackCircuitTraversal traversal,
    float baseDistance,
    RideCarLongitudinalGeometry geometry,
    bool reversed,
    bool hasRearGeometry,
    RideCarOrdinaryPoseResolverOperations operations
  ) {
    ArgumentNullException.ThrowIfNull(traversal);
    ArgumentNullException.ThrowIfNull(geometry);
    ArgumentNullException.ThrowIfNull(operations);
    ValidateOperations(operations);
    if (!float.IsFinite(baseDistance))
      throw new ArgumentOutOfRangeException(
        nameof(baseDistance),
        "Ordinary ride-car base distance must be finite.");

    var circuit = traversal.Circuit;
    if (!float.IsFinite(circuit.Length) || circuit.Length <= 0f ||
        !double.IsFinite(traversal.Length) || traversal.Length <= 0d)
      throw Invalid("track traversal has an invalid circuit length");
    var distances = RideCarOrdinaryContactDistanceResolver.Resolve(
      baseDistance,
      geometry,
      reversed,
      hasRearGeometry,
      circuit.Length);
    ValidateDistances(distances, circuit.Length, hasRearGeometry);
    if (!hasRearGeometry)
      throw Invalid(
        "rear geometry is unavailable, so the two-contact body chord cannot be built");

    var frontCursor = Locate(
      traversal,
      distances.FrontDistance,
      "front",
      operations);
    var rearCursor = Locate(
      traversal,
      distances.RearDistance,
      "rear",
      operations);
    var frontContact = Sample(
      traversal,
      frontCursor,
      distances.FrontDistance,
      "front",
      operations);
    var rearContact = Sample(
      traversal,
      rearCursor,
      distances.RearDistance,
      "rear",
      operations);
    var pose = RideCarStaticPoseBuilder.Build(
      circuit,
      frontContact,
      rearContact,
      geometry,
      reversed);
    ValidatePose(
      traversal,
      frontContact,
      rearContact,
      pose,
      reversed);

    return new RideCarOrdinaryPose(
      traversal,
      circuit,
      baseDistance,
      geometry,
      reversed,
      hasRearGeometry,
      distances,
      frontCursor,
      rearCursor,
      frontContact,
      rearContact,
      pose);
  }

  private static TrackCircuitCursor Locate(
    TrackCircuitTraversal traversal,
    float distance,
    string role,
    RideCarOrdinaryPoseResolverOperations operations
  ) {
    var cursor = operations.Locate(traversal, distance);
    if (!cursor.IsInitialized ||
        !ReferenceEquals(cursor.Traversal, traversal) ||
        !ReferenceEquals(cursor.Circuit, traversal.Circuit))
      throw Invalid($"{role} cursor belongs to a different traversal or circuit");

    var expected = traversal.AtCircuitArcLength(distance);
    if (cursor != expected)
      throw Invalid($"{role} cursor does not retain its exact corrected distance identity");
    return cursor;
  }

  private static TrackCircuitSample Sample(
    TrackCircuitTraversal traversal,
    TrackCircuitCursor cursor,
    float distance,
    string role,
    RideCarOrdinaryPoseResolverOperations operations
  ) {
    var sample = operations.Sample(cursor);
    var expected = cursor.Sample();
    if (!ReferenceEquals(cursor.Traversal, traversal) ||
        !ReferenceEquals(sample.CircuitPiece, expected.CircuitPiece) ||
        !sample.Equals(expected))
      throw Invalid($"{role} sample changed its exact traversal or cursor identity");
    if (!NearlySameArc(sample.CircuitArcLength, distance))
      throw Invalid($"{role} sample changed its corrected circuit distance");
    return sample;
  }

  private static void ValidateDistances(
    RideCarOrdinaryContactDistances? distances,
    float circuitLength,
    bool hasRearGeometry
  ) {
    if (distances == null)
      throw Invalid("contact-distance resolver returned no result");
    if (!float.IsFinite(distances.FrontDistance) ||
        distances.FrontDistance < 0f ||
        distances.FrontDistance >= circuitLength ||
        !float.IsFinite(distances.RearDistance) ||
        distances.RearDistance < 0f ||
        distances.RearDistance >= circuitLength)
      throw Invalid("contact distances are outside the canonical circuit interval");
    if (!hasRearGeometry && distances.FrontDistance != distances.RearDistance)
      throw Invalid("missing rear geometry produced two different contact distances");
  }

  private static void ValidatePose(
    TrackCircuitTraversal traversal,
    TrackCircuitSample frontContact,
    TrackCircuitSample rearContact,
    RideCarStaticPose? pose,
    bool reversed
  ) {
    if (pose == null)
      throw Invalid("static pose builder returned no result");
    if (!ReferenceEquals(pose.Circuit, traversal.Circuit) ||
        !ReferenceEquals(pose.FrontContact.CircuitPiece, frontContact.CircuitPiece) ||
        !ReferenceEquals(pose.RearContact.CircuitPiece, rearContact.CircuitPiece) ||
        !pose.FrontContact.Equals(frontContact) ||
        !pose.RearContact.Equals(rearContact) ||
        pose.Reversed != reversed)
      throw Invalid("static pose changed its exact circuit, contact, or reversal identity");
    if (!TrackMath.IsFinite(pose.ContactMidpoint) ||
        !TrackMath.IsFinite(pose.Forward) ||
        !TrackMath.IsFinite(pose.Right) ||
        !TrackMath.IsFinite(pose.Up) ||
        !TrackMath.IsFinite(pose.Orientation) ||
        !TrackMath.IsFinite(pose.Transform))
      throw Invalid("static pose contains a non-finite vector, orientation, or transform");
  }

  private static bool NearlySameArc(float actual, float expected) {
    if (!float.IsFinite(actual) || !float.IsFinite(expected)) return false;
    var actualBits = Convert.ToInt64(BitConverter.SingleToInt32Bits(actual));
    var expectedBits = Convert.ToInt64(BitConverter.SingleToInt32Bits(expected));
    return Math.Abs(actualBits - expectedBits) <= 2L;
  }

  private static void ValidateOperations(RideCarOrdinaryPoseResolverOperations operations) {
    if (operations.Locate == null || operations.Sample == null)
      throw new ArgumentException(
        "Ordinary ride-car pose operations must be complete.",
        nameof(operations));
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Cannot resolve ordinary ride-car pose: {message}.");
}
