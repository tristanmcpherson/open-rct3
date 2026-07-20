// Track Graph Traversal
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenRCT3.Simulation.Tracks;

/// <summary>
/// Selects one exact adjacent edge when graph traversal crosses a branch or merge.
/// </summary>
/// <param name="boundary">The exact departing edge boundary from which selection occurs.</param>
/// <param name="node">The graph node being crossed.</param>
/// <param name="candidates">The immutable exact outgoing or incoming graph edges.</param>
public delegate TrackEdge TrackGraphEdgeSelector(
  TrackGraphCursor boundary,
  TrackNode node,
  IReadOnlyList<TrackEdge> candidates);

/// <summary>A sampled graph position retaining its exact edge identity.</summary>
public readonly record struct TrackGraphSample(
  int EdgeIndex,
  TrackEdge Edge,
  float PieceArcLength,
  TrackContactPoints ContactPoints
);

/// <summary>An immutable vehicle position bound to one <see cref="TrackGraphTraversal"/>.</summary>
/// <remarks>
/// Edge identity is authoritative at a shared boundary. An edge exit and an adjacent edge entry are
/// geometrically coincident but remain distinct cursor values so reversing direction retains the
/// side of the seam from which the vehicle arrived.
/// </remarks>
public readonly struct TrackGraphCursor : IEquatable<TrackGraphCursor> {
  private readonly TrackGraphTraversal? traversal;

  /// <summary>The exact graph-edge index retained by this cursor.</summary>
  public int EdgeIndex { get; }

  /// <summary>The piece-local centerline arc length retained in double precision.</summary>
  public double PieceArcLength { get; }

  /// <summary>Whether this value was created by a traversal rather than default initialization.</summary>
  public bool IsInitialized => traversal != null;

  /// <summary>The graph to which this cursor is bound.</summary>
  public TrackGraph Graph => Owner.Graph;

  /// <summary>The exact graph edge retained at this position.</summary>
  public TrackEdge Edge => Graph.Edges[EdgeIndex];

  internal TrackGraphTraversal? Traversal => traversal;

  internal TrackGraphCursor(
    TrackGraphTraversal traversal,
    int edgeIndex,
    double pieceArcLength
  ) {
    this.traversal = traversal;
    EdgeIndex = edgeIndex;
    PieceArcLength = pieceArcLength;
  }

  /// <summary>
  /// Returns a new cursor advanced by a signed distance through explicitly selected graph branches.
  /// </summary>
  public TrackGraphCursor Advance(
    double distance,
    TrackGraphEdgeSelector? forwardSelector = null,
    TrackGraphEdgeSelector? reverseSelector = null
  ) => Owner.Advance(this, distance, forwardSelector, reverseSelector);

  /// <summary>Samples both contact rails from this cursor's exact edge-local identity.</summary>
  public TrackGraphSample Sample() => Owner.Sample(this);

  public bool Equals(TrackGraphCursor other) =>
    ReferenceEquals(traversal, other.traversal) &&
    EdgeIndex == other.EdgeIndex &&
    PieceArcLength.Equals(other.PieceArcLength);

  public override bool Equals(object? obj) =>
    obj is TrackGraphCursor other && Equals(other);

  public override int GetHashCode() => HashCode.Combine(
    traversal,
    EdgeIndex,
    PieceArcLength);

  public static bool operator ==(TrackGraphCursor left, TrackGraphCursor right) =>
    left.Equals(right);

  public static bool operator !=(TrackGraphCursor left, TrackGraphCursor right) =>
    !left.Equals(right);

  private TrackGraphTraversal Owner => traversal
    ?? throw new InvalidOperationException("The track-graph cursor is not initialized.");
}

/// <summary>Creates and advances immutable vehicle cursors over an open or branching track DAG.</summary>
/// <remarks>
/// Advancement operates only on scalar piece-local arc lengths. Contact positions are always
/// resampled from the owning <see cref="TrackPiece"/> bake, so repeated sampling never integrates
/// world-space error. A move ending exactly at a seam retains the departing edge's exit when moving
/// forward and the current edge's entry when moving backward. Only distance beyond a seam changes
/// edge identity.
/// </remarks>
public sealed class TrackGraphTraversal {
  private readonly IReadOnlyDictionary<string, ReadOnlyCollection<TrackEdge>> outgoing;
  private readonly IReadOnlyDictionary<string, ReadOnlyCollection<TrackEdge>> incoming;
  private readonly IReadOnlyDictionary<TrackEdge, int> edgeIndices;

