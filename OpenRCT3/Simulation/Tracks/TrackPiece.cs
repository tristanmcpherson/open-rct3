// Track Piece
//
// Authors:
//   - OpenRCT3 Contributors
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation.Tracks;

/// <summary>
/// One independently addressable dual-rail track piece with analytic Hermite curves and a shared,
/// midpoint-derived local arc-length bake.
/// </summary>
public sealed class TrackPiece {
  private readonly HermiteRailSpline leftSpline;
  private readonly HermiteRailSpline rightSpline;
  private readonly float[] arcLengths;
  private readonly BakedRailPoint[] leftBake;
  private readonly BakedRailPoint[] rightBake;
  private readonly ReadOnlyCollection<float> bakedArcLengths;

  public TrackPieceGeometry Geometry { get; }
  public Matrix4x4 Placement { get; }
  public TrackBakeSettings BakeSettings { get; }
  public float Length => arcLengths[^1];
  public int BakedSampleCount => arcLengths.Length;
  public IReadOnlyList<float> BakedArcLengths => bakedArcLengths;
  public TrackPieceEndpoint Entry { get; }
  public TrackPieceEndpoint Exit { get; }

  public TrackPiece(TrackPieceGeometry geometry)
    : this(geometry, Matrix4x4.Identity, TrackBakeSettings.Default) { }

  public TrackPiece(TrackPieceGeometry geometry, Matrix4x4 placement)
    : this(geometry, placement, TrackBakeSettings.Default) { }

  public TrackPiece(
    TrackPieceGeometry geometry,
    Matrix4x4 placement,
    TrackBakeSettings bakeSettings
  ) {
    ArgumentNullException.ThrowIfNull(geometry);
    ArgumentNullException.ThrowIfNull(bakeSettings);
    ValidatePlacement(placement);
    bakeSettings.Validate();

    Geometry = geometry;
    Placement = placement;
    BakeSettings = bakeSettings;

    var transformed = geometry.ControlPoints.Select(point => Transform(point, placement)).ToArray();
    leftSpline = new(transformed.Select(point => new RailSplinePoint(
      point.Parameter,
      point.LeftPosition,
      point.LeftTangent,
      point.BankRadians
    )));
    rightSpline = new(transformed.Select(point => new RailSplinePoint(
      point.Parameter,
      point.RightPosition,
      point.RightTangent,
      point.BankRadians
    )));

    Entry = GetEndpoint(0f);
    Exit = GetEndpoint(1f);

    var bake = Bake(transformed);
    arcLengths = bake.ArcLengths;
    leftBake = bake.Left;
    rightBake = bake.Right;
    bakedArcLengths = Array.AsReadOnly(arcLengths);
  }

  /// <summary>Evaluates the retained analytic spline without using the runtime bake.</summary>
  public RailEvaluation EvaluateRail(RailSide side, float parameter) {
    if (!float.IsFinite(parameter) || parameter is < 0f or > 1f)
      throw new ArgumentOutOfRangeException(nameof(parameter));

    var evaluation = GetSpline(side).Evaluate(parameter);
    return new(
      parameter,
      evaluation.Position,
      Vector3.Normalize(evaluation.Derivative),
      evaluation.BankRadians
    );
  }

  /// <summary>Samples one rail from the adaptive bake at a piece-local arc length.</summary>
  public RailSample SampleRail(RailSide side, float arcLength) {
    if (!float.IsFinite(arcLength) || arcLength < 0f || arcLength > Length)
      throw new ArgumentOutOfRangeException(nameof(arcLength));

    var bake = side switch {
      RailSide.Left => leftBake,
      RailSide.Right => rightBake,
      _ => throw new ArgumentOutOfRangeException(nameof(side)),
    };

    var index = Array.BinarySearch(arcLengths, arcLength);
    if (index >= 0) return bake[index].ToSample(arcLength);

    var upper = ~index;
    var lower = upper - 1;
    var intervalLength = arcLengths[upper] - arcLengths[lower];
    var amount = (arcLength - arcLengths[lower]) / intervalLength;
    return Interpolate(bake[lower], bake[upper], arcLength, intervalLength, amount);
  }

  /// <summary>Samples both wheel-contact rails at one shared piece-local arc length.</summary>
  public TrackContactPoints SampleContactPoints(float arcLength)
    => new(arcLength, SampleRail(RailSide.Left, arcLength), SampleRail(RailSide.Right, arcLength));

