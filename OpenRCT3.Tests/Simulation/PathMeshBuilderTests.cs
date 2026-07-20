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
  public void Build_RejectsPathOutsideTerrain() {
    var terrain = new Terrain(1, 1, 0);
    var park = new Park(buildableWidth: 1, buildableHeight: 1);
    park.Paths[(terrain.Width, 0)] = new PathTile();

    Assert.Throws<InvalidDataException>(new Action(() =>
      PathMeshBuilder.Build(park, terrain, Vector4.One, Vector4.UnitX)));
  }
}
