// Scenery Geometry Builder
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Meshes;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>The exact catalog and material identity for one combined scenery mesh.</summary>
/// <remarks>
/// <see cref="OverlayPath"/> deliberately retains the placement's original DAT identity. Two
/// catalogs may contain identical FTX/TXS symbols, so geometry from different overlay roots must
/// never merge merely because its material references match.
/// </remarks>
public sealed record SceneryMaterialKey(
  string? OverlayPath,
  string? FtxRef,
  string? TxsRef,
  int SupportType,
  uint Transparency,
  uint TextureFlags,
  uint Sides
) {
  /// <summary>The placed object's three serialized flexible-colour selections.</summary>
  public SceneryFlexiColours FlexiColours { get; init; }
}

/// <summary>The three effective DAT palette selections that tint a flexible texture.</summary>
public readonly record struct SceneryFlexiColours(int First, int Second, int Third) {
  public const int PaletteSize = 32;

  /// <summary>
  /// Applies RCT3's DAT object-construction fallback: if any serialized index is outside 0..31,
  /// the whole group resolves to the default 1, 2, 3 triplet before cache lookup.
  /// </summary>
  public static SceneryFlexiColours FromSerialized(int first, int second, int third) =>
    first is >= 0 and < PaletteSize &&
    second is >= 0 and < PaletteSize &&
    third is >= 0 and < PaletteSize
      ? new SceneryFlexiColours(first, second, third)
      : new SceneryFlexiColours(1, 2, 3);
}

/// <summary>One deterministic, material-selectable combined scenery mesh.</summary>
public sealed record SceneryGeometryBatch(SceneryMaterialKey Key, Mesh Mesh);

/// <summary>Combined scenery geometry plus explicit placement outcome counts.</summary>
public sealed record SceneryGeometryBuildResult(
  IReadOnlyList<SceneryGeometryBatch> Batches,
  int PlacementCount,
  int HiddenPlacementCount,
  int UnresolvedPlacementCount,
  int ResolvedPlacementCount,
  int UnsupportedVisualPlacementCount,
  int RenderedPlacementCount,
  int SourceBatchInstanceCount,
  int VertexCount,
  int IndexCount
) {
  public int SkippedPlacementCount =>
    HiddenPlacementCount + UnresolvedPlacementCount + UnsupportedVisualPlacementCount;
}

/// <summary>Assembles resolved SID/SVD/SHS/BSH scenery into deterministic material batches.</summary>
public static class SceneryGeometryBuilder {
  /// <summary>
  /// Builds every visible, resolvable placement in park order. The lookup owns catalog selection;
  /// returning <c>null</c> marks that placement unresolved.
  /// </summary>
  public static SceneryGeometryBuildResult Build(
    Park park,
    Terrain terrain,
    Func<SceneryPlacement, ResolvedSceneryObject?> lookup
  ) => Build(
    park,
    terrain,
    lookup,
    StaticShapeMeshBuilder.BuildBatches,
    BoneShapeMeshBuilder.BuildBatches,
    SceneryGeometryBuildLimits.Default);

  internal static SceneryGeometryBuildResult Build(
    Park park,
    Terrain terrain,
    Func<SceneryPlacement, ResolvedSceneryObject?> lookup,
    Func<StaticShape, IReadOnlyList<StaticShapeMeshBatch>> adaptShape,
    SceneryGeometryBuildLimits limits
  ) => Build(
    park,
    terrain,
    lookup,
    adaptShape,
    BoneShapeMeshBuilder.BuildBatches,
    limits);

