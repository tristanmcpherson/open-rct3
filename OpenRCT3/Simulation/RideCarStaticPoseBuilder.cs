// Ride Car Static Pose Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Simulation.Tracks;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>A finite rigid body pose derived from two exact saved wheel-contact samples.</summary>
internal sealed record RideCarStaticPose(
  TrackCircuit Circuit,
  TrackCircuitSample FrontContact,
  TrackCircuitSample RearContact,
  bool Reversed,
  Vector3 ContactMidpoint,
  Vector3 Forward,
  Vector3 Right,
  Vector3 Up,
  Quaternion Orientation,
  Matrix4x4 Transform
);

/// <summary>Builds a static ride-car body transform from proven circuit-owned contacts.</summary>
/// <remarks>
/// Complete Edition <c>RCT3.exe</c> samples the two wheel contacts in the vehicle path at
/// <c>0x00F62A80</c>. The body forward direction is the rear-to-front contact chord. Paired rail
/// gauges supply roll, then the resulting frame is orthonormalized before use. Native reversal at
/// <c>0x00A562CE</c> and <c>0x00A56507</c> applies pi about native Y-up. After this project's
/// native-to-world axis bridge that negates Forward and Right while preserving Up; it does not move
/// either contact. This builder never advances a cursor, consumes saved distance or speed, infers
/// train spacing, scales the model, or introduces suspension state.
/// </remarks>
internal static class RideCarStaticPoseBuilder {
  private const double UnitTolerance = 0.001d;
  private const double GeometryTolerance = 0.0001d;

