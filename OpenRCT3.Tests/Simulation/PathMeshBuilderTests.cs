// Path Mesh Builder Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Numerics;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class PathMeshBuilderTests {
  [Test]
  public void Build_AtGradePathFollowsTerrainAndClearsSurface() {
    var terrain = new Terrain(1, 1, 0);
    terrain.SetCornerHeight(0, 0, TerrainCornerSlot.SouthWest, 0);
    terrain.SetCornerHeight(0, 0, TerrainCornerSlot.SouthEast, 100);
    terrain.SetCornerHeight(0, 0, TerrainCornerSlot.NorthWest, 200);
    terrain.SetCornerHeight(0, 0, TerrainCornerSlot.NorthEast, 300);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.Paths[(0, 0)] = new PathTile();

    var mesh = PathMeshBuilder.Build(park, terrain, Vector4.One, Vector4.UnitX);

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.Vertices, Has.Count.EqualTo(4));
      Assert.That(mesh.Indices, Is.EqualTo(new uint[] { 0, 1, 3, 1, 2, 3 }));
      Assert.That(mesh.Vertices[0].Position.Z, Is.GreaterThan(0f));
      Assert.That(mesh.Vertices[2].Position.Z, Is.EqualTo(2.83f).Within(0.001f));
      Assert.That(mesh.Vertices.All(vertex => vertex.Color == Vector4.One), Is.True);
    }
  }

  [Test]
  public void Build_AtGradePathUsesRenderedTerrainTrianglePlanes() {
    var terrain = new Terrain(1, 1, 0);
    terrain.SetCornerHeight(0, 0, TerrainCornerSlot.SouthWest, 0);
    terrain.SetCornerHeight(0, 0, TerrainCornerSlot.SouthEast, 0);
    terrain.SetCornerHeight(0, 0, TerrainCornerSlot.NorthWest, 0);
    terrain.SetCornerHeight(0, 0, TerrainCornerSlot.NorthEast, 400);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.Paths[(0, 0)] = new PathTile();

    var mesh = PathMeshBuilder.Build(park, terrain, Vector4.One, Vector4.UnitX);

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.Vertices[0].Position.Z, Is.EqualTo(0.04f).Within(0.001f));
      Assert.That(mesh.Vertices[2].Position.Z, Is.EqualTo(3.48f).Within(0.001f));
    }
  }

  [Test]
  public void Build_RaisedPathUsesStoredSlopeAndQueueColor() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.Paths[(0, 0)] = new PathTile {
      IsQueue = true,
      Raised = true,
      RaisedHeight = 600,
      RaisedSlope = PathRaisedSlope.Sloped,
      RaisedSlopeDirection = Edge.North,
    };
    var queueColor = new Vector4(0.2f, 0.4f, 0.8f, 1f);

    var mesh = PathMeshBuilder.Build(park, terrain, Vector4.One, queueColor);

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.Vertices[0].Position.Z, Is.LessThan(mesh.Vertices[3].Position.Z));
      Assert.That(mesh.Vertices[1].Position.Z, Is.LessThan(mesh.Vertices[2].Position.Z));
      Assert.That(mesh.Vertices.All(vertex => vertex.Position.Z >= 6f), Is.True);
      Assert.That(mesh.Vertices.All(vertex => vertex.Color == queueColor), Is.True);
    }
  }

  [Test]
  public void Build_UsesStableRowMajorPathOrder() {
    var terrain = new Terrain(2, 2, 0);
    var park = new Park(buildableWidth: 2, buildableHeight: 2);
    park.Paths[(1, 1)] = new PathTile { IsQueue = true };
    park.Paths[(0, 0)] = new PathTile();

    var mesh = PathMeshBuilder.Build(park, terrain, Vector4.UnitX, Vector4.UnitY);

    using (Assert.EnterMultipleScope()) {
      Assert.That(mesh.Vertices.Take(4).All(vertex => vertex.Color == Vector4.UnitX), Is.True);
      Assert.That(mesh.Vertices.Skip(4).All(vertex => vertex.Color == Vector4.UnitY), Is.True);
    }
  }

  [Test]
  public void BuildBatches_GroupsByKindAndSurfaceNameInDeterministicOrder() {
    var terrain = new Terrain(4, 1, 0);
    var park = new Park(buildableWidth: 4, buildableHeight: 1);
    park.PathPlacements.Add(new PathPlacement(3, 0, new PathTile {
      IsQueue = true,
      SurfaceSystemName = "QueueBlue",
    }));
    park.PathPlacements.Add(new PathPlacement(2, 0, new PathTile {
      SurfaceSystemName = "Stone",
    }));
    park.PathPlacements.Add(new PathPlacement(1, 0, new PathTile {
      SurfaceSystemName = "Brick",
    }));
    park.PathPlacements.Add(new PathPlacement(0, 0, new PathTile {
      IsQueue = true,
      SurfaceSystemName = "Brick",
    }));

    var batches = PathMeshBuilder.BuildBatches(
      park, terrain, Vector4.UnitX, Vector4.UnitY);

    Assert.That(batches.Select(batch => (batch.Kind, batch.SurfaceSystemName)), Is.EqualTo(new[] {
      (PathMaterialKind.Ordinary, "Brick"),
      (PathMaterialKind.Ordinary, "Stone"),
      (PathMaterialKind.Queue, "Brick"),
      (PathMaterialKind.Queue, "QueueBlue"),
    }));
    Assert.That(batches.All(batch => batch.Mesh.Vertices.Count == 4), Is.True);
    Assert.That(batches.Take(2).SelectMany(batch => batch.Mesh.Vertices)
      .All(vertex => vertex.Color == Vector4.UnitX), Is.True);
    Assert.That(batches.Skip(2).SelectMany(batch => batch.Mesh.Vertices)
      .All(vertex => vertex.Color == Vector4.UnitY), Is.True);
  }

  [Test]
  public void BuildBatches_RetainsPerTileSurfaceColoursInGeometryOrder() {
    var terrain = new Terrain(2, 1, 0);
    var park = new Park(buildableWidth: 2, buildableHeight: 1);
    var westColours = new PathSurfaceColours(1, 2, 3);
    var eastColours = new PathSurfaceColours(4, 5, 6);
    park.PathPlacements.Add(new PathPlacement(1, 0, new PathTile {
      IsQueue = true,
      SurfaceSystemName = "QueueStone",
      SurfaceColours = eastColours,
    }));
    park.PathPlacements.Add(new PathPlacement(0, 0, new PathTile {
      IsQueue = true,
      SurfaceSystemName = "QueueStone",
      SurfaceColours = westColours,
    }));

    var batches = PathMeshBuilder.BuildBatches(
      park, terrain, Vector4.One, Vector4.UnitY);

    using (Assert.EnterMultipleScope()) {
      Assert.That(batches, Has.Count.EqualTo(2));
      Assert.That(batches.Select(batch => batch.Kind),
        Is.All.EqualTo(PathMaterialKind.Queue));
      Assert.That(batches.Select(batch => batch.SurfaceSystemName),
        Is.All.EqualTo("QueueStone"));
      Assert.That(batches.Select(batch => batch.MaterialColours),
        Is.EqualTo(new PathSurfaceColours?[] { westColours, eastColours }));
      Assert.That(batches.Select(batch => batch.SurfaceColours), Is.EqualTo(new[] {
        new PathSurfaceColours?[] { westColours },
        new PathSurfaceColours?[] { eastColours },
      }));
      Assert.That(batches.Select(batch => batch.Mesh.Vertices.Count),
        Is.All.EqualTo(4));
      Assert.That(batches.Select(batch => batch.Mesh.Indices.Count),
        Is.All.EqualTo(6));
      Assert.That(batches[0].Mesh.Vertices[0].Position.X,
        Is.LessThan(batches[1].Mesh.Vertices[0].Position.X));
    }
  }

  [Test]
  public void BuildBatches_SeparatesUnresolvedOrdinaryAndQueueSurfaces() {
    var terrain = new Terrain(2, 1, 0);
    var park = new Park(buildableWidth: 2, buildableHeight: 1);
    park.PathPlacements.Add(new PathPlacement(0, 0, new PathTile()));
    park.PathPlacements.Add(new PathPlacement(1, 0, new PathTile { IsQueue = true }));

    var batches = PathMeshBuilder.BuildBatches(
      park, terrain, Vector4.One, Vector4.UnitY, "Park Paths");

    Assert.That(batches.Select(batch => (batch.Kind, batch.SurfaceSystemName)), Is.EqualTo(new[] {
      (PathMaterialKind.Ordinary, (string?)null),
      (PathMaterialKind.Queue, (string?)null),
    }));
    Assert.That(batches.Select(batch => batch.Mesh.Name), Is.EqualTo(new[] {
      "Park Paths Ordinary Unresolved",
      "Park Paths Queue Unresolved",
    }));
  }

  [Test]
  public void Build_RejectsPathOutsideTerrain() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.Paths[(terrain.Width, 0)] = new PathTile();

    Assert.Throws<InvalidDataException>(new Action(() =>
      PathMeshBuilder.Build(park, terrain, Vector4.One, Vector4.UnitX)));
  }
}
