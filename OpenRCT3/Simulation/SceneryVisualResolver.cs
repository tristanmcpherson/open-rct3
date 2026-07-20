// Scenery Visual Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>One static-shape LOD reached through a placed object's SID and SVD resources.</summary>
public sealed record ResolvedSceneryStaticLod(
  SceneryItemVisual Visual,
  SceneryItemVisualLod Lod,
  StaticShape Shape
);

/// <summary>One rest-pose bone-shape LOD reached through a placed object's SID and SVD resources.</summary>
public sealed record ResolvedSceneryBoneLod(
  SceneryItemVisual Visual,
  SceneryItemVisualLod Lod,
  BoneShape Shape
) {
  /// <summary>The BAN resources declared by this LOD, retained in serialized SVD order.</summary>
  public IReadOnlyList<BoneAnimation> Animations { get; init; } = [];
}

/// <summary>
/// A decoded SID plus the shape LODs from its first supported visual alternative.
/// </summary>
public sealed record ResolvedSceneryObject(
  SceneryItem Item,
  IReadOnlyList<ResolvedSceneryStaticLod> StaticLods
) {
  public IReadOnlyList<ResolvedSceneryBoneLod> BoneLods { get; init; } = [];
}

/// <summary>Resolves SID-to-SVD-to-SHS/BSH resource chains for scenery rendering.</summary>
public sealed class SceneryVisualResolver {
  private readonly Func<string, FileType, SceneryResourceEntry?> find;
  private readonly Func<
    SceneryResourceEntry,
    string,
    FileType,
    SceneryResourceEntry?> findFrom;
  private readonly Func<Ovl, IReadOnlyList<SceneryItem>> decodeItems;
  private readonly Func<Ovl, IReadOnlyList<SceneryItemVisual>> decodeVisuals;
  private readonly Func<Ovl, IReadOnlyList<StaticShape>> decodeShapes;
  private readonly Func<Ovl, IReadOnlyList<BoneShape>> decodeBoneShapes;
  private readonly Func<Ovl, IReadOnlyList<BoneAnimation>> decodeBoneAnimations;
  private readonly Dictionary<Ovl, IReadOnlyDictionary<string, SceneryItem>> itemCache = [];
  private readonly Dictionary<Ovl, IReadOnlyDictionary<string, SceneryItemVisual>> visualCache = [];
  private readonly Dictionary<Ovl, IReadOnlyDictionary<string, StaticShape>> shapeCache = [];
  private readonly Dictionary<Ovl, IReadOnlyDictionary<string, BoneShape>> boneShapeCache = [];
  private readonly Dictionary<Ovl, IReadOnlyDictionary<string, BoneAnimation>> boneAnimationCache = [];

  public SceneryVisualResolver(SceneryResourceCatalog catalog)
    : this(
      catalog.Find,
      SceneryItems.Extract,
      SceneryItemVisuals.Extract,
      StaticShapes.Extract,
      BoneShapes.Extract,
      BoneAnimations.Extract,
      catalog.FindFrom) { }

  internal SceneryVisualResolver(
    Func<string, FileType, SceneryResourceEntry?> find,
    Func<Ovl, IReadOnlyList<SceneryItem>> decodeItems,
    Func<Ovl, IReadOnlyList<SceneryItemVisual>> decodeVisuals,
    Func<Ovl, IReadOnlyList<StaticShape>> decodeShapes,
    Func<Ovl, IReadOnlyList<BoneShape>> decodeBoneShapes,
    Func<Ovl, IReadOnlyList<BoneAnimation>>? decodeBoneAnimations = null,
    Func<SceneryResourceEntry, string, FileType, SceneryResourceEntry?>? findFrom = null
  ) {
    ArgumentNullException.ThrowIfNull(find);
    ArgumentNullException.ThrowIfNull(decodeItems);
    ArgumentNullException.ThrowIfNull(decodeVisuals);
    ArgumentNullException.ThrowIfNull(decodeShapes);
    ArgumentNullException.ThrowIfNull(decodeBoneShapes);
    this.find = find;
    this.decodeItems = decodeItems;
    this.decodeVisuals = decodeVisuals;
    this.decodeShapes = decodeShapes;
    this.decodeBoneShapes = decodeBoneShapes;
    this.decodeBoneAnimations = decodeBoneAnimations ?? BoneAnimations.Extract;
    this.findFrom = findFrom ?? ((_, name, type) => find(name, type));
  }