  internal static SceneryGeometryBuildResult Build(
    Park park,
    Terrain terrain,
    Func<SceneryPlacement, ResolvedSceneryObject?> lookup,
    Func<StaticShape, IReadOnlyList<StaticShapeMeshBatch>> adaptShape,
    Func<BoneShape, IReadOnlyList<StaticShapeMeshBatch>> adaptBoneShape,
    SceneryGeometryBuildLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(park);
    ArgumentNullException.ThrowIfNull(terrain);
    ArgumentNullException.ThrowIfNull(lookup);
    ArgumentNullException.ThrowIfNull(adaptShape);
    ArgumentNullException.ThrowIfNull(adaptBoneShape);
    limits.Validate();

    if (Convert.ToUInt64(park.SceneryPlacements.Count) > limits.MaximumPlacements)
      throw Invalid(
        $"placement count {park.SceneryPlacements.Count} exceeds " +
        $"the limit {limits.MaximumPlacements}");

    var shapeCache = new Dictionary<object, IReadOnlyList<StaticShapeMeshBatch>>(
      ReferenceEqualityComparer.Instance);
    var batchesByKey = new Dictionary<SceneryMaterialKey, MutableBatch>();
    var orderedBatches = new List<MutableBatch>();
    var hiddenCount = 0;
    var unresolvedCount = 0;
    var resolvedCount = 0;
    var unsupportedCount = 0;
    var renderedCount = 0;
    var sourceBatchCount = 0UL;
    var vertexCount = 0UL;
    var indexCount = 0UL;
    var outputMeshes = new List<Mesh>();
    var completed = false;

    try {
      foreach (var placement in park.SceneryPlacements) {
        if (placement.IsHidden) {
          hiddenCount++;
          continue;
        }
        if (!terrain.HasTile(placement.TileX, placement.TileY))
          throw Invalid(
            $"placement '{placement.ObjectKey}' at ({placement.TileX}, {placement.TileY}) " +
            "is outside the terrain grid");

        var resolved = lookup(placement);
        if (resolved == null) {
          unresolvedCount++;
          continue;
        }
        resolvedCount++;

        var selected = SelectShapeLod(placement, resolved);
        if (selected == null) {
          unsupportedCount++;
          continue;
        }

        if (!shapeCache.TryGetValue(selected.Shape, out var sourceBatches)) {
          sourceBatches = selected.Shape switch {
            StaticShape shape => adaptShape(shape),
            BoneShape shape => adaptBoneShape(shape),
            _ => throw Invalid(
              $"shape '{selected.ShapeName}' has unsupported model type " +
              $"'{selected.Shape.GetType().Name}'"),
          };
          if (sourceBatches == null)
            throw Invalid($"shape adapter returned null for '{selected.ShapeName}'");
          if (sourceBatches.Count == 0)
            throw Invalid($"shape adapter returned no batches for '{selected.ShapeName}'");
          shapeCache.Add(selected.Shape, sourceBatches);
        }

        var transform = CreatePlacementTransform(
          placement, resolved.Item, selected.Visual, terrain);
        foreach (var sourceBatch in sourceBatches) {
          Reserve(
            ref sourceBatchCount,
            1,
            limits.MaximumSourceBatchInstances,
            "source batch instances");
          ValidateSourceBatch(selected.ShapeName, sourceBatch);

          var key = new SceneryMaterialKey(
            placement.OverlayPath,
            sourceBatch.FtxRef,
            sourceBatch.TxsRef,
            sourceBatch.SupportType,
            sourceBatch.Transparency,
            sourceBatch.TextureFlags,
            sourceBatch.Sides) {
            FlexiColours = SceneryFlexiColours.FromSerialized(
              placement.FlexiColour0,
              placement.FlexiColour1,
              placement.FlexiColour2)
          };
          MutableBatch target;
          if (sourceBatch.Transparency == 2) {
            if (Convert.ToUInt64(orderedBatches.Count) >= limits.MaximumMaterialBatches)
              throw Invalid(
                $"material batch count exceeds the limit {limits.MaximumMaterialBatches}");
            target = new MutableBatch(key);
            orderedBatches.Add(target);
          } else if (!batchesByKey.TryGetValue(key, out var sharedTarget)) {
            if (Convert.ToUInt64(orderedBatches.Count) >= limits.MaximumMaterialBatches)
              throw Invalid(
                $"material batch count exceeds the limit {limits.MaximumMaterialBatches}");
            target = new MutableBatch(key);
            batchesByKey.Add(key, target);
            orderedBatches.Add(target);
          } else target = sharedTarget;

          AddTransformed(
            selected.ShapeName,
            sourceBatch,
            transform,
            target,
            ref vertexCount,
            ref indexCount,
            limits);
        }
        renderedCount++;
      }

      var output = new SceneryGeometryBatch[orderedBatches.Count];
      for (var index = 0; index < orderedBatches.Count; index++) {
        var batch = orderedBatches[index];
        var mesh = new Mesh(batch.Vertices, batch.Indices) {
          Name = batch.Key.FtxRef ?? "Untextured scenery"
        };
        outputMeshes.Add(mesh);
        output[index] = new SceneryGeometryBatch(batch.Key, mesh);
      }

      var result = new SceneryGeometryBuildResult(
        output,
        park.SceneryPlacements.Count,
        hiddenCount,
        unresolvedCount,
        resolvedCount,
        unsupportedCount,
        renderedCount,
        Convert.ToInt32(sourceBatchCount),
        Convert.ToInt32(vertexCount),
        Convert.ToInt32(indexCount));
      DisposeAdaptedMeshes(shapeCache.Values);
      completed = true;
      return result;
    } finally {
      if (!completed) {
        try {
          DisposeAdaptedMeshes(shapeCache.Values);
        } finally {
          DisposeMeshes(outputMeshes);
        }
      }
    }
  }

  /// <summary>
  /// Returns the world-space center of a scenery tile.
  /// </summary>
  /// <remarks>
  /// RCT3's position-type-specific anchors are applied by the placement transform. This public
  /// helper retains the useful single-tile center calculation for callers that do not have a SID.
  /// </remarks>
  public static Vector2 CalculateTileAnchor(Terrain terrain, int tileX, int tileY) {
    ArgumentNullException.ThrowIfNull(terrain);
    if (!terrain.HasTile(tileX, tileY))
      throw new ArgumentOutOfRangeException(
        nameof(tileX),
        $"Scenery tile ({tileX}, {tileY}) is outside the terrain grid.");

    return CalculateWorldAnchor(terrain, tileX, tileY, new PlacementAnchor(0.5d, 0.5d));
  }

