// Track Section Geometry Adapter
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation.Tracks;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>
/// Converts one resolved TKS car-spline pair into exact hand-authored runtime rail geometry.
/// </summary>
/// <remarks>
/// RCT3 SPL control point 1 is relative to its node and points toward the previous node, while
/// control point 2 points toward the next node. Each serialized cubic segment has its own local
/// parameter. Mapping those equal local intervals onto a shared piece parameter preserves both
/// rails' paired node boundaries without treating the stored approximate arc lengths as authoring
/// parameters. For a normalized segment interval <c>dt</c>, the
/// equivalent Hermite derivatives are therefore <c>-3 * cp1 / dt</c> at the end and
/// <c>3 * cp2 / dt</c> at the start. Only pairs that fit the runtime's shared control-point model
/// without resampling or tangent averaging are accepted. Decoded SPL vectors use RCT3's
/// left-handed, Y-up coordinates, so positions and relative control offsets cross the same
/// <c>(X, Y, Z) -&gt; (X, Z, Y)</c> native-to-park bridge as static-shape geometry before runtime
/// controls are built.
///
/// That bridge changes handedness. The pinned serializer transforms SPL vectors but assigns TKS
/// left and right references without relabeling them. No pinned runtime reference or aligned
/// TKS/SID fixture proves that the native-to-park bridge swaps those semantic roles, so this exact
/// subset retains the serialized left/right identities. A future alignment proof must resolve that
/// boundary before any role swap is introduced.
///
/// RCT3's native reversed-piece sampler maps distance from the opposite endpoint, swaps the
/// evaluated left/right rail outputs, and negates both derivatives. Reversed geometry therefore
/// applies <c>left(u) = right(1-u)</c> and <c>right(u) = left(1-u)</c> without changing placement.
/// </remarks>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/include/spline.h">
/// rct3-importer spline control-point layout
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerSPL.cpp">
/// rct3-importer SPL length calculation
/// </seealso>
public static class TrackSectionGeometryAdapter {
  private const int MaximumSplineLinkCount = 1_000_000;
  private const int MaximumNodeCount = 1_000_000;
  private const int TravelDataSize = 14;
  private const float MaximumSerializedLengthRelativeError = 0.00001f;