  /// <summary>
  /// Resolves <paramref name="objectKey"/>. A missing catalog SID returns <c>null</c>. SID visual
  /// references are serialized alternatives (for example, billboard and 3D tree variants), so the
  /// first visual containing a supported static- or bone-shape LOD wins.
  /// </summary>
  public ResolvedSceneryObject? Resolve(string objectKey) {
    ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
    var itemEntry = find(objectKey, FileType.SceneryItem);
    if (itemEntry == null) return null;

    var item = GetExact(
      itemEntry.Archive,
      itemEntry.File.Name,
      itemCache,
      decodeItems,
      "SID");
    foreach (var visualRef in item.VisualRefs) {
      var visualEntry = FindRequired(
        itemEntry, visualRef, FileType.SceneryItemVisual, item.Name);
      var visual = GetExact(
        visualEntry.Archive,
        visualEntry.File.Name,
        visualCache,
        decodeVisuals,
        "SVD");
      var staticLods = new List<ResolvedSceneryStaticLod>();
      var boneLods = new List<ResolvedSceneryBoneLod>();
      foreach (var lod in visual.Lods) {
        switch (lod.Type) {
          case SvdLodType.StaticShape:
            if (string.IsNullOrWhiteSpace(lod.StaticShapeRef))
              throw new InvalidDataException(
                $"SVD '{visual.Name}' static LOD '{lod.Name}' has no SHS reference.");

            var shapeEntry = FindRequired(
              visualEntry, lod.StaticShapeRef, FileType.StaticShape, visual.Name);
            var shape = GetExact(
              shapeEntry.Archive,
              shapeEntry.File.Name,
              shapeCache,
              decodeShapes,
              "SHS");
            staticLods.Add(new ResolvedSceneryStaticLod(visual, lod, shape));
            break;
          case SvdLodType.BoneShape:
            if (string.IsNullOrWhiteSpace(lod.BoneShapeRef))
              throw new InvalidDataException(
                $"SVD '{visual.Name}' bone LOD '{lod.Name}' has no BSH reference.");

            var boneShapeEntry = FindRequired(
              visualEntry, lod.BoneShapeRef, FileType.BoneShape, visual.Name);
            var boneShape = GetExact(
              boneShapeEntry.Archive,
              boneShapeEntry.File.Name,
              boneShapeCache,
              decodeBoneShapes,
              "BSH");
            if (lod.AnimationRefs == null)
              throw new InvalidDataException(
                $"SVD '{visual.Name}' bone LOD '{lod.Name}' has a null BAN reference list.");
            var animations = new List<BoneAnimation>(lod.AnimationRefs.Count);
            foreach (var animationRef in lod.AnimationRefs) {
              var animationEntry = FindRequired(
                visualEntry, animationRef, FileType.BoneAnim, visual.Name);
              var animation = GetExact(
                animationEntry.Archive,
                animationEntry.File.Name,
                boneAnimationCache,
                this.decodeBoneAnimations,
                "BAN");
              animations.Add(animation);
            }
            boneLods.Add(new ResolvedSceneryBoneLod(visual, lod, boneShape) {
              Animations = animations.ToArray()
            });
            break;
        }
      }
      if (staticLods.Count > 0 || boneLods.Count > 0)
        return new ResolvedSceneryObject(item, staticLods.ToArray()) {
          BoneLods = boneLods.ToArray()
        };
    }
    return new ResolvedSceneryObject(item, []);
  }

  private SceneryResourceEntry FindRequired(
    SceneryResourceEntry owner,
    string taggedReference,
    FileType expectedType,
    string ownerName
  ) {
    var (name, type) = ParseTaggedReference(taggedReference, ownerName);
    if (type != expectedType)
      throw new InvalidDataException(
        $"Resource '{ownerName}' references '{taggedReference}', expected " +
        $"{expectedType.ToTagString()}.");
    return findFrom(owner, name, type)
      ?? throw new InvalidDataException(
        $"Resource '{ownerName}' references missing '{taggedReference}'.");
  }

  private static (string Name, FileType Type) ParseTaggedReference(
    string reference,
    string ownerName
  ) {
    if (string.IsNullOrWhiteSpace(reference))
      throw new InvalidDataException($"Resource '{ownerName}' has an empty reference.");
    var separator = reference.LastIndexOf(':');
    if (separator <= 0 || separator == reference.Length - 1)
      throw new InvalidDataException(
        $"Resource '{ownerName}' has malformed reference '{reference}'.");

    var name = reference[..separator];
    var tag = reference[(separator + 1)..];
    var type = tag.ToFileType();
    if (type == FileType.Unknown)
      throw new InvalidDataException(
        $"Resource '{ownerName}' has unknown reference type '{tag}'.");
    return (name, type);
  }

  private static T GetExact<T>(
    Ovl archive,
    string name,
    Dictionary<Ovl, IReadOnlyDictionary<string, T>> cache,
    Func<Ovl, IReadOnlyList<T>> decode,
    string kind
  ) where T : notnull {
    if (!cache.TryGetValue(archive, out var resources)) {
      var decoded = decode(archive);
      var byName = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
      foreach (var resource in decoded) {
        var resourceName = ResourceName(resource);
        if (!byName.TryAdd(resourceName, resource))
          throw new InvalidDataException(
            $"OVL archive contains duplicate {kind} resource '{resourceName}'.");
      }
      cache.Add(archive, resources = byName);
    }

    return resources.TryGetValue(name, out var result)
      ? result
      : throw new InvalidDataException(
        $"OVL catalog entry '{name}' has no decoded {kind} resource.");
  }

  private static string ResourceName<T>(T resource) => resource switch {
    SceneryItem item => item.Name,
    SceneryItemVisual visual => visual.Name,
    StaticShape shape => shape.Name,
    BoneShape shape => shape.Name,
    BoneAnimation animation => animation.Name,
    _ => throw new ArgumentException(
      $"Unsupported scenery resource model '{typeof(T).Name}'.", nameof(resource)),
  };
}
