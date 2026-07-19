// TrackPieceTests
//
// Authors:
//   - OpenRCT3 Contributors
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Numerics;
using OpenRCT3.Simulation.Tracks;

namespace OpenRCT3.Tests.Simulation.Tracks;

[TestFixture]
public class TrackPieceTests {
  [Test]
  public void HandAuthoredPiece_SamplesArcLengthBoundariesAndContactPoints() {
    var piece = StraightPiece(length: 10f);

    var entry = piece.SampleRail(RailSide.Left, 0f);
    var exit = piece.SampleRail(RailSide.Right, piece.Length);
    var contacts = piece.SampleContactPoints(piece.Length * 0.5f);

    Assert.That(piece.Length, Is.EqualTo(10f).Within(0.0001f));
    AssertVector(entry.Position, new(0f, -0.5f, 0f));
    AssertVector(exit.Position, new(10f, 0.5f, 0f));
    AssertVector(contacts.Midpoint, new(5f, 0f, 0f));
    Assert.That(contacts.Left.ArcLength, Is.EqualTo(contacts.Right.ArcLength));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      piece.SampleRail(RailSide.Left, -0.001f)));
    Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
      piece.SampleRail(RailSide.Right, piece.Length + 0.001f)));
  }

  [Test]
  public void ProceduralGeometry_AuthorsTheWholePieceAndKeepsAnalyticEvaluation() {
    var geometry = TrackPieceGeometry.FromProcedural(
      controlPointCount: 3,
      parameter => new(
        new(parameter * 8f, -0.5f, 0f),
        new(8f, 0f, 0f),
        new(parameter * 8f, 0.5f, 0f),
        new(8f, 0f, 0f),
        0f
      )
    );
    var piece = new TrackPiece(geometry, Matrix4x4.CreateTranslation(2f, 3f, 4f));

    var analytic = piece.EvaluateRail(RailSide.Right, 0.5f);

    Assert.That(geometry.AuthoringMode, Is.EqualTo(TrackPieceAuthoringMode.Procedural));
    Assert.That(geometry.ControlPoints, Has.Count.EqualTo(3));
    AssertVector(analytic.Position, new(6f, 3.5f, 4f));
    AssertVector(analytic.Tangent, Vector3.UnitX);
  }

  [Test]
  public void AdaptiveBake_IsDeterministicAndRespondsToCurveAndBankRate() {
    var strictChord = new TrackBakeSettings(
      ChordToleranceGaugeFraction: 0f,
      MinimumChordTolerance: 0.01f,
      MaximumBankAngleChangeRadians: MathF.PI,
      MaximumSubdivisionDepth: 12
    );
    var curvedGeometry = TrackPieceGeometry.FromHandAuthored([
      Pair(0f, new(0f, 0f, 0f), new(10f, 0f, 0f), Vector3.UnitZ),
      Pair(1f, new(10f, 10f, 0f), new(0f, 10f, 0f), Vector3.UnitZ),
    ]);
    var firstCurve = new TrackPiece(curvedGeometry, Matrix4x4.Identity, strictChord);
    var secondCurve = new TrackPiece(curvedGeometry, Matrix4x4.Identity, strictChord);
    var inflectedCurve = new TrackPiece(
      TrackPieceGeometry.FromHandAuthored([
        Pair(0f, Vector3.Zero, new(10f, 20f, 0f), Vector3.UnitZ),
        Pair(1f, new(10f, 0f, 0f), new(10f, 20f, 0f), Vector3.UnitZ),
      ]),
      Matrix4x4.Identity,
      strictChord
    );

    var bankDriven = new TrackPiece(
      TrackPieceGeometry.FromHandAuthored([
        Pair(0f, Vector3.Zero, new(10f, 0f, 0f), Vector3.UnitY, bank: 0f),
        Pair(1f, new(10f, 0f, 0f), new(10f, 0f, 0f), Vector3.UnitY, bank: MathF.PI / 2f),
      ]),
      Matrix4x4.Identity,
      new(
        ChordToleranceGaugeFraction: 0f,
        MinimumChordTolerance: 1f,
        MaximumBankAngleChangeRadians: 0.1f,
        MaximumSubdivisionDepth: 12
      )
    );

    Assert.That(firstCurve.BakedSampleCount, Is.GreaterThan(2));
    Assert.That(firstCurve.BakedArcLengths, Is.EqualTo(secondCurve.BakedArcLengths));
    Assert.That(inflectedCurve.BakedSampleCount, Is.GreaterThan(2));
    Assert.That(bankDriven.BakedSampleCount, Is.GreaterThan(2));
    Assert.That(
      bankDriven.SampleRail(RailSide.Left, bankDriven.Length).BankRadians,
      Is.EqualTo(MathF.PI / 2f).Within(0.0001f)
    );
  }

  [Test]
  public void HandAuthoredGeometry_RejectsMalformedRailPairs() {
    Assert.Throws<ArgumentException>(new Action(() =>
      TrackPieceGeometry.FromHandAuthored([
        new(0f, Vector3.Zero, Vector3.UnitX, Vector3.Zero, Vector3.UnitX, 0f),
        new(1f, Vector3.UnitX, Vector3.UnitX, Vector3.UnitX, Vector3.UnitX, 0f),
      ])));
    Assert.Throws<ArgumentException>(new Action(() =>
      TrackPieceGeometry.FromHandAuthored([
        new(0f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, -Vector3.UnitX, 0f),
        new(1f, Vector3.UnitX, Vector3.UnitX, Vector3.One, -Vector3.UnitX, 0f),
      ])));
    Assert.Throws<ArgumentException>(new Action(() =>
      TrackPieceGeometry.FromHandAuthored([
        Pair(0.1f, Vector3.Zero, Vector3.UnitX, Vector3.UnitY),
        Pair(1f, Vector3.UnitX, Vector3.UnitX, Vector3.UnitY),
      ])));
  }

  private static TrackPiece StraightPiece(float length)
    => new(TrackPieceGeometry.FromHandAuthored([
      Pair(0f, Vector3.Zero, new(length, 0f, 0f), Vector3.UnitY),
      Pair(1f, new(length, 0f, 0f), new(length, 0f, 0f), Vector3.UnitY),
    ]));

  private static RailControlPair Pair(
    float parameter,
    Vector3 center,
    Vector3 tangent,
    Vector3 gaugeDirection,
    float bank = 0f
  ) {
    var halfGauge = Vector3.Normalize(gaugeDirection) * 0.5f;
    return new(
      parameter,
      center - halfGauge,
      tangent,
      center + halfGauge,
      tangent,
      bank
    );
  }

  private static void AssertVector(Vector3 actual, Vector3 expected)
    => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.0001f));
}
