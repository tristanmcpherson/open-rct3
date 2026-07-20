// Path Visual Selector
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL.Files;
using System.Linq;

namespace OpenRCT3.Simulation;

[Flags]
internal enum PathEdgeMask {
  None = 0,
  South = 1,
  West = 2,
  East = 4,
  North = 8,
  All = South | West | East | North,
}

[Flags]
internal enum PathCornerMask {
  None = 0,
  SouthWest = 1,
  SouthEast = 2,
  NorthWest = 4,
  NorthEast = 8,
  All = SouthWest | SouthEast | NorthWest | NorthEast,
}

/// <summary>One exact PTD/QTD owner selection plus its local-to-world quarter turn.</summary>
internal sealed record PathVisualSelection(
  string OwnerName,
  int QuarterTurns,
  string ShapeKind
);

/// <summary>
/// Selects the native path owner whose documented rail and corner-stump topology matches a tile.
/// </summary>
internal static class PathVisualSelector {
  public static PathVisualSelection SelectOrdinary(
    PathType resource,
    PathTile tile,
    Func<Edge, bool> isConnected,
    Func<PathCornerMask, bool> hasDiagonal
  ) {
    ArgumentNullException.ThrowIfNull(resource);
    ArgumentNullException.ThrowIfNull(isConnected);
    ArgumentNullException.ThrowIfNull(hasDiagonal);

    if (tile.Raised && tile.RaisedSlope != PathRaisedSlope.Flat)
      return SelectOrdinarySlope(resource, tile, isConnected);

    var edges = EdgeMask(isConnected);
    var diagonals = CornerMask(hasDiagonal);
    if (edges == PathEdgeMask.None)
      throw new InvalidDataException("An isolated ordinary path has no native PTD rail shape.");

    if (Count(edges) == 1)
      return Select(resource, PathTypeShapeKind.TurnU, edges, PathEdgeMask.West);
    if (Count(edges) == 2) {
      if (IsOppositePair(edges))
        return Select(resource, PathTypeShapeKind.StraightA, edges,
          PathEdgeMask.South | PathEdgeMask.North);

      const PathEdgeMask canonicalEdges = PathEdgeMask.North | PathEdgeMask.West;
      var quarterTurns = MatchRotation(canonicalEdges, edges);
      var canonicalDiagonals = Rotate(diagonals, 4 - quarterTurns);
      var kind = (canonicalDiagonals & PathCornerMask.NorthWest) == 0
        ? PathTypeShapeKind.TurnLA
        : PathTypeShapeKind.TurnLB;
      return Select(resource, kind, quarterTurns);
    }

    if (Count(edges) == 3) {
      const PathEdgeMask canonicalEdges =
        PathEdgeMask.South | PathEdgeMask.East | PathEdgeMask.West;
      var quarterTurns = MatchRotation(canonicalEdges, edges);
      var canonicalDiagonals = Rotate(diagonals, 4 - quarterTurns);
      var southWestMissing =
        (canonicalDiagonals & PathCornerMask.SouthWest) == 0;
      var southEastMissing =
        (canonicalDiagonals & PathCornerMask.SouthEast) == 0;
      var kind = (southWestMissing, southEastMissing) switch {
        (false, false) => PathTypeShapeKind.StraightB,
        (true, true) => PathTypeShapeKind.TurnTA,
        (true, false) => PathTypeShapeKind.TurnTB,
        (false, true) => PathTypeShapeKind.TurnTC,
      };
      return Select(resource, kind, quarterTurns);
    }

    return SelectFourWay(resource, diagonals);
  }

  public static PathVisualSelection SelectQueue(QueueType resource, PathTile tile) {
    ArgumentNullException.ThrowIfNull(resource);
    if (tile.Raised && tile.RaisedSlope != PathRaisedSlope.Flat) {
      var quarterTurns = MatchRotation(Edge.West, tile.RaisedSlopeDirection);
      if (tile.RaisedSlope == PathRaisedSlope.Gentle) {
        var owner = tile.QueueEndDirection == tile.RaisedSlopeDirection
          ? resource.SlopeStraight1
          : resource.SlopeStraight2;
        return SelectQueueOwner(resource, owner, quarterTurns, "QueueSlopeStraight");
      }
      var steepOwner = tile.QueueEndDirection == tile.RaisedSlopeDirection
        ? resource.SlopeUp
        : resource.SlopeDown;
      return SelectQueueOwner(resource, steepOwner, quarterTurns, "QueueSlope");
    }

    var start = tile.QueueStartDirection;
    var end = tile.QueueEndDirection;
    if (!start.HasValue || !end.HasValue) {
      var direction = tile.Direction ?? Edge.East;
      var quarterTurns = MatchRotation(Edge.East, direction);
      return SelectQueueOwner(resource, resource.Straight, quarterTurns, "QueueStraight");
    }
    if (start == end)
      throw new InvalidDataException("A queue path has identical incoming and outgoing edges.");
    if (start.Value.Opposite() == end.Value) {
      var quarterTurns = MatchRotation(
        PathEdgeMask.West | PathEdgeMask.East,
        ToMask(start.Value) | ToMask(end.Value));
      return SelectQueueOwner(resource, resource.Straight, quarterTurns, "QueueStraight");
    }

    var incoming = start.Value.Offset();
    var outgoing = end.Value.Offset();
    var cross = (-incoming.dx * outgoing.dy) - (-incoming.dy * outgoing.dx);
    if (cross > 0) {
      var quarterTurns = MatchOrderedRotation(Edge.South, Edge.West, start.Value, end.Value);
      return SelectQueueOwner(resource, resource.TurnLeft, quarterTurns, "QueueTurnLeft");
    }
    var rightTurns = MatchOrderedRotation(Edge.South, Edge.East, start.Value, end.Value);
    return SelectQueueOwner(resource, resource.TurnRight, rightTurns, "QueueTurnRight");
  }