  /// <summary>
  /// Creates one rigid local-to-world body transform from exact samples owned by one circuit.
  /// </summary>
  public static RideCarStaticPose Build(
    TrackCircuit circuit,
    TrackCircuitSample frontContact,
    TrackCircuitSample rearContact,
    RideCarLongitudinalGeometry geometry,
    bool reversed
  ) {
    ArgumentNullException.ThrowIfNull(circuit);
    ArgumentNullException.ThrowIfNull(geometry);

    var modelFrame = ValidateGeometry(geometry);
    var frontPose = ValidateOwnedSample(circuit, frontContact, "front");
    var rearPose = ValidateOwnedSample(circuit, rearContact, "rear");

    Vector3 trackForward;
    try {
      trackForward = TrackMath.Direction(rearPose.Center, frontPose.Center);
    } catch (ArgumentException exception) {
      throw Invalid("front and rear contact midpoints define a degenerate chord", exception);
    }

    if (TrackMath.Dot(frontPose.Right, rearPose.Right) <= 0d)
      throw Invalid("front and rear rail gauges point into different hemispheres");
    var meanRight = TrackMath.Midpoint(frontPose.Right, rearPose.Right);
    var projectedRight = meanRight -
      (Convert.ToSingle(TrackMath.Dot(meanRight, trackForward)) * trackForward);
    if (!TrackMath.IsFinite(projectedRight) ||
        TrackMath.Length(projectedRight) <= TrackMath.Epsilon)
      throw Invalid("the paired rail gauges do not define a right axis across the body chord");

    var trackRight = TrackMath.Normalize(projectedRight);
    var trackUp = TrackMath.Normalize(Vector3.Cross(trackForward, trackRight));
    trackRight = TrackMath.Normalize(Vector3.Cross(trackUp, trackForward));
    if (TrackMath.Dot(trackRight, meanRight) <= 0d)
      throw Invalid("orthonormalization reversed the paired rail-gauge direction");

    // Native reversal is an exact pi rotation about local up, not reversed cursor movement.
    var forward = reversed ? -trackForward : trackForward;
    var right = reversed ? -trackRight : trackRight;
    var up = trackUp;
    var worldFrame = Frame(forward, right, up);
    var rotation = Matrix4x4.Transpose(modelFrame) * worldFrame;

    var modelWheelMidpoint = TrackMath.Midpoint(
      geometry.FrontWheelCenterPosition,
      geometry.RearWheelCenterPosition);
    var worldContactMidpoint = TrackMath.Midpoint(frontPose.Center, rearPose.Center);
    var rotatedModelMidpoint = Vector3.TransformNormal(modelWheelMidpoint, rotation);
    if (!TrackMath.IsFinite(rotatedModelMidpoint))
      throw Invalid("rotating the model-space wheel midpoint produced a non-finite value");

    var translation = SubtractFinite(
      worldContactMidpoint,
      rotatedModelMidpoint,
      "body translation");
    var transform = rotation with {
      M41 = translation.X,
      M42 = translation.Y,
      M43 = translation.Z,
    };
    var orientation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotation));
    ValidateResult(
      transform,
      orientation,
      geometry.LongitudinalAxis,
      modelWheelMidpoint,
      worldContactMidpoint,
      forward,
      right,
      up);

    return new(
      circuit,
      frontContact,
      rearContact,
      reversed,
      worldContactMidpoint,
      forward,
      right,
      up,
      orientation,
      transform);
  }

  private static Matrix4x4 ValidateGeometry(RideCarLongitudinalGeometry geometry) {
    if (!TrackMath.IsFinite(geometry.CarFrontPosition) ||
        !TrackMath.IsFinite(geometry.CarRearPosition) ||
        !TrackMath.IsFinite(geometry.FrontWheelCenterPosition) ||
        !TrackMath.IsFinite(geometry.RearWheelCenterPosition) ||
        !TrackMath.IsFinite(geometry.LongitudinalAxis))
      throw Invalid("model-space longitudinal geometry contains a non-finite vector");
    if (!float.IsFinite(geometry.CarLength) || geometry.CarLength <= TrackMath.Epsilon ||
        !float.IsFinite(geometry.FrontWheelCenterLongitudinalPosition) ||
        !float.IsFinite(geometry.RearWheelCenterLongitudinalPosition) ||
        !float.IsFinite(geometry.Wheelbase) || geometry.Wheelbase <= TrackMath.Epsilon ||
        !float.IsFinite(geometry.FrontWheelSpan) ||
        geometry.FrontWheelSpan <= TrackMath.Epsilon ||
        !float.IsFinite(geometry.RearWheelSpan) ||
        geometry.RearWheelSpan <= TrackMath.Epsilon)
      throw Invalid("model-space longitudinal geometry contains an invalid scalar");

    var axisLength = TrackMath.Length(geometry.LongitudinalAxis);
    if (!double.IsFinite(axisLength) || Math.Abs(axisLength - 1d) > UnitTolerance)
      throw Invalid("model-space longitudinal axis is not a unit direction");
    var modelForward = TrackMath.Normalize(geometry.LongitudinalAxis);
    Vector3 authoredForward;
    try {
      authoredForward = TrackMath.Direction(
        geometry.CarRearPosition,
        geometry.CarFrontPosition);
    } catch (ArgumentException exception) {
      throw Invalid("model-space car endpoints define a degenerate axis", exception);
    }
    if (TrackMath.Dot(modelForward, authoredForward) < 1d - UnitTolerance)
      throw Invalid("model-space longitudinal axis disagrees with the car endpoints");
    if (!NearlyEqual(
      TrackMath.Distance(geometry.CarRearPosition, geometry.CarFrontPosition),
      geometry.CarLength))
      throw Invalid("model-space car length disagrees with the car endpoints");

    var frontProjection = TrackMath.Dot(
      geometry.FrontWheelCenterPosition,
      modelForward);
    var rearProjection = TrackMath.Dot(
      geometry.RearWheelCenterPosition,
      modelForward);
    if (!NearlyEqual(frontProjection, geometry.FrontWheelCenterLongitudinalPosition) ||
        !NearlyEqual(rearProjection, geometry.RearWheelCenterLongitudinalPosition) ||
        !NearlyEqual(Math.Abs(frontProjection - rearProjection), geometry.Wheelbase))
      throw Invalid("model-space wheel projections disagree with the decoded wheelbase");

    // Decoded BSH geometry uses repo Z-up, while stock longitudinal markers can run along repo Y.
    // Preserve that authored up reference while aligning the decoded car axis.
    var projectedModelUp = Vector3.UnitZ -
      (Convert.ToSingle(TrackMath.Dot(Vector3.UnitZ, modelForward)) * modelForward);
    if (!TrackMath.IsFinite(projectedModelUp) ||
        TrackMath.Length(projectedModelUp) <= TrackMath.Epsilon)
      throw Invalid("model-space longitudinal geometry does not define a rigid body frame");
    var modelUp = TrackMath.Normalize(projectedModelUp);
    var modelRight = TrackMath.Normalize(Vector3.Cross(modelUp, modelForward));
    modelUp = TrackMath.Normalize(Vector3.Cross(modelForward, modelRight));
    return Frame(modelForward, modelRight, modelUp);
  }

  private static void ValidateSavedPieceIdentity(
    RideCarSavedWheelCursorEntry savedContacts,
    TrackCircuitCursor frontCursor,
    TrackCircuitCursor rearCursor
  ) {
    var car = savedContacts.CarRuntime;
    if (car.TrackPiece == null || car.RearTrackPiece == null ||
        car.TrackPiece.PieceIndex != frontCursor.PieceIndex ||
        car.RearTrackPiece.PieceIndex != rearCursor.PieceIndex ||
        !ReferenceEquals(car.TrackPiece.Piece, frontCursor.CircuitPiece.Piece) ||
        !ReferenceEquals(car.RearTrackPiece.Piece, rearCursor.CircuitPiece.Piece) ||
        car.TrackPiece.SavedTrackPieceEntryId !=
          savedContacts.Front.SavedTrackPieceEntryId ||
        car.RearTrackPiece.SavedTrackPieceEntryId !=
          savedContacts.Rear.SavedTrackPieceEntryId)
      throw Invalid("saved wheel cursors changed their exact cached TrackPiece identities");
  }

  private static TrackContactPose ValidateOwnedSample(
    TrackCircuit circuit,
    TrackCircuitSample sample,
    string role
  ) {
    if (sample.PieceIndex < 0 || sample.PieceIndex >= circuit.Pieces.Count)
      throw Invalid($"{role} sample piece index is outside its circuit");
    var ownedPiece = circuit.Pieces[sample.PieceIndex];
    if (!ReferenceEquals(sample.CircuitPiece, ownedPiece))
      throw Invalid($"{role} sample belongs to a different circuit or cursor owner");
    if (!float.IsFinite(sample.CircuitArcLength) ||
        sample.CircuitArcLength < 0f || sample.CircuitArcLength > circuit.Length ||
        !float.IsFinite(sample.PieceArcLength) || sample.PieceArcLength < 0f ||
        sample.PieceArcLength > ownedPiece.Piece.Length)
      throw Invalid($"{role} sample contains an invalid circuit or piece arc length");

    TrackContactPose pose;
    try {
      pose = TrackContactPoseAdapter.Create(sample.ContactPoints);
    } catch (InvalidDataException exception) {
      throw Invalid($"{role} sample has malformed wheel contacts", exception);
    }
    var expectedContacts = ownedPiece.Piece.SampleContactPoints(sample.PieceArcLength);
    if (!sample.ContactPoints.Equals(expectedContacts))
      throw Invalid($"{role} sample does not match its exact circuit-piece geometry");

    var expectedCircuitArc = ExpectedCircuitArcLength(circuit, sample);
    if (!NearlySameArc(sample.CircuitArcLength, expectedCircuitArc))
      throw Invalid($"{role} sample circuit arc does not match its piece-local identity");
    return pose;
  }

  private static float ExpectedCircuitArcLength(
    TrackCircuit circuit,
    TrackCircuitSample sample
  ) {
    var total = 0d;
    foreach (var index in Enumerable.Range(0, sample.PieceIndex))
      total += circuit.Pieces[index].Piece.Length;
    total += sample.PieceArcLength;
    if (!double.IsFinite(total) || total < 0d || total > float.MaxValue)
      throw Invalid("sample circuit arc cannot be represented as a finite value");
    return Math.Clamp(Convert.ToSingle(total), 0f, circuit.Length);
  }

  private static Matrix4x4 Frame(Vector3 forward, Vector3 right, Vector3 up) => new(
    forward.X, forward.Y, forward.Z, 0f,
    right.X, right.Y, right.Z, 0f,
    up.X, up.Y, up.Z, 0f,
    0f, 0f, 0f, 1f);

  private static Vector3 SubtractFinite(
    Vector3 left,
    Vector3 right,
    string description
  ) => new(
    ToFiniteSingle(Convert.ToDouble(left.X) - right.X, description),
    ToFiniteSingle(Convert.ToDouble(left.Y) - right.Y, description),
    ToFiniteSingle(Convert.ToDouble(left.Z) - right.Z, description));

  private static float ToFiniteSingle(double value, string description) {
    if (!double.IsFinite(value) || value > float.MaxValue || value < float.MinValue)
      throw Invalid($"{description} is outside the finite single-precision range");
    return Convert.ToSingle(value);
  }

  private static void ValidateResult(
    Matrix4x4 transform,
    Quaternion orientation,
    Vector3 modelForward,
    Vector3 modelWheelMidpoint,
    Vector3 worldContactMidpoint,
    Vector3 forward,
    Vector3 right,
    Vector3 up
  ) {
    if (!TrackMath.IsFinite(transform) || !TrackMath.IsFinite(orientation))
      throw Invalid("derived body orientation or transform is non-finite");
    if (Math.Abs(Convert.ToDouble(transform.GetDeterminant()) - 1d) > UnitTolerance)
      throw Invalid("derived body transform is not a rigid proper rotation");
    if (TrackMath.Dot(Vector3.TransformNormal(modelForward, transform), forward) <
        1d - UnitTolerance)
      throw Invalid("derived body transform does not align the model longitudinal axis");
    if (TrackMath.Dot(Vector3.Cross(forward, right), up) < 1d - UnitTolerance)
      throw Invalid("derived body frame is not right-handed");
    var mappedMidpoint = Vector3.Transform(modelWheelMidpoint, transform);
    if (!TrackMath.IsFinite(mappedMidpoint) ||
        TrackMath.Distance(mappedMidpoint, worldContactMidpoint) > GeometryTolerance)
      throw Invalid("derived body transform does not preserve the wheel-contact midpoint");
  }

  private static bool NearlyEqual(double actual, double expected) {
    if (!double.IsFinite(actual) || !double.IsFinite(expected)) return false;
    var scale = Math.Max(1d, Math.Max(Math.Abs(actual), Math.Abs(expected)));
    return Math.Abs(actual - expected) <= GeometryTolerance * scale;
  }

  private static bool NearlySameArc(float actual, float expected) {
    if (!float.IsFinite(actual) || !float.IsFinite(expected) || actual < 0f || expected < 0f)
      return false;
    var actualBits = Convert.ToInt64(BitConverter.SingleToInt32Bits(actual));
    var expectedBits = Convert.ToInt64(BitConverter.SingleToInt32Bits(expected));
    return Math.Abs(actualBits - expectedBits) <= 2L;
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-car static pose input is malformed or unsupported: {message}.");

  private static InvalidDataException Invalid(string message, Exception innerException) =>
    new($"Ride-car static pose input is malformed or unsupported: {message}.", innerException);

  /// <summary>Builds directly from one fully resolved saved wheel-cursor registry entry.</summary>
  public static RideCarStaticPose Build(
    RideCarSavedWheelCursorEntry savedContacts,
    RideCarLongitudinalGeometry geometry
  ) {
    ArgumentNullException.ThrowIfNull(savedContacts);
    ArgumentNullException.ThrowIfNull(geometry);
    if (!savedContacts.IsResolved || savedContacts.CarRuntime == null)
      throw Invalid("saved wheel-cursor entry is unresolved or incomplete");
    if (savedContacts.Front.Cursor is not { } frontCursor ||
        savedContacts.Front.Sample is not { } frontSample ||
        savedContacts.Rear.Cursor is not { } rearCursor ||
        savedContacts.Rear.Sample is not { } rearSample)
      throw Invalid("resolved saved wheel-cursor entry has missing cursor or sample evidence");
    if (!frontCursor.IsInitialized || !rearCursor.IsInitialized ||
        !ReferenceEquals(frontCursor.Traversal, rearCursor.Traversal) ||
        !ReferenceEquals(frontCursor.Circuit, rearCursor.Circuit))
      throw Invalid("saved front and rear cursors have inconsistent traversal ownership");
    if (!frontCursor.Sample().Equals(frontSample) || !rearCursor.Sample().Equals(rearSample))
      throw Invalid("saved wheel-cursor samples changed their exact cursor identity");
    ValidateSavedPieceIdentity(savedContacts, frontCursor, rearCursor);

    return Build(
      frontCursor.Circuit,
      frontSample,
      rearSample,
      geometry,
      savedContacts.RequiresPiContactFrameRotation);
  }

  /// <summary>Builds from two exact cursors owned by one traversal.</summary>
  public static RideCarStaticPose Build(
    TrackCircuitCursor frontCursor,
    TrackCircuitCursor rearCursor,
    RideCarLongitudinalGeometry geometry,
    bool reversed
  ) {
    ArgumentNullException.ThrowIfNull(geometry);
    if (!frontCursor.IsInitialized || !rearCursor.IsInitialized ||
        !ReferenceEquals(frontCursor.Traversal, rearCursor.Traversal) ||
        !ReferenceEquals(frontCursor.Circuit, rearCursor.Circuit))
      throw Invalid("front and rear cursors have inconsistent traversal ownership");
    return Build(
      frontCursor.Circuit,
      frontCursor.Sample(),
      rearCursor.Sample(),
      geometry,
      reversed);
  }
}