  /// <summary>Creates geometry from a resolved track section's authoritative car rails.</summary>
  public static TrackPieceGeometry CreateCarGeometry(
    TrackSectionResourceLink section,
    bool reversed = false
  ) {
    ArgumentNullException.ThrowIfNull(section);
    if (section.Source is null || section.Source.File is null || section.Source.Resource is null)
      throw Invalid("track-section source is incomplete");
    if (section.Source.File.Type != FileType.TrackSection)
      throw Invalid(
        $"source '{section.Source.File.Name}' has type " +
        $"'{section.Source.File.Type.ToTagString()}' instead of 'tks'");
    if (!string.Equals(
      section.Source.File.Name,
      section.Source.Resource.Name,
      StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"source file name '{section.Source.File.Name}' does not match decoded TKS name " +
        $"'{section.Source.Resource.Name}'");
    if (section.Splines is null)
      throw Invalid($"TKS '{section.Source.Resource.Name}' has a null spline-link list");
    if (section.Splines.Count > MaximumSplineLinkCount)
      throw Invalid(
        $"TKS '{section.Source.Resource.Name}' spline-link count exceeds the limit " +
        MaximumSplineLinkCount);
    if (section.Source.Resource.CarSplines is null)
      throw Invalid($"TKS '{section.Source.Resource.Name}' has a null car-spline pair");

    var left = FindCarSpline(
      section,
      TrackSectionSplineRole.CarLeft,
      section.Source.Resource.CarSplines.Left);
    var right = FindCarSpline(
      section,
      TrackSectionSplineRole.CarRight,
      section.Source.Resource.CarSplines.Right);
    return CreateCarGeometry(left.Resource, right.Resource, reversed);
  }

  internal static TrackPieceGeometry CreateCarGeometry(
    Spline left,
    Spline right,
    bool reversed = false
  ) {
    ArgumentNullException.ThrowIfNull(left);
    ArgumentNullException.ThrowIfNull(right);
    ValidateSpline(left, "left");
    ValidateSpline(right, "right");
    if (left.Nodes.Count != right.Nodes.Count)
      throw Invalid(
        $"car rails have different node counts ({left.Nodes.Count} and {right.Nodes.Count})");

    var parameters = CreateSharedParameters(left.Nodes.Count);
    ValidateInteriorDerivatives(left, parameters, "left");
    ValidateInteriorDerivatives(right, parameters, "right");

    var controlPoints = new RailControlPair[parameters.Length];
    foreach (var index in Enumerable.Range(0, parameters.Length)) {
      controlPoints[index] = new RailControlPair(
        parameters[index],
        ToOpenRct3Coordinates(left.Nodes[index].Position),
        GetTangent(left, parameters, index, "left"),
        ToOpenRct3Coordinates(right.Nodes[index].Position),
        GetTangent(right, parameters, index, "right"),
        0f);
    }
    if (reversed) controlPoints = Reverse(controlPoints);

    try {
      return TrackPieceGeometry.FromHandAuthored(controlPoints);
    } catch (ArgumentException exception) {
      throw new InvalidDataException(
        "Track-section geometry is unsupported by the exact paired-rail model.",
        exception);
    }
  }

  private static RailControlPair[] Reverse(IReadOnlyList<RailControlPair> source) {
    var reversed = new RailControlPair[source.Count];
    foreach (var index in Enumerable.Range(0, source.Count)) {
      var point = source[source.Count - index - 1];
      reversed[index] = new RailControlPair(
        1f - point.Parameter,
        point.RightPosition,
        -point.RightTangent,
        point.LeftPosition,
        -point.LeftTangent,
        0f);
    }
    return reversed;
  }

  private static SplineResourceSource FindCarSpline(
    TrackSectionResourceLink section,
    TrackSectionSplineRole role,
    string expectedReference
  ) {
    TrackSectionSplineLink? resolved = null;
    foreach (var link in section.Splines) {
      if (link is null)
        throw Invalid($"TKS '{section.Source.Resource.Name}' has a null spline link");
      if (link.Role != role) continue;
      if (link.Index != null)
        throw Invalid($"TKS '{section.Source.Resource.Name}' {role} link has an index");
      if (resolved != null)
        throw Invalid($"TKS '{section.Source.Resource.Name}' has duplicate {role} links");
      resolved = link;
    }

    if (resolved is null)
      throw Invalid($"TKS '{section.Source.Resource.Name}' has no {role} link");
    if (!string.Equals(resolved.Reference, expectedReference, StringComparison.Ordinal))
      throw Invalid(
        $"TKS '{section.Source.Resource.Name}' {role} link '{resolved.Reference}' does not " +
        $"match serialized reference '{expectedReference}'");
    if (resolved.Source is null)
      throw Invalid($"TKS '{section.Source.Resource.Name}' {role} link is unresolved");
    if (resolved.Source.File is null || resolved.Source.Resource is null)
      throw Invalid($"TKS '{section.Source.Resource.Name}' {role} source is incomplete");
    if (resolved.Source.File.Type != FileType.Spline)
      throw Invalid(
        $"TKS '{section.Source.Resource.Name}' {role} source has type " +
        $"'{resolved.Source.File.Type.ToTagString()}' instead of 'spl'");
    if (!string.Equals(
      resolved.Source.File.Name,
      resolved.Source.Resource.Name,
      StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"TKS '{section.Source.Resource.Name}' {role} source file name " +
        $"'{resolved.Source.File.Name}' does not match decoded SPL name " +
        $"'{resolved.Source.Resource.Name}'");
    return resolved.Source;
  }

  private static void ValidateSpline(Spline spline, string side) {
    if (string.IsNullOrWhiteSpace(spline.Name))
      throw Invalid($"{side} car spline has an empty name");
    if (spline.Cyclic)
      throw Invalid($"{side} car spline '{spline.Name}' is cyclic");
    if (spline.Nodes is null || spline.Segments is null)
      throw Invalid($"{side} car spline '{spline.Name}' has a null node or segment list");
    if (spline.Nodes.Count is < 2 or > MaximumNodeCount)
      throw Invalid(
        $"{side} car spline '{spline.Name}' node count {spline.Nodes.Count} is outside " +
        $"the supported range 2 through {MaximumNodeCount}");
    if (spline.Segments.Count != spline.Nodes.Count - 1)
      throw Invalid(
        $"{side} car spline '{spline.Name}' has {spline.Segments.Count} segments for " +
        $"{spline.Nodes.Count} open-spline nodes");
    if (!float.IsFinite(spline.TotalLength) || spline.TotalLength <= 0f)
      throw Invalid($"{side} car spline '{spline.Name}' total length is not finite and positive");
    var expectedInverseLength = 1f / spline.TotalLength;
    if (!float.IsFinite(spline.InverseTotalLength)
      || spline.InverseTotalLength <= 0f
      || !ApproximatelyEqual(spline.InverseTotalLength, expectedInverseLength))
      throw Invalid(
        $"{side} car spline '{spline.Name}' inverse total length is inconsistent");

    var accumulatedLength = 0f;
    foreach (var index in Enumerable.Range(0, spline.Nodes.Count)) {
      var node = spline.Nodes[index];
      if (node is null)
        throw Invalid($"{side} car spline '{spline.Name}' node {index} is null");
      if (!IsFinite(node.Position)
        || !IsFinite(node.PreviousControlOffset)
        || !IsFinite(node.NextControlOffset))
        throw Invalid(
          $"{side} car spline '{spline.Name}' node {index} contains non-finite data");
    }
    foreach (var index in Enumerable.Range(0, spline.Segments.Count)) {
      var segment = spline.Segments[index];
      if (segment is null)
        throw Invalid($"{side} car spline '{spline.Name}' segment {index} is null");
      if (!float.IsFinite(segment.Length) || segment.Length <= 0f)
        throw Invalid(
          $"{side} car spline '{spline.Name}' segment {index} length is not finite and positive");
      if (segment.TravelData is null || segment.TravelData.Count != TravelDataSize)
        throw Invalid(
          $"{side} car spline '{spline.Name}' segment {index} does not have exactly " +
          $"{TravelDataSize} travel bytes");
      accumulatedLength += segment.Length;
      if (!float.IsFinite(accumulatedLength))
        throw Invalid($"{side} car spline '{spline.Name}' accumulated length overflowed");
    }
    if (!ApproximatelyEqual(accumulatedLength, spline.TotalLength))
      throw Invalid(
        $"{side} car spline '{spline.Name}' segment lengths do not exactly sum to its total");
  }

  private static float[] CreateSharedParameters(int nodeCount) {
    var parameters = new float[nodeCount];
    foreach (var index in Enumerable.Range(0, nodeCount))
      parameters[index] = Convert.ToSingle(index) / Convert.ToSingle(nodeCount - 1);
    return parameters;
  }

  private static void ValidateInteriorDerivatives(
    Spline spline,
    IReadOnlyList<float> parameters,
    string side
  ) {
    foreach (var index in Enumerable.Range(1, spline.Nodes.Count - 2)) {
      var incoming = GetIncomingTangent(spline, parameters, index, side);
      var outgoing = GetOutgoingTangent(spline, parameters, index, side);
      if (!ApproximatelyEqual(incoming, outgoing))
        throw Invalid(
          $"{side} car spline '{spline.Name}' node {index} has incompatible incoming " +
          "and outgoing derivatives");
    }
  }

  private static Vector3 GetTangent(
    Spline spline,
    IReadOnlyList<float> parameters,
    int index,
    string side
  ) {
    if (index == 0) return GetOutgoingTangent(spline, parameters, index, side);
    return GetIncomingTangent(spline, parameters, index, side);
  }

  private static Vector3 GetIncomingTangent(
    Spline spline,
    IReadOnlyList<float> parameters,
    int index,
    string side
  ) => ScaleControlOffset(
    spline.Nodes[index].PreviousControlOffset,
    parameters[index] - parameters[index - 1],
    -3f,
    spline,
    index,
    side);

  private static Vector3 GetOutgoingTangent(
    Spline spline,
    IReadOnlyList<float> parameters,
    int index,
    string side
  ) => ScaleControlOffset(
    spline.Nodes[index].NextControlOffset,
    parameters[index + 1] - parameters[index],
    3f,
    spline,
    index,
    side);

  private static Vector3 ScaleControlOffset(
    Vector3 offset,
    float parameterRange,
    float scale,
    Spline spline,
    int index,
    string side
  ) {
    var transformedOffset = ToOpenRct3Coordinates(offset);
    var tangent = (transformedOffset * scale) / parameterRange;
    if (!IsFinite(tangent))
      throw Invalid(
        $"{side} car spline '{spline.Name}' node {index} derivative is non-finite");
    return tangent;
  }

  private static Vector3 ToOpenRct3Coordinates(Vector3 value) =>
    new(value.X, value.Z, value.Y);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static bool ApproximatelyEqual(float actual, float expected) {
    var scale = MathF.Max(MathF.Abs(actual), MathF.Abs(expected));
    return MathF.Abs(actual - expected) <= scale * MaximumSerializedLengthRelativeError;
  }

  private static bool ApproximatelyEqual(Vector3 actual, Vector3 expected) {
    var scale = Math.Max(TrackMath.Length(actual), TrackMath.Length(expected));
    return TrackMath.Distance(actual, expected) <=
      scale * MaximumSerializedLengthRelativeError;
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Track-section geometry is malformed or unsupported: {message}.");
}