  public TrackGraph Graph { get; }

  public TrackGraphTraversal(TrackGraph graph) {
    ArgumentNullException.ThrowIfNull(graph);
    Graph = graph;
    edgeIndices = BuildEdgeIndex(graph);
    outgoing = BuildAdjacency(graph, forward: true);
    incoming = BuildAdjacency(graph, forward: false);
  }

  /// <summary>Creates a cursor at one exact graph-edge-local arc identity.</summary>
  public TrackGraphCursor AtEdge(TrackEdge edge, double pieceArcLength) {
    ArgumentNullException.ThrowIfNull(edge);
    if (!edgeIndices.TryGetValue(edge, out var edgeIndex))
      throw new ArgumentException(
        "The edge is not one exact member of this graph.",
        nameof(edge));
    ValidatePosition(edgeIndex, pieceArcLength);
    return new(this, edgeIndex, pieceArcLength);
  }

  /// <summary>Advances a cursor by a signed finite distance through this DAG.</summary>
  public TrackGraphCursor Advance(
    TrackGraphCursor cursor,
    double distance,
    TrackGraphEdgeSelector? forwardSelector = null,
    TrackGraphEdgeSelector? reverseSelector = null
  ) {
    ValidateCursor(cursor);
    if (!double.IsFinite(distance))
      throw new ArgumentOutOfRangeException(nameof(distance));
    if (distance == 0d) return cursor;

    var remaining = Math.Abs(distance);
    return distance > 0d
      ? AdvanceForward(cursor, remaining, forwardSelector)
      : AdvanceBackward(cursor, remaining, reverseSelector);
  }

  /// <summary>
  /// Samples the cursor's exact edge directly instead of locating it from an accumulated path
  /// distance.
  /// </summary>
  public TrackGraphSample Sample(TrackGraphCursor cursor) {
    ValidateCursor(cursor);
    var edge = Graph.Edges[cursor.EdgeIndex];
    var pieceLength = Convert.ToDouble(edge.Piece.Length);
    var pieceArcLength = cursor.PieceArcLength == pieceLength
      ? edge.Piece.Length
      : Convert.ToSingle(cursor.PieceArcLength);
    pieceArcLength = Math.Clamp(pieceArcLength, 0f, edge.Piece.Length);
    return new(
      cursor.EdgeIndex,
      edge,
      pieceArcLength,
      edge.Piece.SampleContactPoints(pieceArcLength));
  }

  private TrackGraphCursor AdvanceForward(
    TrackGraphCursor cursor,
    double remaining,
    TrackGraphEdgeSelector? selector
  ) {
    var edgeIndex = cursor.EdgeIndex;
    var pieceArcLength = cursor.PieceArcLength;
    foreach (var _ in Enumerable.Range(0, Graph.Edges.Count + 1)) {
      var edge = Graph.Edges[edgeIndex];
      var pieceLength = Convert.ToDouble(edge.Piece.Length);
      var distanceToExit = pieceLength - pieceArcLength;
      if (remaining <= distanceToExit) {
        var resultArcLength = remaining == distanceToExit
          ? pieceLength
          : Math.Min(pieceLength, pieceArcLength + remaining);
        return new(this, edgeIndex, resultArcLength);
      }

      remaining -= distanceToExit;
      var boundary = new TrackGraphCursor(this, edgeIndex, pieceLength);
      var next = SelectAdjacent(
        boundary,
        edge.To,
        outgoing[edge.To.Id],
        selector,
        forward: true);
      edgeIndex = edgeIndices[next];
      pieceArcLength = 0d;
    }

    throw new InvalidOperationException(
      "Forward track traversal exceeded the graph's bounded edge count.");
  }