  private BakeResult Bake(IReadOnlyList<RailControlPair> controlPoints) {
    var averageGauge = controlPoints.Average(point =>
      Vector3.Distance(point.LeftPosition, point.RightPosition));
    var chordTolerance = MathF.Max(
      BakeSettings.MinimumChordTolerance,
      averageGauge * BakeSettings.ChordToleranceGaugeFraction
    );

    var parameters = new List<float> { 0f };
    for (var index = 0; index < controlPoints.Count - 1; index++)
      Subdivide(
        controlPoints[index].Parameter,
        controlPoints[index + 1].Parameter,
        depth: 0,
        chordTolerance,
        parameters
      );

    var arcs = new float[parameters.Count];
    var left = new BakedRailPoint[parameters.Count];
    var right = new BakedRailPoint[parameters.Count];
    var evaluations = parameters.Select(EvaluatePair).ToArray();

    for (var index = 1; index < evaluations.Length; index++) {
      var previousMidpoint = evaluations[index - 1].Midpoint;
      var midpoint = evaluations[index].Midpoint;
      arcs[index] = arcs[index - 1] + Vector3.Distance(previousMidpoint, midpoint);
    }
    if (arcs[^1] <= TrackMath.Epsilon)
      throw new ArgumentException("A track piece must have non-zero centerline arc length.", nameof(Geometry));

    for (var index = 0; index < evaluations.Length; index++) {
      var pair = evaluations[index];
      var centerDerivative = (pair.Left.Derivative + pair.Right.Derivative) * 0.5f;
      var arcDerivative = centerDerivative.Length();
      if (arcDerivative <= TrackMath.Epsilon)
        throw new ArgumentException("A track piece cannot contain a stationary centerline tangent.", nameof(Geometry));

      left[index] = BakedRailPoint.Create(
        pair.Left,
        pair.Right.Position - pair.Left.Position,
        arcDerivative
      );
      right[index] = BakedRailPoint.Create(
        pair.Right,
        pair.Right.Position - pair.Left.Position,
        arcDerivative
      );
    }

    return new(arcs, left, right);
  }

  private void Subdivide(
    float startParameter,
    float endParameter,
    int depth,
    float chordTolerance,
    ICollection<float> output
  ) {
    var midpointParameter = (startParameter + endParameter) * 0.5f;
    var firstQuarterParameter = (startParameter + midpointParameter) * 0.5f;
    var thirdQuarterParameter = (midpointParameter + endParameter) * 0.5f;
    var start = EvaluatePair(startParameter);
    var firstQuarter = EvaluatePair(firstQuarterParameter);
    var midpoint = EvaluatePair(midpointParameter);
    var thirdQuarter = EvaluatePair(thirdQuarterParameter);
    var end = EvaluatePair(endParameter);
    ValidateRegularity([start, firstQuarter, midpoint, thirdQuarter, end]);

    var leftDeviation = MaximumChordDeviation(
      start.Left.Position,
      firstQuarter.Left.Position,
      midpoint.Left.Position,
      thirdQuarter.Left.Position,
      end.Left.Position
    );
    var rightDeviation = MaximumChordDeviation(
      start.Right.Position,
      firstQuarter.Right.Position,
      midpoint.Right.Position,
      thirdQuarter.Right.Position,
      end.Right.Position
    );
    var bankChange = MathF.Abs(end.Left.BankRadians - start.Left.BankRadians);
    var needsSubdivision = MathF.Max(leftDeviation, rightDeviation) > chordTolerance
      || bankChange > BakeSettings.MaximumBankAngleChangeRadians;

    if (needsSubdivision && depth < BakeSettings.MaximumSubdivisionDepth) {
      Subdivide(startParameter, midpointParameter, depth + 1, chordTolerance, output);
      Subdivide(midpointParameter, endParameter, depth + 1, chordTolerance, output);
      return;
    }
    if (needsSubdivision)
      throw new InvalidOperationException(
        "Adaptive track bake could not meet its tolerances within the subdivision limit."
      );

    output.Add(endParameter);
  }

  private static float MaximumChordDeviation(
    Vector3 start,
    Vector3 firstQuarter,
    Vector3 midpoint,
    Vector3 thirdQuarter,
    Vector3 end
  )
    => MathF.Max(
      Vector3.Distance(firstQuarter, Vector3.Lerp(start, end, 0.25f)),
      MathF.Max(
        Vector3.Distance(midpoint, Vector3.Lerp(start, end, 0.5f)),
        Vector3.Distance(thirdQuarter, Vector3.Lerp(start, end, 0.75f))
      )
    );