  private static ResolvedShapeSelection? SelectShapeLod(
    SceneryPlacement placement,
    ResolvedSceneryObject resolved
  ) {
    if (resolved.Item == null)
      throw Invalid($"placement '{placement.ObjectKey}' resolved to a null SID");
    if (resolved.StaticLods == null)
      throw Invalid($"SID '{resolved.Item.Name}' resolved to a null static-LOD list");
    if (resolved.BoneLods == null)
      throw Invalid($"SID '{resolved.Item.Name}' resolved to a null bone-LOD list");
    if (string.IsNullOrWhiteSpace(resolved.Item.Name))
      throw Invalid("resolved SID has no name");
    if (!string.Equals(
          placement.ObjectKey,
          resolved.Item.Name,
          StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"placement '{placement.ObjectKey}' resolved to SID '{resolved.Item.Name}'");
    ValidateItem(resolved.Item);

    if (resolved.Item.VisualRefs == null || resolved.Item.VisualRefs.Count == 0)
      throw Invalid($"SID '{resolved.Item.Name}' declares no visual alternatives");

    var declaredNames = new List<string>(resolved.Item.VisualRefs.Count);
    var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var visualRef in resolved.Item.VisualRefs) {
      var name = ParseTaggedReference(visualRef, "svd", resolved.Item.Name);
      if (!uniqueNames.Add(name))
        throw Invalid($"SID '{resolved.Item.Name}' declares duplicate visual '{visualRef}'");
      declaredNames.Add(name);
    }

    foreach (var candidate in resolved.StaticLods) {
      if (candidate == null || candidate.Visual == null ||
          candidate.Lod == null || candidate.Shape == null)
        throw Invalid($"SID '{resolved.Item.Name}' has a null resolved static LOD");
      if (!uniqueNames.Contains(candidate.Visual.Name))
        throw Invalid(
          $"SID '{resolved.Item.Name}' resolved undeclared visual '{candidate.Visual.Name}'");
    }
    foreach (var candidate in resolved.BoneLods) {
      if (candidate == null || candidate.Visual == null ||
          candidate.Lod == null || candidate.Shape == null)
        throw Invalid($"SID '{resolved.Item.Name}' has a null resolved bone LOD");
      if (!uniqueNames.Contains(candidate.Visual.Name))
        throw Invalid(
          $"SID '{resolved.Item.Name}' resolved undeclared visual '{candidate.Visual.Name}'");
    }

    foreach (var declaredName in declaredNames) {
      var staticCandidates = resolved.StaticLods
        .Where(candidate => string.Equals(
          candidate.Visual.Name,
          declaredName,
          StringComparison.OrdinalIgnoreCase))
        .ToArray();
      var boneCandidates = resolved.BoneLods
        .Where(candidate => string.Equals(
          candidate.Visual.Name,
          declaredName,
          StringComparison.OrdinalIgnoreCase))
        .ToArray();
      if (staticCandidates.Length == 0 && boneCandidates.Length == 0) continue;

      var visual = staticCandidates.Length > 0
        ? staticCandidates[0].Visual
        : boneCandidates[0].Visual;
      if (staticCandidates.Any(candidate => !ReferenceEquals(candidate.Visual, visual)) ||
          boneCandidates.Any(candidate => !ReferenceEquals(candidate.Visual, visual)))
        throw Invalid(
          $"SID '{resolved.Item.Name}' has conflicting SVD objects named '{declaredName}'");
      ValidateVisual(visual);
      var shapeLods = visual.Lods.Where(lod =>
        lod.Type is SvdLodType.StaticShape or SvdLodType.BoneShape).ToArray();
      var candidateCount = staticCandidates.Length + boneCandidates.Length;
      if (shapeLods.Length != candidateCount)
        throw Invalid(
          $"SVD '{visual.Name}' resolved {candidateCount} supported shape LODs, expected " +
          $"{shapeLods.Length}");

      var ordered = new List<ResolvedShapeSelection>(shapeLods.Length);
      var staticIndex = 0;
      var boneIndex = 0;
      foreach (var expected in shapeLods) {
        switch (expected.Type) {
          case SvdLodType.StaticShape:
            if (staticIndex >= staticCandidates.Length)
              throw Invalid(
                $"SVD '{visual.Name}' is missing resolved static LOD '{expected.Name}'");
            var staticCandidate = staticCandidates[staticIndex++];
            if (!ReferenceEquals(staticCandidate.Lod, expected) &&
                staticCandidate.Lod != expected)
              throw Invalid(
                $"SVD '{visual.Name}' static LOD order does not match its serialized order");
            var staticShapeName = ParseTaggedReference(
              expected.StaticShapeRef,
              "shs",
              $"SVD '{visual.Name}' LOD '{expected.Name}'");
            if (!string.Equals(
                  staticShapeName,
                  staticCandidate.Shape.Name,
                  StringComparison.OrdinalIgnoreCase))
              throw Invalid(
                $"SVD '{visual.Name}' LOD '{expected.Name}' resolved shape " +
                $"'{staticCandidate.Shape.Name}', expected '{staticShapeName}'");
            ordered.Add(new ResolvedShapeSelection(
              visual, expected, staticShapeName, staticCandidate.Shape));
            break;
          case SvdLodType.BoneShape:
            if (boneIndex >= boneCandidates.Length)
              throw Invalid(
                $"SVD '{visual.Name}' is missing resolved bone LOD '{expected.Name}'");
            var boneCandidate = boneCandidates[boneIndex++];
            if (!ReferenceEquals(boneCandidate.Lod, expected) && boneCandidate.Lod != expected)
              throw Invalid(
                $"SVD '{visual.Name}' bone LOD order does not match its serialized order");
            var boneShapeName = ParseTaggedReference(
              expected.BoneShapeRef,
              "bsh",
              $"SVD '{visual.Name}' LOD '{expected.Name}'");
            if (!string.Equals(
                  boneShapeName,
                  boneCandidate.Shape.Name,
                  StringComparison.OrdinalIgnoreCase))
              throw Invalid(
                $"SVD '{visual.Name}' LOD '{expected.Name}' resolved shape " +
                $"'{boneCandidate.Shape.Name}', expected '{boneShapeName}'");
            ordered.Add(new ResolvedShapeSelection(
              visual, expected, boneShapeName, boneCandidate.Shape));
            break;
        }
      }
      if (staticIndex != staticCandidates.Length || boneIndex != boneCandidates.Length)
        throw Invalid($"SVD '{visual.Name}' resolved shape LOD types out of serialized order");

      // SID visual references are ordered alternatives, not additive components. Use exactly the
      // first supported alternative and its first serialized supported shape LOD.
      return ordered[0];
    }

    return null;
  }

  private static void ValidateItem(SceneryItem item) {
    if (!float.IsFinite(item.PositionX) || !float.IsFinite(item.PositionY) ||
        !float.IsFinite(item.PositionZ))
      throw Invalid($"SID '{item.Name}' position offset is non-finite");
    if (!Enum.IsDefined(item.PositionType))
      throw Invalid($"SID '{item.Name}' position type {item.PositionType} is unsupported");
    if (item.SquaresX == 0 || item.SquaresZ == 0)
      throw Invalid($"SID '{item.Name}' footprint dimensions must be positive");
    if (item.SquaresX > int.MaxValue || item.SquaresZ > int.MaxValue)
      throw Invalid($"SID '{item.Name}' footprint dimensions exceed the simulation range");
  }