  private static PathVisualSelection SelectOrdinarySlope(
    PathType resource,
    PathTile tile,
    Func<Edge, bool> isConnected
  ) {
    var quarterTurns = MatchRotation(Edge.West, tile.RaisedSlopeDirection);
    if (tile.RaisedSlope == PathRaisedSlope.Sloped)
      return Select(resource, PathTypeShapeKind.Slope, quarterTurns);

    var northConnected = isConnected(Rotate(Edge.North, quarterTurns));
    var southConnected = isConnected(Rotate(Edge.South, quarterTurns));
    var kind = (northConnected, southConnected) switch {
      (false, false) => PathTypeShapeKind.SlopeStraight,
      (true, false) => PathTypeShapeKind.SlopeStraightLeft,
      (false, true) => PathTypeShapeKind.SlopeStraightRight,
      (true, true) => PathTypeShapeKind.SlopeMid,
    };
    return Select(resource, kind, quarterTurns);
  }

  private static PathVisualSelection SelectFourWay(PathType resource, PathCornerMask diagonals) {
    var missing = PathCornerMask.All & ~diagonals;
    if (missing == PathCornerMask.None)
      return Select(resource, PathTypeShapeKind.Flat, 0);
    if (missing == PathCornerMask.All)
      return Select(resource, PathTypeShapeKind.TurnX, 0);

    var missingCount = Count(missing);
    if (missingCount == 1) {
      var quarterTurns = MatchRotation(PathCornerMask.NorthWest, missing);
      return Select(resource, PathTypeShapeKind.CornerA, quarterTurns);
    }
    if (missingCount == 2) {
      if (IsOppositePair(missing)) {
        var quarterTurns = MatchRotation(
          PathCornerMask.SouthWest | PathCornerMask.NorthEast,
          missing);
        return Select(resource, PathTypeShapeKind.CornerC, quarterTurns);
      }
      var adjacentTurns = MatchRotation(
        PathCornerMask.NorthWest | PathCornerMask.NorthEast,
        missing);
      return Select(resource, PathTypeShapeKind.CornerB, adjacentTurns);
    }

    var cornerDTurns = MatchRotation(
      PathCornerMask.SouthWest | PathCornerMask.SouthEast | PathCornerMask.NorthEast,
      missing);
    return Select(resource, PathTypeShapeKind.CornerD, cornerDTurns);
  }

  private static PathVisualSelection Select(
    PathType resource,
    PathTypeShapeKind kind,
    PathEdgeMask actual,
    PathEdgeMask canonical
  ) => Select(resource, kind, MatchRotation(canonical, actual));

  private static PathVisualSelection Select(
    PathType resource,
    PathTypeShapeKind kind,
    int quarterTurns
  ) {
    if (resource.ShapeOwners == null)
      throw new InvalidDataException(
        $"PTD '{resource.InternalName}' has a null shape-owner list.");
    var groups = resource.ShapeOwners.Where(owner => owner != null && owner.Kind == kind).ToArray();
    if (groups.Length != 1)
      throw new InvalidDataException(
        $"PTD '{resource.InternalName}' has {groups.Length} {kind} owner groups; " +
        "exactly one is required.");
    var group = groups[0];
    if (group.Variants == null || group.Variants.Count != 4)
      throw new InvalidDataException(
        $"PTD '{resource.InternalName}' {kind} owner has " +
        $"{group.Variants?.Count ?? 0} variants.");
    var normalizedTurns = NormalizeTurns(quarterTurns);
    var ownerName = group.Variants[normalizedTurns];
    if (string.IsNullOrWhiteSpace(ownerName))
      throw new InvalidDataException(
        $"PTD '{resource.InternalName}' {kind} owner variant {normalizedTurns} is empty.");
    return new PathVisualSelection(ownerName, normalizedTurns, kind.ToString());
  }

  private static PathVisualSelection SelectQueueOwner(
    QueueType resource,
    string ownerName,
    int quarterTurns,
    string shapeKind
  ) {
    if (string.IsNullOrWhiteSpace(ownerName))
      throw new InvalidDataException(
        $"QTD '{resource.InternalName}' {shapeKind} owner is empty.");
    return new PathVisualSelection(ownerName, NormalizeTurns(quarterTurns), shapeKind);
  }