  private static void ValidateRegularity(IReadOnlyList<RailPairEvaluation> evaluations) {
    var derivativeScale = evaluations.Max(pair => MathF.Max(
      pair.Left.Derivative.Length(),
      pair.Right.Derivative.Length()
    ));
    var minimumDerivative = MathF.Max(TrackMath.Epsilon, derivativeScale * 0.00001f);

    foreach (var pair in evaluations) {
      var centerDerivative = (pair.Left.Derivative + pair.Right.Derivative) * 0.5f;
      if (pair.Left.Derivative.Length() <= minimumDerivative
          || pair.Right.Derivative.Length() <= minimumDerivative
          || centerDerivative.Length() <= minimumDerivative)
        throw new ArgumentException(
          "A track piece cannot contain a stationary or near-stationary spline tangent."
        );
    }
  }

  private RailPairEvaluation EvaluatePair(float parameter)
    => new(leftSpline.Evaluate(parameter), rightSpline.Evaluate(parameter));

  private TrackPieceEndpoint GetEndpoint(float parameter) {
    var pair = EvaluatePair(parameter);
    return new(
      new(pair.Left.Position, pair.Left.Derivative, pair.Left.BankRadians),
      new(pair.Right.Position, pair.Right.Derivative, pair.Right.BankRadians)
    );
  }

  private HermiteRailSpline GetSpline(RailSide side) => side switch {
    RailSide.Left => leftSpline,
    RailSide.Right => rightSpline,
    _ => throw new ArgumentOutOfRangeException(nameof(side)),
  };

  private static RailSample Interpolate(
    BakedRailPoint start,
    BakedRailPoint end,
    float arcLength,
    float intervalLength,
    float amount
  ) {
    var amountSquared = amount * amount;
    var amountCubed = amountSquared * amount;
    var h00 = (2f * amountCubed) - (3f * amountSquared) + 1f;
    var h10 = amountCubed - (2f * amountSquared) + amount;
    var h01 = (-2f * amountCubed) + (3f * amountSquared);
    var h11 = amountCubed - amountSquared;
    var startTangent = start.DerivativePerArc * intervalLength;
    var endTangent = end.DerivativePerArc * intervalLength;
    var position = (h00 * start.Position) + (h10 * startTangent)
      + (h01 * end.Position) + (h11 * endTangent);

    var derivative = ((6f * amountSquared) - (6f * amount)) * start.Position
      + ((3f * amountSquared) - (4f * amount) + 1f) * startTangent
      + ((-6f * amountSquared) + (6f * amount)) * end.Position
      + ((3f * amountSquared) - (2f * amount)) * endTangent;
    var tangentSource = derivative;
    if (tangentSource.LengthSquared() <= TrackMath.Epsilon)
      tangentSource = Vector3.Lerp(start.Tangent, end.Tangent, amount);
    if (tangentSource.LengthSquared() <= TrackMath.Epsilon)
      tangentSource = amount < 0.5f ? start.Tangent : end.Tangent;
    var tangent = Vector3.Normalize(tangentSource);
    var lateral = InterpolateLateral(start, end, tangent, amount);

    return new(
      arcLength,
      position,
      tangent,
      CreateOrientation(tangent, lateral),
      TrackMath.LerpUnwrapped(start.BankRadians, end.BankRadians, amount)
    );
  }

  private static Vector3 InterpolateLateral(
    BakedRailPoint start,
    BakedRailPoint end,
    Vector3 tangent,
    float amount
  ) {
    var lateral = Vector3.Lerp(start.Lateral, end.Lateral, amount);
    lateral -= Vector3.Dot(lateral, tangent) * tangent;
    if (lateral.LengthSquared() > TrackMath.Epsilon) return Vector3.Normalize(lateral);

    var source = amount < 0.5f ? start : end;
    var axis = Vector3.Cross(source.Tangent, tangent);
    if (axis.LengthSquared() <= TrackMath.Epsilon) {
      lateral = source.Lateral - (Vector3.Dot(source.Lateral, tangent) * tangent);
      return Vector3.Normalize(lateral);
    }

    axis = Vector3.Normalize(axis);
    var angle = MathF.Acos(Math.Clamp(Vector3.Dot(source.Tangent, tangent), -1f, 1f));
    lateral = Vector3.Transform(
      source.Lateral,
      Quaternion.CreateFromAxisAngle(axis, angle)
    );
    lateral -= Vector3.Dot(lateral, tangent) * tangent;
    return Vector3.Normalize(lateral);
  }

