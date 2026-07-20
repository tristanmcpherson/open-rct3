// Path Visual Selector Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using NUnit.Framework;
using OpenCobra.OVL.Files;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class PathVisualSelectorTests {
  private static PathType OrdinaryResource() => new(
    "test", 1, "test", "display:txt", "icon:gsi", "primary", "alternate",
    Enum.GetValues<PathTypeShapeKind>()
      .Select(kind => new PathTypeShapeOwners(
        kind,
        Enumerable.Range(0, 4).Select(index => $"{kind}-{index}").ToArray()))
      .ToArray(),
    [],
    null
  );

  private static QueueType QueueResource() => new(
    "queue", "queue", "display:txt", "icon:gsi", "queue:ftx",
    "straight", "left", "right", "up", "down", "gentle-a", "gentle-b", []
  );

  [Test]
  public void SelectOrdinary_UsesConnectedStraightAndCornerOwners() {
    var resource = OrdinaryResource();
    var straight = PathVisualSelector.SelectOrdinary(
      resource,
      new PathTile(),
      edge => edge is Edge.South or Edge.North,
      _ => false
    );
    var corner = PathVisualSelector.SelectOrdinary(
      resource,
      new PathTile(),
      edge => edge is Edge.North or Edge.West,
      _ => false
    );

    using (Assert.EnterMultipleScope()) {
      Assert.That(straight.ShapeKind, Is.EqualTo(nameof(PathTypeShapeKind.StraightA)));
      Assert.That(straight.OwnerName, Does.StartWith("StraightA-"));
      Assert.That(corner.ShapeKind, Is.EqualTo(nameof(PathTypeShapeKind.TurnLA)));
      Assert.That(corner.OwnerName, Is.EqualTo("TurnLA-0"));
    }
  }

  [Test]
  public void SelectOrdinary_UsesJunctionAndRaisedSlopeOwners() {
    var resource = OrdinaryResource();
    var junction = PathVisualSelector.SelectOrdinary(
      resource,
      new PathTile(),
      _ => true,
      _ => false
    );
    var slope = PathVisualSelector.SelectOrdinary(
      resource,
      new PathTile {
        Raised = true,
        RaisedSlope = PathRaisedSlope.Gentle,
        RaisedSlopeDirection = Edge.West,
      },
      edge => edge is Edge.North or Edge.South,
      _ => false
    );

    using (Assert.EnterMultipleScope()) {
      Assert.That(junction.ShapeKind, Is.EqualTo(nameof(PathTypeShapeKind.TurnX)));
      Assert.That(slope.ShapeKind, Is.EqualTo(nameof(PathTypeShapeKind.SlopeMid)));
      Assert.That(slope.OwnerName, Is.EqualTo("SlopeMid-0"));
    }
  }

  [Test]
  public void SelectQueue_UsesDirectedTurnAndSlopeOwners() {
    var resource = QueueResource();
    var turn = PathVisualSelector.SelectQueue(resource, new PathTile {
      QueueStartDirection = Edge.South,
      QueueEndDirection = Edge.West,
    });
    var slope = PathVisualSelector.SelectQueue(resource, new PathTile {
      Raised = true,
      RaisedSlope = PathRaisedSlope.Sloped,
      RaisedSlopeDirection = Edge.West,
      QueueEndDirection = Edge.West,
    });

    using (Assert.EnterMultipleScope()) {
      Assert.That(turn.OwnerName, Is.EqualTo("left"));
      Assert.That(turn.ShapeKind, Is.EqualTo("QueueTurnLeft"));
      Assert.That(slope.OwnerName, Is.EqualTo("up"));
      Assert.That(slope.ShapeKind, Is.EqualTo("QueueSlope"));
    }
  }

  [Test]
  public void SelectOrdinary_CoversEveryNonIsolatedEdgeAndDiagonalMask() {
    var resource = OrdinaryResource();

    foreach (var edgeValue in Enumerable.Range(1, Convert.ToInt32(PathEdgeMask.All))) {
      var edges = (PathEdgeMask)edgeValue;
      foreach (var diagonalValue in Enumerable.Range(
                 0,
                 Convert.ToInt32(PathCornerMask.All) + 1)) {
        var diagonals = (PathCornerMask)diagonalValue;
        var selection = PathVisualSelector.SelectOrdinary(
          resource,
          new PathTile(),
          edge => (edges & ToMask(edge)) != 0,
          corner => (diagonals & corner) != 0);

        using (Assert.EnterMultipleScope()) {
          Assert.That(selection.QuarterTurns, Is.InRange(0, 3),
            $"edges={edges}, diagonals={diagonals}");
          Assert.That(selection.OwnerName,
            Is.EqualTo($"{selection.ShapeKind}-{selection.QuarterTurns}"),
            $"edges={edges}, diagonals={diagonals}");
        }
      }
    }
  }

  [Test]
  public void SelectOrdinary_ClassifiesEveryFourWayDiagonalMask() {
    var resource = OrdinaryResource();

    foreach (var diagonalValue in Enumerable.Range(
               0,
               Convert.ToInt32(PathCornerMask.All) + 1)) {
      var diagonals = (PathCornerMask)diagonalValue;
      var missing = PathCornerMask.All & ~diagonals;
      var missingCount = System.Numerics.BitOperations.PopCount(Convert.ToUInt32(missing));
      var expected = missingCount switch {
        0 => PathTypeShapeKind.Flat,
        1 => PathTypeShapeKind.CornerA,
        2 when IsOpposite(missing) => PathTypeShapeKind.CornerC,
        2 => PathTypeShapeKind.CornerB,
        3 => PathTypeShapeKind.CornerD,
        4 => PathTypeShapeKind.TurnX,
        _ => throw new InvalidOperationException(),
      };

      var selection = PathVisualSelector.SelectOrdinary(
        resource,
        new PathTile(),
        _ => true,
        corner => (diagonals & corner) != 0);

      Assert.That(selection.ShapeKind, Is.EqualTo(expected.ToString()),
        $"diagonals={diagonals}");
    }
  }

  [Test]
  public void SelectQueue_CoversEveryOrderedDirectionPair() {
    var resource = QueueResource();
    var leftTurns = new HashSet<(Edge Start, Edge End)> {
      (Edge.South, Edge.West),
      (Edge.East, Edge.South),
      (Edge.North, Edge.East),
      (Edge.West, Edge.North),
    };
    var selections = new List<PathVisualSelection>();

    foreach (var start in Enum.GetValues<Edge>()) {
      foreach (var end in Enum.GetValues<Edge>()) {
        var tile = new PathTile {
          QueueStartDirection = start,
          QueueEndDirection = end,
        };
        if (start == end) {
          Assert.Throws<InvalidDataException>(new Action(() =>
            PathVisualSelector.SelectQueue(resource, tile)));
          continue;
        }

        var selection = PathVisualSelector.SelectQueue(resource, tile);
        selections.Add(selection);
        if (start.Opposite() == end) {
          using (Assert.EnterMultipleScope()) {
            Assert.That(selection.ShapeKind, Is.EqualTo("QueueStraight"));
            Assert.That(selection.OwnerName, Is.EqualTo(resource.Straight));
          }
        } else if (leftTurns.Contains((start, end))) {
          using (Assert.EnterMultipleScope()) {
            Assert.That(selection.ShapeKind, Is.EqualTo("QueueTurnLeft"));
            Assert.That(selection.OwnerName, Is.EqualTo(resource.TurnLeft));
          }
        } else {
          using (Assert.EnterMultipleScope()) {
            Assert.That(selection.ShapeKind, Is.EqualTo("QueueTurnRight"));
            Assert.That(selection.OwnerName, Is.EqualTo(resource.TurnRight));
          }
        }
      }
    }

    using (Assert.EnterMultipleScope()) {
      Assert.That(selections, Has.Count.EqualTo(12));
      Assert.That(
        selections.Count(selection => selection.ShapeKind == "QueueStraight"),
        Is.EqualTo(4));
      Assert.That(
        selections.Count(selection => selection.ShapeKind == "QueueTurnLeft"),
        Is.EqualTo(4));
      Assert.That(
        selections.Count(selection => selection.ShapeKind == "QueueTurnRight"),
        Is.EqualTo(4));
    }
  }

  [Test]
  public void SelectOrdinarySlope_CoversEveryDirectionSlopeAndSideRailCombination() {
    var resource = OrdinaryResource();
    var directions = new[] { Edge.West, Edge.South, Edge.East, Edge.North };
    var expectedKinds = new[,] {
      { PathTypeShapeKind.SlopeStraight, PathTypeShapeKind.SlopeStraightRight },
      { PathTypeShapeKind.SlopeStraightLeft, PathTypeShapeKind.SlopeMid },
    };

    foreach (var quarterTurns in Enumerable.Range(0, directions.Length)) {
      var direction = directions[quarterTurns];
      var steep = PathVisualSelector.SelectOrdinary(
        resource,
        new PathTile {
          Raised = true,
          RaisedSlope = PathRaisedSlope.Sloped,
          RaisedSlopeDirection = direction,
        },
        _ => false,
        _ => false);
      using (Assert.EnterMultipleScope()) {
        Assert.That(steep.ShapeKind, Is.EqualTo(nameof(PathTypeShapeKind.Slope)));
        Assert.That(steep.QuarterTurns, Is.EqualTo(quarterTurns));
      }

      foreach (var northConnected in new[] { false, true }) {
        foreach (var southConnected in new[] { false, true }) {
          var north = Rotate(Edge.North, quarterTurns);
          var south = Rotate(Edge.South, quarterTurns);
          var gentle = PathVisualSelector.SelectOrdinary(
            resource,
            new PathTile {
              Raised = true,
              RaisedSlope = PathRaisedSlope.Gentle,
              RaisedSlopeDirection = direction,
            },
            edge => (edge == north && northConnected) ||
              (edge == south && southConnected),
            _ => false);
          var expected = expectedKinds[
            northConnected ? 1 : 0,
            southConnected ? 1 : 0];

          using (Assert.EnterMultipleScope()) {
            Assert.That(gentle.ShapeKind, Is.EqualTo(expected.ToString()));
            Assert.That(gentle.QuarterTurns, Is.EqualTo(quarterTurns));
          }
        }
      }
    }
  }

  [Test]
  public void SelectQueueSlope_CoversEveryDirectionAndTravelPolarity() {
    var resource = QueueResource();
    var directions = new[] { Edge.West, Edge.South, Edge.East, Edge.North };

    foreach (var quarterTurns in Enumerable.Range(0, directions.Length)) {
      var direction = directions[quarterTurns];
      foreach (var slope in new[] { PathRaisedSlope.Gentle, PathRaisedSlope.Sloped }) {
        foreach (var travelsUp in new[] { false, true }) {
          var selection = PathVisualSelector.SelectQueue(resource, new PathTile {
            Raised = true,
            RaisedSlope = slope,
            RaisedSlopeDirection = direction,
            QueueEndDirection = travelsUp ? direction : direction.Opposite(),
          });
          var expectedOwner = (slope, travelsUp) switch {
            (PathRaisedSlope.Gentle, true) => resource.SlopeStraight1,
            (PathRaisedSlope.Gentle, false) => resource.SlopeStraight2,
            (PathRaisedSlope.Sloped, true) => resource.SlopeUp,
            _ => resource.SlopeDown,
          };

          using (Assert.EnterMultipleScope()) {
            Assert.That(selection.OwnerName, Is.EqualTo(expectedOwner));
            Assert.That(selection.QuarterTurns, Is.EqualTo(quarterTurns));
          }
        }
      }
    }
  }

  [Test]
  public void SelectOrdinary_RejectsMalformedSelectedOwnerVariants() {
    var resource = OrdinaryResource();
    var owners = resource.ShapeOwners.ToArray();
    var index = Array.FindIndex(owners, owner => owner.Kind == PathTypeShapeKind.StraightA);
    owners[index] = new PathTypeShapeOwners(PathTypeShapeKind.StraightA, ["a", "b", "c"]);
    var malformed = resource with { ShapeOwners = owners };

    Assert.Throws<InvalidDataException>(new Action(() =>
      PathVisualSelector.SelectOrdinary(
        malformed,
        new PathTile(),
        edge => edge is Edge.South or Edge.North,
        _ => false)));
  }

  [Test]
  public void SelectQueue_RejectsBlankSelectedOwner() {
    var malformed = QueueResource() with { Straight = " " };

    Assert.Throws<InvalidDataException>(new Action(() =>
      PathVisualSelector.SelectQueue(malformed, new PathTile { Direction = Edge.East })));
  }

  private static PathEdgeMask ToMask(Edge edge) => edge switch {
    Edge.South => PathEdgeMask.South,
    Edge.West => PathEdgeMask.West,
    Edge.East => PathEdgeMask.East,
    Edge.North => PathEdgeMask.North,
    _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null),
  };

  private static bool IsOpposite(PathCornerMask mask) =>
    mask == (PathCornerMask.SouthWest | PathCornerMask.NorthEast) ||
    mask == (PathCornerMask.SouthEast | PathCornerMask.NorthWest);

  private static Edge Rotate(Edge edge, int quarterTurns) {
    var cycle = new[] { Edge.South, Edge.East, Edge.North, Edge.West };
    var index = Array.IndexOf(cycle, edge);
    return cycle[(index + quarterTurns) % cycle.Length];
  }
}