  private TrackGraphCursor AdvanceBackward(
    TrackGraphCursor cursor,
    double remaining,
    TrackGraphEdgeSelector? selector
  ) {
    var edgeIndex = cursor.EdgeIndex;
    var pieceArcLength = cursor.PieceArcLength;
    foreach (var _ in Enumerable.Range(0, Graph.Edges.Count + 1)) {
      if (remaining <= pieceArcLength) {
        var resultArcLength = remaining == pieceArcLength
          ? 0d
          : Math.Max(0d, pieceArcLength - remaining);
        return new(this, edgeIndex, resultArcLength);
      }

      remaining -= pieceArcLength;
      var edge = Graph.Edges[edgeIndex];
      var boundary = new TrackGraphCursor(this, edgeIndex, 0d);
      var previous = SelectAdjacent(
        boundary,
        edge.From,
        incoming[edge.From.Id],
        selector,
        forward: false);
      edgeIndex = edgeIndices[previous];
      pieceArcLength = previous.Piece.Length;
    }

    throw new InvalidOperationException(
      "Reverse track traversal exceeded the graph's bounded edge count.");
  }

  private static TrackEdge SelectAdjacent(
    TrackGraphCursor boundary,
    TrackNode node,
    IReadOnlyList<TrackEdge> candidates,
    TrackGraphEdgeSelector? selector,
    bool forward
  ) {
    var direction = forward ? "forward" : "reverse";
    var junction = forward ? "branch" : "merge";
    if (candidates.Count == 0)
      throw new InvalidOperationException(
        $"{direction} track traversal cannot continue past terminal node '{node.Id}'.");
    if (candidates.Count == 1) return candidates[0];
    if (selector is null)
      throw new InvalidOperationException(
        $"{direction} track traversal requires an explicit selector at {junction} node " +
        $"'{node.Id}'.");

    var selected = selector(boundary, node, candidates);
    if (selected is null || !candidates.Any(candidate => ReferenceEquals(candidate, selected)))
      throw new ArgumentException(
        $"The {direction} selector did not return one exact adjacent graph edge.",
        forward ? "forwardSelector" : "reverseSelector");
    return selected;
  }

  private void ValidateCursor(TrackGraphCursor cursor) {
    if (!ReferenceEquals(cursor.Traversal, this))
      throw new ArgumentException(
        "The track-graph cursor belongs to a different traversal.",
        nameof(cursor));
    ValidatePosition(cursor.EdgeIndex, cursor.PieceArcLength);
  }

  private void ValidatePosition(int edgeIndex, double pieceArcLength) {
    if (edgeIndex < 0 || edgeIndex >= Graph.Edges.Count)
      throw new ArgumentOutOfRangeException(nameof(edgeIndex));
    var pieceLength = Convert.ToDouble(Graph.Edges[edgeIndex].Piece.Length);
    if (!double.IsFinite(pieceArcLength)
      || pieceArcLength < 0d
      || pieceArcLength > pieceLength)
      throw new ArgumentOutOfRangeException(nameof(pieceArcLength));
  }

  private static IReadOnlyDictionary<TrackEdge, int> BuildEdgeIndex(TrackGraph graph) {
    var result = new Dictionary<TrackEdge, int>(ReferenceEqualityComparer.Instance);
    foreach (var index in Enumerable.Range(0, graph.Edges.Count)) {
      var edge = graph.Edges[index];
      if (!float.IsFinite(edge.Piece.Length) || edge.Piece.Length <= 0f)
        throw new ArgumentException(
          $"Track edge '{edge.Id}' does not have a finite, positive piece length.",
          nameof(graph));
      result.Add(edge, index);
    }
    return new ReadOnlyDictionary<TrackEdge, int>(result);
  }

  private static IReadOnlyDictionary<string, ReadOnlyCollection<TrackEdge>> BuildAdjacency(
    TrackGraph graph,
    bool forward
  ) {
    var result = new Dictionary<string, ReadOnlyCollection<TrackEdge>>(
      graph.Nodes.Count,
      StringComparer.Ordinal);
    foreach (var node in graph.Nodes) {
      var adjacent = graph.Edges.Where(edge =>
        string.Equals(
          forward ? edge.From.Id : edge.To.Id,
          node.Id,
          StringComparison.Ordinal)).ToArray();
      result.Add(node.Id, Array.AsReadOnly(adjacent));
    }
    return new ReadOnlyDictionary<string, ReadOnlyCollection<TrackEdge>>(result);
  }
}