  private static void ValidateVisual(SceneryItemVisual visual) {
    if (string.IsNullOrWhiteSpace(visual.Name)) throw Invalid("resolved SVD has no name");
    if (visual.Lods == null) throw Invalid($"SVD '{visual.Name}' has a null LOD list");
    if (!float.IsFinite(visual.Scale) || visual.Scale < 0f)
      throw Invalid($"SVD '{visual.Name}' scale variation is invalid");
    foreach (var lod in visual.Lods) {
      if (lod == null) throw Invalid($"SVD '{visual.Name}' has a null LOD");
      if (!Enum.IsDefined(lod.Type))
        throw Invalid($"SVD '{visual.Name}' LOD '{lod.Name}' has unsupported type {lod.Type}");
    }
    // SVD.scale is a placement-time variation amplitude whose normal baseline is zero. No decoded
    // deterministic placement-random value exists yet, so applying either Scale or 1 + Scale would
    // invent behavior. Geometry therefore stays at effective scale 1.0 for now.
  }

  private static PlacementTransform CreatePlacementTransform(
    SceneryPlacement placement,
    SceneryItem item,
    SceneryItemVisual visual,
    Terrain terrain
  ) {
    ValidateVisual(visual);
    if (!float.IsFinite(placement.HeightAdjust))
      throw Invalid($"placement '{placement.ObjectKey}' height adjustment is non-finite");
    if (!Enum.IsDefined(placement.Rotation))
      throw Invalid(
        $"placement '{placement.ObjectKey}' rotation {placement.Rotation} is unsupported");

    var direction = ResolveDirection(placement);
    var normalizedAnchor = CalculateNormalizedAnchor(placement, item, direction);
    var anchor = CalculateWorldAnchor(
      terrain, placement.TileX, placement.TileY, normalizedAnchor);
    var height = PlacementHeight(placement, item, terrain, normalizedAnchor, direction);
    var verticalSlope = CalculateVerticalSlope(placement, item, terrain, direction);
    // SID PositionX/Y/Z are collision and placement metadata in the pinned importer. No source
    // proves they translate the rendered SVD/SHS, so preserve them on the decoded item but do not
    // invent a visual transform here.
    var translation = new Vector3(anchor, height);
    if (!IsFinite(translation) || !IsFinite(verticalSlope))
      throw Invalid($"placement '{placement.ObjectKey}' translation is non-finite");
    return new PlacementTransform(direction, translation, verticalSlope);
  }

  private static float PlacementHeight(
    SceneryPlacement placement,
    SceneryItem item,
    Terrain terrain,
    PlacementAnchor anchor,
    int direction
  ) {
    var usesAbsoluteHeight = UsesAbsoluteHeight(placement, item);
    if (usesAbsoluteHeight && !placement.SerializedHeight.HasValue)
      throw Invalid(
        $"placement '{placement.ObjectKey}' uses absolute height without a serialized value");

    double worldHeight;
    if (usesAbsoluteHeight) {
      worldHeight = Convert.ToDouble(placement.SerializedHeight!.Value);
    } else {
      var sampledHeight = SampleTerrainHeight(
        placement, item, terrain, anchor, direction);
      // RCT3.exe 0x00b25ba6 rounds relative terrain upward to a whole world unit. SID SmoothHeight
      // enters the second dispatch at 0x00b25c0c and overwrites that value with the raw sample.
      var groundHeight = item.Flags.HasFlag(SidFlags.SmoothHeight)
        ? sampledHeight
        : Math.Ceiling(sampledHeight);
      worldHeight = groundHeight + placement.HeightOffset;
    }
    // HEIGHTADJUST is applied by the caller after either original placement routine returns.
    worldHeight += placement.HeightAdjust;
    if (!double.IsFinite(worldHeight) ||
        worldHeight < -float.MaxValue || worldHeight > float.MaxValue)
      throw Invalid($"placement '{placement.ObjectKey}' height is outside the render range");
    return Convert.ToSingle(worldHeight);
  }

  private static bool UsesAbsoluteHeight(
    SceneryPlacement placement,
    SceneryItem item
  ) => placement.ForceAbsoluteHeight || item.Flags.HasFlag(SidFlags.GroundChange);

  private static int ResolveDirection(SceneryPlacement placement) {
    var direction = placement.SerializedDirection;
    if (direction is < 0 or > 3)
      throw Invalid(
        $"placement '{placement.ObjectKey}' serialized direction " +
        $"{direction} is unsupported");
    return direction;
  }

  private static PlacementAnchor CalculateNormalizedAnchor(
    SceneryPlacement placement,
    SceneryItem item,
    int direction
  ) => item.PositionType switch {
    SidPosition.TileFull or SidPosition.PathCenter =>
      CalculateFootprintCenter(item, direction),
    SidPosition.PathEdgeInner =>
      CalculateEdgeAnchor(direction, 0.15d, 0.85d),
    SidPosition.PathEdgeOuter or SidPosition.PathEdgeJoin =>
      CalculateEdgeAnchor(direction, 0.05d, 0.95d),
    SidPosition.Wall or SidPosition.Corner =>
      CalculateEdgeAnchor(direction, 0d, 1d),
    SidPosition.TileQuarter => CalculateQuarterAnchor(placement),
    SidPosition.TileHalf => CalculateHalfAnchor(direction),
    _ => throw Invalid($"SID position type {item.PositionType} is unsupported"),
  };

  private static PlacementAnchor CalculateFootprintCenter(
    SceneryItem item,
    int direction
  ) {
    var (width, depth) = RotatedFootprint(item, direction);
    return new PlacementAnchor(
      Convert.ToDouble(width) * 0.5d,
      Convert.ToDouble(depth) * 0.5d);
  }

  private static PlacementAnchor CalculateEdgeAnchor(
    int direction,
    double near,
    double far
  ) => direction switch {
    0 => new PlacementAnchor(near, 0.5d),
    1 => new PlacementAnchor(0.5d, far),
    2 => new PlacementAnchor(far, 0.5d),
    3 => new PlacementAnchor(0.5d, near),
    _ => throw Invalid($"serialized direction {direction} is unsupported"),
  };