  private static PathEdgeMask EdgeMask(Func<Edge, bool> predicate) {
    var result = PathEdgeMask.None;
    foreach (var edge in Enum.GetValues<Edge>())
      if (predicate(edge)) result |= ToMask(edge);
    return result;
  }

  private static PathCornerMask CornerMask(Func<PathCornerMask, bool> predicate) {
    var result = PathCornerMask.None;
    foreach (var corner in new[] {
      PathCornerMask.SouthWest,
      PathCornerMask.SouthEast,
      PathCornerMask.NorthWest,
      PathCornerMask.NorthEast,
    }) if (predicate(corner)) result |= corner;
    return result;
  }

  private static int MatchOrderedRotation(
    Edge canonicalStart,
    Edge canonicalEnd,
    Edge actualStart,
    Edge actualEnd
  ) {
    foreach (var quarterTurns in Enumerable.Range(0, 4))
      if (Rotate(canonicalStart, quarterTurns) == actualStart &&
          Rotate(canonicalEnd, quarterTurns) == actualEnd) return quarterTurns;
    throw new InvalidDataException("Queue turn directions cannot be mapped to a quarter turn.");
  }

  private static int MatchRotation(Edge canonical, Edge actual) {
    foreach (var quarterTurns in Enumerable.Range(0, 4))
      if (Rotate(canonical, quarterTurns) == actual) return quarterTurns;
    throw new InvalidDataException("Path direction cannot be mapped to a quarter turn.");
  }

  private static int MatchRotation(PathEdgeMask canonical, PathEdgeMask actual) {
    foreach (var quarterTurns in Enumerable.Range(0, 4))
      if (Rotate(canonical, quarterTurns) == actual) return quarterTurns;
    throw new InvalidDataException("Path edge topology cannot be mapped to a native shape.");
  }

  private static int MatchRotation(PathCornerMask canonical, PathCornerMask actual) {
    foreach (var quarterTurns in Enumerable.Range(0, 4))
      if (Rotate(canonical, quarterTurns) == actual) return quarterTurns;
    throw new InvalidDataException("Path corner topology cannot be mapped to a native shape.");
  }

  private static PathEdgeMask Rotate(PathEdgeMask mask, int quarterTurns) {
    var result = PathEdgeMask.None;
    foreach (var edge in Enum.GetValues<Edge>())
      if ((mask & ToMask(edge)) != 0) result |= ToMask(Rotate(edge, quarterTurns));
    return result;
  }

  private static PathCornerMask Rotate(PathCornerMask mask, int quarterTurns) {
    var result = mask;
    foreach (var _ in Enumerable.Range(0, NormalizeTurns(quarterTurns))) {
      result = PathCornerMask.None
        | ((result & PathCornerMask.SouthWest) != 0 ? PathCornerMask.SouthEast : 0)
        | ((result & PathCornerMask.SouthEast) != 0 ? PathCornerMask.NorthEast : 0)
        | ((result & PathCornerMask.NorthEast) != 0 ? PathCornerMask.NorthWest : 0)
        | ((result & PathCornerMask.NorthWest) != 0 ? PathCornerMask.SouthWest : 0);
    }
    return result;
  }

  private static Edge Rotate(Edge edge, int quarterTurns) {
    var result = edge;
    foreach (var _ in Enumerable.Range(0, NormalizeTurns(quarterTurns))) {
      result = result switch {
        Edge.South => Edge.East,
        Edge.East => Edge.North,
        Edge.North => Edge.West,
        Edge.West => Edge.South,
        _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null),
      };
    }
    return result;
  }

  private static int NormalizeTurns(int quarterTurns) => ((quarterTurns % 4) + 4) % 4;

  private static PathEdgeMask ToMask(Edge edge) => edge switch {
    Edge.South => PathEdgeMask.South,
    Edge.West => PathEdgeMask.West,
    Edge.East => PathEdgeMask.East,
    Edge.North => PathEdgeMask.North,
    _ => throw new ArgumentOutOfRangeException(nameof(edge), edge, null),
  };

  private static int Count(PathEdgeMask mask) =>
    System.Numerics.BitOperations.PopCount(Convert.ToUInt32(mask));

  private static int Count(PathCornerMask mask) =>
    System.Numerics.BitOperations.PopCount(Convert.ToUInt32(mask));

  private static bool IsOppositePair(PathEdgeMask mask) =>
    mask == (PathEdgeMask.South | PathEdgeMask.North) ||
    mask == (PathEdgeMask.West | PathEdgeMask.East);

  private static bool IsOppositePair(PathCornerMask mask) =>
    mask == (PathCornerMask.SouthWest | PathCornerMask.NorthEast) ||
    mask == (PathCornerMask.SouthEast | PathCornerMask.NorthWest);
}