  private static Quaternion CreateOrientation(Vector3 tangent, Vector3 lateral) {
    var up = Vector3.Normalize(Vector3.Cross(tangent, lateral));
    var orientationMatrix = new Matrix4x4(
      tangent.X, tangent.Y, tangent.Z, 0f,
      lateral.X, lateral.Y, lateral.Z, 0f,
      up.X, up.Y, up.Z, 0f,
      0f, 0f, 0f, 1f
    );
    return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(orientationMatrix));
  }

  private static RailControlPair Transform(RailControlPair point, Matrix4x4 placement)
    => point with {
      LeftPosition = Vector3.Transform(point.LeftPosition, placement),
      LeftTangent = Vector3.TransformNormal(point.LeftTangent, placement),
      RightPosition = Vector3.Transform(point.RightPosition, placement),
      RightTangent = Vector3.TransformNormal(point.RightTangent, placement),
    };

  private static void ValidatePlacement(Matrix4x4 placement) {
    if (!TrackMath.IsFinite(placement)
        || placement.M14 != 0f
        || placement.M24 != 0f
        || placement.M34 != 0f
        || placement.M44 != 1f
        || MathF.Abs(placement.GetDeterminant()) <= TrackMath.Epsilon)
      throw new ArgumentException("Track placement must be a finite, invertible affine transform.", nameof(placement));
  }

  private readonly record struct BakeResult(
    float[] ArcLengths,
    BakedRailPoint[] Left,
    BakedRailPoint[] Right
  );

  private readonly record struct RailPairEvaluation(
    RailCurveEvaluation Left,
    RailCurveEvaluation Right
  ) {
    public Vector3 Midpoint => (Left.Position + Right.Position) * 0.5f;
  }

  private readonly record struct BakedRailPoint(
    Vector3 Position,
    Vector3 Tangent,
    Vector3 DerivativePerArc,
    Vector3 Lateral,
    float BankRadians
  ) {
    public static BakedRailPoint Create(
      RailCurveEvaluation rail,
      Vector3 gauge,
      float centerArcDerivative
    ) {
      var tangent = Vector3.Normalize(rail.Derivative);
      var right = gauge - (Vector3.Dot(gauge, tangent) * tangent);
      if (right.LengthSquared() <= TrackMath.Epsilon)
        throw new ArgumentException("The rail gauge cannot be parallel to its tangent.");
      right = Vector3.Normalize(right);

      var bankRotation = Quaternion.CreateFromAxisAngle(tangent, rail.BankRadians);
      right = Vector3.Normalize(Vector3.Transform(right, bankRotation));

      return new(
        rail.Position,
        tangent,
        rail.Derivative / centerArcDerivative,
        right,
        rail.BankRadians
      );
    }

    public RailSample ToSample(float arcLength)
      => new(arcLength, Position, Tangent, CreateOrientation(Tangent, Lateral), BankRadians);
  }
}

internal readonly record struct RailSplinePoint(
  float Parameter,
  Vector3 Position,
  Vector3 Tangent,
  float BankRadians
);

internal readonly record struct RailCurveEvaluation(
  Vector3 Position,
  Vector3 Derivative,
  float BankRadians
);

internal sealed class HermiteRailSpline {
  private readonly RailSplinePoint[] points;
  private readonly float[] parameters;

  public HermiteRailSpline(IEnumerable<RailSplinePoint> points) {
    this.points = points.ToArray();
    parameters = this.points.Select(point => point.Parameter).ToArray();
  }

  public RailCurveEvaluation Evaluate(float parameter) {
    var exactIndex = Array.BinarySearch(parameters, parameter);
    var lowerIndex = exactIndex >= 0 ? exactIndex : (~exactIndex) - 1;
    if (lowerIndex >= points.Length - 1) lowerIndex = points.Length - 2;
    if (lowerIndex < 0) lowerIndex = 0;

    var start = points[lowerIndex];
    var end = points[lowerIndex + 1];
    var parameterRange = end.Parameter - start.Parameter;
    var amount = (parameter - start.Parameter) / parameterRange;
    var amountSquared = amount * amount;
    var amountCubed = amountSquared * amount;
    var h00 = (2f * amountCubed) - (3f * amountSquared) + 1f;
    var h10 = amountCubed - (2f * amountSquared) + amount;
    var h01 = (-2f * amountCubed) + (3f * amountSquared);
    var h11 = amountCubed - amountSquared;
    var startTangent = start.Tangent * parameterRange;
    var endTangent = end.Tangent * parameterRange;
    var position = (h00 * start.Position) + (h10 * startTangent)
      + (h01 * end.Position) + (h11 * endTangent);

    var derivative = (((6f * amountSquared) - (6f * amount)) * start.Position
      + ((3f * amountSquared) - (4f * amount) + 1f) * startTangent
      + ((-6f * amountSquared) + (6f * amount)) * end.Position
      + ((3f * amountSquared) - (2f * amount)) * endTangent) / parameterRange;

    return new(
      position,
      derivative,
      TrackMath.LerpUnwrapped(start.BankRadians, end.BankRadians, amount)
    );
  }
}