  private static PlacementAnchor CalculateQuarterAnchor(SceneryPlacement placement) =>
    placement.Corner switch {
      0 => new PlacementAnchor(0.25d, 0.75d),
      1 => new PlacementAnchor(0.75d, 0.75d),
      2 => new PlacementAnchor(0.25d, 0.25d),
      3 => new PlacementAnchor(0.75d, 0.25d),
      _ => throw Invalid(
        $"placement '{placement.ObjectKey}' quarter corner {placement.Corner} is unsupported"),
    };

  private static PlacementAnchor CalculateHalfAnchor(int direction) => direction switch {
    0 => new PlacementAnchor(0.25d, 0.5d),
    1 => new PlacementAnchor(0.5d, 0.75d),
    2 => new PlacementAnchor(0.75d, 0.5d),
    3 => new PlacementAnchor(0.5d, 0.25d),
    _ => throw Invalid($"serialized direction {direction} is unsupported"),
  };

  private static (uint Width, uint Depth) RotatedFootprint(
    SceneryItem item,
    int direction
  ) => direction is 1 or 3
    ? (item.SquaresZ, item.SquaresX)
    : (item.SquaresX, item.SquaresZ);

  private static Vector2 CalculateWorldAnchor(
    Terrain terrain,
    int tileX,
    int tileY,
    PlacementAnchor anchor
  ) {
    if (!double.IsFinite(anchor.U) || !double.IsFinite(anchor.V) ||
        anchor.U < 0d || anchor.V < 0d)
      throw Invalid($"tile ({tileX}, {tileY}) anchor is invalid");
    var x = Convert.ToDouble(terrain.Origin.X) +
      ((Convert.ToDouble(tileX) + anchor.U) * terrain.TileSize.X);
    var y = Convert.ToDouble(terrain.Origin.Y) +
      ((Convert.ToDouble(tileY) + anchor.V) * terrain.TileSize.Y);
    if (!double.IsFinite(x) || !double.IsFinite(y) ||
        x < -float.MaxValue || x > float.MaxValue ||
        y < -float.MaxValue || y > float.MaxValue)
      throw Invalid($"tile ({tileX}, {tileY}) anchor is non-finite");
    return new Vector2(Convert.ToSingle(x), Convert.ToSingle(y));
  }

  private static double SampleTerrainHeight(
    SceneryPlacement placement,
    SceneryItem item,
    Terrain terrain,
    PlacementAnchor anchor,
    int direction
  ) {
    if (UsesFenceCornerAverage(item))
      return SampleTerrainCornerAverage(terrain, placement.TileX, placement.TileY);
    if (item.PositionType == SidPosition.Corner && UsesRevisedSmoothFence(item))
      return SampleTerrainEdge(terrain, placement.TileX, placement.TileY, direction);
    return item.PositionType switch {
      SidPosition.TileFull or SidPosition.TileQuarter or SidPosition.TileHalf or
        SidPosition.PathCenter => SampleTerrainSurface(
          terrain, placement.TileX, placement.TileY, anchor),
      SidPosition.PathEdgeInner or SidPosition.PathEdgeOuter or SidPosition.Wall or
        SidPosition.PathEdgeJoin => SampleTerrainEdge(
          terrain, placement.TileX, placement.TileY, direction),
      SidPosition.Corner => SampleTerrainCorner(
        terrain, placement.TileX, placement.TileY, direction),
      _ => throw Invalid($"SID position type {item.PositionType} is unsupported"),
    };
  }

  // RCT3.exe 0x00b25eef gives Wild's 1x1 SmoothHeight fence pieces a four-corner average.
  private static bool UsesFenceCornerAverage(SceneryItem item) =>
    item.PositionType == SidPosition.TileFull &&
    item.StructureVersion >= 2 &&
    item.Flags.HasFlag(SidFlags.SmoothHeight) &&
    item.Flags.HasFlag(SidFlags.Fence) &&
    item.SquaresX == 1 &&
    item.SquaresZ == 1;

  private static bool UsesRevisedSmoothFence(SceneryItem item) =>
    item.StructureVersion >= 2 &&
    item.Flags.HasFlag(SidFlags.SmoothHeight) &&
    item.Flags.HasFlag(SidFlags.Fence);

  private static Vector2 CalculateVerticalSlope(
    SceneryPlacement placement,
    SceneryItem item,
    Terrain terrain,
    int direction
  ) {
    // RCT3.exe uses a separate explicit-height routine at 0x00b264a0. Only the terrain-relative
    // routine at 0x00b256e0 reaches the SmoothHeight matrix dispatch.
    if (UsesAbsoluteHeight(placement, item) ||
        !item.Flags.HasFlag(SidFlags.SmoothHeight))
      return Vector2.Zero;

    var isEdgePosition = item.PositionType is
      SidPosition.PathEdgeInner or
      SidPosition.PathEdgeOuter or
      SidPosition.Wall or
      SidPosition.PathEdgeJoin;
    var usesEdgeSlope = isEdgePosition &&
      (item.StructureVersion >= 2
        ? item.Flags.HasFlag(SidFlags.Fence)
        : item.PositionType == SidPosition.Wall);
    var usesCornerSlope = item.PositionType == SidPosition.Corner &&
      UsesRevisedSmoothFence(item);
    var usesFullSlope = UsesFenceCornerAverage(item);
    if (!usesEdgeSlope && !usesCornerSlope && !usesFullSlope)
      return Vector2.Zero;

    var heights = ReadTerrainCornerHeights(
      terrain, placement.TileX, placement.TileY);
    var rct3Slope = item.PositionType switch {
      SidPosition.PathEdgeInner or
      SidPosition.PathEdgeOuter or
      SidPosition.Wall or
      SidPosition.PathEdgeJoin => CalculateEdgeSlope(heights, direction),
      SidPosition.Corner => CalculateCornerSlope(heights, direction),
      SidPosition.TileFull => CalculateFullTileSlope(heights, direction),
      _ => Rct3PlacementSlope.None,
    };

    // The executable writes these coefficients to row-vector matrix elements m01 and m21. The
    // native-to-park coordinate bridge preserves native X as OpenRCT3 X and maps native Z to OpenRCT3
    // Y, so the equivalent local height plane is z += m01*x + m21*y. The placement quarter-turn then
    // carries that plane into its physical world orientation.
    var result = new Vector2(rct3Slope.M01, rct3Slope.M21);
    if (!IsFinite(result))
      throw Invalid($"placement '{placement.ObjectKey}' terrain slope is non-finite");
    return result;
  }

  private static Rct3PlacementSlope CalculateEdgeSlope(
    TerrainCornerHeights heights,
    int direction
  ) {
    var (first, second) = direction switch {
      0 => (heights.SouthWest, heights.NorthWest),
      1 => (heights.NorthWest, heights.NorthEast),
      2 => (heights.NorthEast, heights.SouthEast),
      3 => (heights.SouthEast, heights.SouthWest),
      _ => throw Invalid($"serialized direction {direction} is unsupported"),
    };
    return new Rct3PlacementSlope(0f, (first - second) * 0.25f);
  }

  private static Rct3PlacementSlope CalculateCornerSlope(
    TerrainCornerHeights heights,
    int direction
  ) => direction switch {
    0 => new Rct3PlacementSlope(
      (heights.NorthWest - heights.NorthEast) * 0.25f,
      (heights.SouthWest - heights.NorthWest) * 0.25f),
    1 => new Rct3PlacementSlope(
      (heights.NorthEast - heights.SouthEast) * 0.25f,
      (heights.NorthWest - heights.NorthEast) * 0.25f),
    2 => new Rct3PlacementSlope(
      (heights.SouthEast - heights.SouthWest) * 0.25f,
      (heights.NorthEast - heights.SouthEast) * 0.25f),
    3 => new Rct3PlacementSlope(
      (heights.SouthWest - heights.NorthWest) * 0.25f,
      (heights.SouthEast - heights.SouthWest) * 0.25f),
    _ => throw Invalid($"serialized direction {direction} is unsupported"),
  };

  private static Rct3PlacementSlope CalculateFullTileSlope(
    TerrainCornerHeights heights,
    int direction
  ) => direction switch {
    0 => new Rct3PlacementSlope(
      (heights.SouthWest + heights.NorthWest -
        heights.SouthEast - heights.NorthEast) * 0.125f,
      (heights.SouthWest + heights.SouthEast -
        heights.NorthWest - heights.NorthEast) * 0.125f),
    1 => new Rct3PlacementSlope(
      (heights.NorthWest + heights.NorthEast -
        heights.SouthWest - heights.SouthEast) * 0.125f,
      (heights.SouthWest + heights.NorthWest -
        heights.SouthEast - heights.NorthEast) * 0.125f),
    2 => new Rct3PlacementSlope(
      (heights.SouthEast + heights.NorthEast -
        heights.SouthWest - heights.NorthWest) * 0.125f,
      (heights.NorthWest + heights.NorthEast -
        heights.SouthWest - heights.SouthEast) * 0.125f),
    3 => new Rct3PlacementSlope(
      (heights.SouthWest + heights.SouthEast -
        heights.NorthWest - heights.NorthEast) * 0.125f,
      (heights.SouthEast + heights.NorthEast -
        heights.SouthWest - heights.NorthWest) * 0.125f),
    _ => throw Invalid($"serialized direction {direction} is unsupported"),
  };

  private static TerrainCornerHeights ReadTerrainCornerHeights(
    Terrain terrain,
    int tileX,
    int tileY
  ) => new(
    Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(tileX, tileY, TerrainCornerSlot.SouthWest).Height),
    Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(tileX, tileY, TerrainCornerSlot.SouthEast).Height),
    Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(tileX, tileY, TerrainCornerSlot.NorthWest).Height),
    Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(tileX, tileY, TerrainCornerSlot.NorthEast).Height));

  private static double SampleTerrainCornerAverage(
    Terrain terrain,
    int tileX,
    int tileY
  ) {
    var southWest = Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(tileX, tileY, TerrainCornerSlot.SouthWest).Height);
    var southEast = Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(tileX, tileY, TerrainCornerSlot.SouthEast).Height);
    var northWest = Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(tileX, tileY, TerrainCornerSlot.NorthWest).Height);
    var northEast = Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(tileX, tileY, TerrainCornerSlot.NorthEast).Height);
    return Convert.ToDouble(
      (southWest + southEast + northWest + northEast) * 0.25f);
  }

  private static double SampleTerrainSurface(
    Terrain terrain,
    int tileX,
    int tileY,
    PlacementAnchor anchor
  ) {
    var absoluteX = Convert.ToDouble(tileX) + anchor.U;
    var absoluteY = Convert.ToDouble(tileY) + anchor.V;
    if (!double.IsFinite(absoluteX) || !double.IsFinite(absoluteY) ||
        absoluteX < int.MinValue || absoluteX > int.MaxValue ||
        absoluteY < int.MinValue || absoluteY > int.MaxValue)
      throw Invalid($"terrain sample at ({absoluteX}, {absoluteY}) is outside the grid");

    var sampleX = Convert.ToInt32(Math.Floor(absoluteX));
    var sampleY = Convert.ToInt32(Math.Floor(absoluteY));
    if (!terrain.HasTile(sampleX, sampleY))
      throw Invalid($"terrain sample at ({absoluteX}, {absoluteY}) is outside the grid");
    var u = Convert.ToSingle(absoluteX - sampleX);
    var v = Convert.ToSingle(absoluteY - sampleY);
    var southWest = Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(sampleX, sampleY, TerrainCornerSlot.SouthWest).Height);
    var southEast = Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(sampleX, sampleY, TerrainCornerSlot.SouthEast).Height);
    var northWest = Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(sampleX, sampleY, TerrainCornerSlot.NorthWest).Height);
    var northEast = Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(sampleX, sampleY, TerrainCornerSlot.NorthEast).Height);

    // RCT3.exe 0x00b259c2 samples the same SouthEast-to-NorthWest split rendered by terrain.
    var height = u + v <= 1f
      ? (southWest * (1f - u - v)) + (southEast * u) + (northWest * v)
      : (northEast * (u + v - 1f)) +
        (northWest * (1f - u)) +
        (southEast * (1f - v));
    return Convert.ToDouble(height);
  }

  private static double SampleTerrainEdge(
    Terrain terrain,
    int tileX,
    int tileY,
    int direction
  ) {
    var edge = direction switch {
      0 => Edge.West,
      1 => Edge.North,
      2 => Edge.East,
      3 => Edge.South,
      _ => throw Invalid($"serialized direction {direction} is unsupported"),
    };
    var (first, second) = terrain.GetEdgeCornerHeights(tileX, tileY, edge);
    var firstHeight = Terrain.CornerHeightToWorldZ(first);
    var secondHeight = Terrain.CornerHeightToWorldZ(second);
    return Convert.ToDouble((firstHeight + secondHeight) * 0.5f);
  }

  private static double SampleTerrainCorner(
    Terrain terrain,
    int tileX,
    int tileY,
    int direction
  ) {
    var slot = direction switch {
      0 => TerrainCornerSlot.NorthWest,
      1 => TerrainCornerSlot.NorthEast,
      2 => TerrainCornerSlot.SouthEast,
      3 => TerrainCornerSlot.SouthWest,
      _ => throw Invalid($"serialized direction {direction} is unsupported"),
    };
    return Convert.ToDouble(Terrain.CornerHeightToWorldZ(
      terrain.GetCorner(tileX, tileY, slot).Height));
  }

  private static void ValidateSourceBatch(
    string shapeName,
    StaticShapeMeshBatch batch
  ) {
    if (batch == null) throw Invalid($"shape adapter returned a null batch for '{shapeName}'");
    if (batch.Mesh == null)
      throw Invalid($"shape adapter returned a null mesh for '{shapeName}'");
    if (batch.Mesh.Vertices == null || batch.Mesh.Vertices.Count == 0)
      throw Invalid($"shape '{shapeName}' batch {batch.SourceMeshIndex} has no vertices");
    if (batch.Mesh.Indices == null || batch.Mesh.Indices.Count == 0 ||
        batch.Mesh.Indices.Count % 3 != 0)
      throw Invalid(
        $"shape '{shapeName}' batch {batch.SourceMeshIndex} is not a triangle list");
    ValidateOptionalReference(batch.FtxRef, "ftx", shapeName, batch.SourceMeshIndex);
    ValidateOptionalReference(batch.TxsRef, "txs", shapeName, batch.SourceMeshIndex);
    if (batch.Transparency > 2)
      throw Invalid(
        $"shape '{shapeName}' batch {batch.SourceMeshIndex} has invalid transparency " +
        $"{batch.Transparency}");
    if (batch.Sides is not 1 and not 3)
      throw Invalid(
        $"shape '{shapeName}' batch {batch.SourceMeshIndex} has invalid sides {batch.Sides}");
  }

  private static void AddTransformed(
    string shapeName,
    StaticShapeMeshBatch sourceBatch,
    PlacementTransform transform,
    MutableBatch target,
    ref ulong vertexCount,
    ref ulong indexCount,
    SceneryGeometryBuildLimits limits
  ) {
    var source = sourceBatch.Mesh;
    Reserve(
      ref vertexCount,
      Convert.ToUInt64(source.Vertices.Count),
      limits.MaximumVertices,
      "vertices");
    Reserve(
      ref indexCount,
      Convert.ToUInt64(source.Indices.Count),
      limits.MaximumIndices,
      "indices");
    var baseVertex = Convert.ToUInt64(target.Vertices.Count);
    if (baseVertex + Convert.ToUInt64(source.Vertices.Count) > uint.MaxValue)
      throw Invalid($"material batch '{target.Key.FtxRef}' exceeds the 32-bit vertex range");

    var vertexIndex = 0;
    foreach (var vertex in source.Vertices) {
      if (!IsFinite(vertex.Position) || !IsFinite(vertex.Normal) ||
          !IsFinite(vertex.TexCoord) || !IsFinite(vertex.Color))
        throw Invalid(
          $"shape '{shapeName}' batch {sourceBatch.SourceMeshIndex} vertex " +
          $"{vertexIndex} is non-finite");
      var position = TransformPosition(vertex.Position, transform);
      var normal = TransformNormal(vertex.Normal, transform);
      if (!IsFinite(position) || !IsFinite(normal))
        throw Invalid(
          $"shape '{shapeName}' batch {sourceBatch.SourceMeshIndex} vertex " +
          $"{vertexIndex} transforms to a non-finite value");
      target.Vertices.Add(new Vertex {
        Position = position,
        Normal = normal,
        TexCoord = vertex.TexCoord,
        Color = vertex.Color
      });
      vertexIndex++;
    }

    var indexOffset = 0;
    foreach (var sourceIndex in source.Indices) {
      if (sourceIndex >= Convert.ToUInt32(source.Vertices.Count))
        throw Invalid(
          $"shape '{shapeName}' batch {sourceBatch.SourceMeshIndex} index {indexOffset} " +
          $"references vertex {sourceIndex}, but only {source.Vertices.Count} exist");
      var combined = baseVertex + sourceIndex;
      if (combined > uint.MaxValue)
        throw Invalid($"material batch '{target.Key.FtxRef}' index exceeds 32-bit range");
      target.Indices.Add(Convert.ToUInt32(combined));
      indexOffset++;
    }
  }

  private static Vector3 TransformPosition(
    Vector3 value,
    PlacementTransform transform
  ) {
    var sloped = new Vector3(
      value.X,
      value.Y,
      value.Z +
        (transform.VerticalSlope.X * value.X) +
        (transform.VerticalSlope.Y * value.Y));
    return Rotate(sloped, transform.Direction) + transform.Translation;
  }

  private static Vector3 TransformNormal(
    Vector3 value,
    PlacementTransform transform
  ) {
    if (transform.VerticalSlope == Vector2.Zero)
      return Rotate(value, transform.Direction);

    // Position uses A = [[R,0],[slope^T,1]]. Normals therefore use A^-T: subtract the
    // local slope times normal Z, then apply the same horizontal quarter-turn.
    var adjusted = new Vector3(
      value.X - (transform.VerticalSlope.X * value.Z),
      value.Y - (transform.VerticalSlope.Y * value.Z),
      value.Z);
    var rotated = Rotate(adjusted, transform.Direction);
    if (!IsFinite(rotated)) return rotated;

    var lengthSquared =
      (Convert.ToDouble(rotated.X) * rotated.X) +
      (Convert.ToDouble(rotated.Y) * rotated.Y) +
      (Convert.ToDouble(rotated.Z) * rotated.Z);
    if (lengthSquared == 0d) return Vector3.Zero;
    var length = Math.Sqrt(lengthSquared);
    return new Vector3(
      Convert.ToSingle(Convert.ToDouble(rotated.X) / length),
      Convert.ToSingle(Convert.ToDouble(rotated.Y) / length),
      Convert.ToSingle(Convert.ToDouble(rotated.Z) / length));
  }

  private static Vector3 Rotate(Vector3 value, int direction) => direction switch {
    // RCT3.exe's scenery routine uses 0 West, 1 North, 2 East, 3 South and constructs
    // -(direction + 2) * pi/2. This differs from path-record direction decoding, so use the
    // preserved DAT ordinal rather than Edge.
    0 => new Vector3(-value.X, -value.Y, value.Z),
    1 => new Vector3(-value.Y, value.X, value.Z),
    2 => value,
    3 => new Vector3(value.Y, -value.X, value.Z),
    _ => throw Invalid($"scenery serialized direction {direction} is unsupported"),
  };

  private static string ParseTaggedReference(
    string? reference,
    string expectedTag,
    string owner
  ) {
    if (string.IsNullOrWhiteSpace(reference))
      throw Invalid($"{owner} has an empty {expectedTag} reference");
    var separator = reference.LastIndexOf(':');
    if (separator <= 0 || separator == reference.Length - 1)
      throw Invalid($"{owner} has malformed reference '{reference}'");
    var tag = reference[(separator + 1)..];
    if (!string.Equals(tag, expectedTag, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{owner} references '{reference}', expected the '{expectedTag}' resource type");
    return reference[..separator];
  }

  private static void ValidateOptionalReference(
    string? reference,
    string expectedTag,
    string shapeName,
    int meshIndex
  ) {
    if (reference == null) return;
    ParseTaggedReference(
      reference,
      expectedTag,
      $"shape '{shapeName}' batch {meshIndex}");
  }

  private static void Reserve(
    ref ulong total,
    ulong addition,
    ulong maximum,
    string description
  ) {
    if (addition > maximum || total > maximum - addition)
      throw Invalid($"aggregate {description} exceed the limit {maximum}");
    total += addition;
  }

  private static void DisposeAdaptedMeshes(
    IEnumerable<IReadOnlyList<StaticShapeMeshBatch>> adaptedShapes
  ) {
    var meshes = new List<Mesh>();
    foreach (var batches in adaptedShapes) {
      foreach (var batch in batches) {
        if (batch?.Mesh != null) meshes.Add(batch.Mesh);
      }
    }
    DisposeMeshes(meshes);
  }

  private static void DisposeMeshes(IEnumerable<Mesh> meshes) {
    var disposed = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    foreach (var mesh in meshes) {
      if (disposed.Add(mesh)) mesh.Dispose();
    }
  }

  private static bool IsFinite(Vector2 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static bool IsFinite(Vector4 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

  private static InvalidDataException Invalid(string message) =>
    new($"Scenery geometry is malformed: {message}.");

  private sealed record ResolvedShapeSelection(
    SceneryItemVisual Visual,
    SceneryItemVisualLod Lod,
    string ShapeName,
    object Shape
  );

  private sealed class MutableBatch(SceneryMaterialKey key) {
    public SceneryMaterialKey Key { get; } = key;
    public List<Vertex> Vertices { get; } = [];
    public List<uint> Indices { get; } = [];
  }

  private readonly record struct PlacementAnchor(double U, double V);

  private readonly record struct PlacementTransform(
    int Direction,
    Vector3 Translation,
    Vector2 VerticalSlope);

  private readonly record struct Rct3PlacementSlope(float M01, float M21) {
    public static Rct3PlacementSlope None { get; } = new(0f, 0f);
  }

  private readonly record struct TerrainCornerHeights(
    float SouthWest,
    float SouthEast,
    float NorthWest,
    float NorthEast);
}

internal readonly record struct SceneryGeometryBuildLimits(
  ulong MaximumPlacements,
  ulong MaximumSourceBatchInstances,
  ulong MaximumMaterialBatches,
  ulong MaximumVertices,
  ulong MaximumIndices
) {
  public static SceneryGeometryBuildLimits Default { get; } = new(
    1_000_000,
    1_000_000,
    65_536,
    4_000_000,
    12_000_000);

  public void Validate() {
    if (MaximumPlacements > int.MaxValue ||
        MaximumSourceBatchInstances > int.MaxValue ||
        MaximumMaterialBatches > int.MaxValue ||
        MaximumVertices > int.MaxValue ||
        MaximumIndices > int.MaxValue)
      throw new ArgumentOutOfRangeException(
        nameof(SceneryGeometryBuildLimits),
        "Scenery geometry limits must fit the renderer's signed collection counts.");
  }
}
