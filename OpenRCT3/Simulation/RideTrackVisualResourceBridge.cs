// Ride Track Visual Resource Bridge
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>The decoded resources required to follow TKS-owned ride-track visuals.</summary>
internal sealed record RideTrackVisualResourceSet(
  TrackSectionResourceGraph Sections,
  IReadOnlyList<OvlResourceDependencyClosure> DependencyClosures,
  IReadOnlyList<RideVisualResourceSource> Visuals,
  RideVisualShapeResourceSet Shapes
);

/// <summary>One decoded mesh's exact serialized material and texture-style identities.</summary>
internal sealed record RideTrackVisualMeshMaterialIdentity(
  int MeshIndex,
  string MeshName,
  string? FlexibleTextureReference,
  string? TextureStyleReference
);

/// <summary>One supported track SVD LOD linked to its exact SHS or BSH resource.</summary>
internal sealed record RideTrackVisualShapeLodLink(
  SceneryItemVisualLod Lod,
  RideStaticShapeResourceSource? StaticShapeSource,
  RideBoneShapeResourceSource? BoneShapeSource,
  IReadOnlyList<RideTrackVisualMeshMaterialIdentity> Materials
) {
  public StaticShape? StaticShape => StaticShapeSource?.Resource;
  public BoneShape? BoneShape => BoneShapeSource?.Resource;
}

/// <summary>One TKS/SID/SVD chain and all supported shape LODs in serialized order.</summary>
internal sealed record RideTrackVisualLink(
  TrackSectionResourceLink Section,
  TrackSectionSceneryLink Scenery,
  RideVisualResourceSource VisualSource,
  IReadOnlyList<string> AllowedArchivePaths,
  IReadOnlyList<RideTrackVisualShapeLodLink> Lods
);

/// <summary>Closure-proven ride-track visual chains with no renderer-object ownership.</summary>
internal sealed record RideTrackVisualResourceBridgeResult(
  IReadOnlyList<RideTrackVisualLink> Visuals
) {
  public int StaticLodCount => Visuals.Sum(visual =>
    visual.Lods.Count(lod => lod.StaticShapeSource != null));
  public int BoneLodCount => Visuals.Sum(visual =>
    visual.Lods.Count(lod => lod.BoneShapeSource != null));
  public int MeshCount => Visuals.Sum(visual =>
    visual.Lods.Sum(lod => lod.Materials.Count));
}

/// <summary>Follows exact TKS SID-to-SVD-to-SHS/BSH edges inside proven OVL closures.</summary>
/// <remarks>
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerTKS.cpp">
/// ManagerTKS</see> writes the required SID SymbolRef.
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerSID.cpp">
/// ManagerSID</see> writes the ordered SVD SymbolRefs, and
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerSVD.cpp">
/// ManagerSVD</see> writes the optional SHS/BSH and FTX/TXS SymbolRefs for each LOD. This bridge
/// retains those exact identities and source pair halves only. It does not adapt, place, or render
/// any mesh.
/// </remarks>
internal static class RideTrackVisualResourceBridge {
  public static RideTrackVisualResourceBridgeResult Resolve(
    RideTrackVisualResourceSet resources
  ) => Resolve(resources, RideTrackVisualResourceBridgeLimits.Default);

  internal static RideTrackVisualResourceBridgeResult Resolve(
    RideTrackVisualResourceSet resources,
    RideTrackVisualResourceBridgeLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentNullException.ThrowIfNull(resources.Sections);
    ArgumentNullException.ThrowIfNull(resources.DependencyClosures);
    ArgumentNullException.ThrowIfNull(resources.Visuals);
    ArgumentNullException.ThrowIfNull(resources.Shapes);
    ArgumentNullException.ThrowIfNull(resources.Shapes.StaticShapes);
    ArgumentNullException.ThrowIfNull(resources.Shapes.BoneShapes);
    ValidateLimits(limits);

    var budget = new ResolutionBudget(limits);
    var closures = BuildClosureIndex(resources.DependencyClosures, budget, limits);
    var visuals = new ResourceIndex<RideVisualResourceSource, SceneryItemVisual>(
      resources.Visuals,
      source => source.File,
      source => source.Resource,
      visual => visual.Name,
      FileType.SceneryItemVisual,
      "SVD",
      budget,
      limits);
    var staticShapes = new ResourceIndex<RideStaticShapeResourceSource, StaticShape>(
      resources.Shapes.StaticShapes,
      source => source.File,
      source => source.Resource,
      shape => shape.Name,
      FileType.StaticShape,
      "SHS",
      budget,
      limits);
    var boneShapes = new ResourceIndex<RideBoneShapeResourceSource, BoneShape>(
      resources.Shapes.BoneShapes,
      source => source.File,
      source => source.Resource,
      shape => shape.Name,
      FileType.BoneShape,
      "BSH",
      budget,
      limits);

    var graph = resources.Sections;
    if (graph.Sections == null) throw Invalid("TKS section list is null");
    budget.ReserveResources(graph.Sections.Count, "TKS section list");
    var links = new List<RideTrackVisualLink>();
    foreach (var section in graph.Sections) {
      if (section == null) throw Invalid("TKS section list contains null");
      ValidateSource(
        section.Source.File,
        section.Source.Resource,
        item => item.Name,
        FileType.TrackSection,
        "TKS",
        limits);
      if (!closures.TryGetValue(section.Source.File.Path, out var allowedPaths))
        throw Invalid(
          $"TKS '{section.Source.Resource.Name}' has no exact dependency closure");
      if (!allowedPaths.Contains(section.Source.File.Path))
        throw Invalid(
          $"TKS '{section.Source.Resource.Name}' closure omits its source archive " +
          $"'{section.Source.File.Path}'");
      LinkSection(
        section,
        allowedPaths,
        visuals,
        staticShapes,
        boneShapes,
        links,
        budget,
        limits);
    }
    return new RideTrackVisualResourceBridgeResult(
      Array.AsReadOnly(links.ToArray()));
  }

  private static void LinkSection(
    TrackSectionResourceLink section,
    IReadOnlySet<string> allowedPaths,
    ResourceIndex<RideVisualResourceSource, SceneryItemVisual> visuals,
    ResourceIndex<RideStaticShapeResourceSource, StaticShape> staticShapes,
    ResourceIndex<RideBoneShapeResourceSource, BoneShape> boneShapes,
    ICollection<RideTrackVisualLink> links,
    ResolutionBudget budget,
    RideTrackVisualResourceBridgeLimits limits
  ) {
    if (section.Scenery == null)
      throw Invalid($"TKS '{section.Source.Resource.Name}' has a null SID link");
    var sceneryName = ParseTaggedReference(
      section.Scenery.Reference,
      "sid",
      $"TKS '{section.Source.Resource.Name}' SID",
      limits);
    var scenerySource = section.Scenery.Source
      ?? throw Invalid(
        $"TKS '{section.Source.Resource.Name}' is missing SID " +
        $"'{section.Scenery.Reference}'");
    ValidateSource(
      scenerySource.File,
      scenerySource.Resource,
      item => item.Name,
      FileType.SceneryItem,
      "SID",
      limits);
    if (!string.Equals(
      sceneryName,
      scenerySource.Resource.Name,
      StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"TKS '{section.Source.Resource.Name}' SID reference '{sceneryName}' resolves to " +
        $"'{scenerySource.Resource.Name}'");
    if (!allowedPaths.Contains(scenerySource.File.Path))
      throw Invalid(
        $"TKS '{section.Source.Resource.Name}' SID '{sceneryName}' resolves outside its " +
        $"dependency closure at '{scenerySource.File.Path}'");
    if (scenerySource.Resource.Type != SidType.RideTrack)
      throw Invalid(
        $"TKS '{section.Source.Resource.Name}' SID '{sceneryName}' has scenery type " +
        $"'{scenerySource.Resource.Type}' instead of '{SidType.RideTrack}'");
    if (scenerySource.Resource.VisualRefs == null)
      throw Invalid($"SID '{sceneryName}' has a null SVD reference list");
    if (scenerySource.Resource.VisualRefs.Count == 0)
      throw Invalid($"SID '{sceneryName}' has no SVD references");
    budget.ReserveRelationships(
      scenerySource.Resource.VisualRefs.Count,
      $"SID '{sceneryName}' SVD references");

    var seenVisuals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var exactClosure = Array.AsReadOnly(allowedPaths.ToArray());
    foreach (var reference in scenerySource.Resource.VisualRefs) {
      var visualName = ParseTaggedReference(
        reference,
        "svd",
        $"SID '{sceneryName}' visual",
        limits);
      if (!seenVisuals.Add(visualName))
        throw Invalid($"SID '{sceneryName}' repeats SVD reference '{reference}'");
      var visualSource = visuals.ResolveRequired(
        visualName,
        allowedPaths,
        $"SID '{sceneryName}'");
      var lods = LinkLods(
        visualSource,
        allowedPaths,
        staticShapes,
        boneShapes,
        budget,
        limits);
      links.Add(new RideTrackVisualLink(
        section,
        section.Scenery,
        visualSource,
        exactClosure,
        lods));
    }
  }

  private static IReadOnlyList<RideTrackVisualShapeLodLink> LinkLods(
    RideVisualResourceSource visualSource,
    IReadOnlySet<string> allowedPaths,
    ResourceIndex<RideStaticShapeResourceSource, StaticShape> staticShapes,
    ResourceIndex<RideBoneShapeResourceSource, BoneShape> boneShapes,
    ResolutionBudget budget,
    RideTrackVisualResourceBridgeLimits limits
  ) {
    var visual = visualSource.Resource;
    if (visual.Lods == null) throw Invalid($"SVD '{visual.Name}' has a null LOD list");
    if (!float.IsFinite(visual.Scale) || visual.Scale < 0f)
      throw Invalid($"SVD '{visual.Name}' has invalid scale variation {visual.Scale}");
    budget.ReserveRelationships(visual.Lods.Count, $"SVD '{visual.Name}' LODs");
    var links = new List<RideTrackVisualShapeLodLink>();
    foreach (var lod in visual.Lods) {
      if (lod == null) throw Invalid($"SVD '{visual.Name}' LOD list contains null");
      ValidateIdentifier(lod.Name, $"SVD '{visual.Name}' LOD name", limits);
      if (!float.IsFinite(lod.Distance))
        throw Invalid($"SVD '{visual.Name}' LOD '{lod.Name}' distance is non-finite");
      switch (lod.Type) {
        case SvdLodType.StaticShape:
          if (lod.BoneShapeRef != null)
            throw Invalid(
              $"SVD '{visual.Name}' static LOD '{lod.Name}' also declares a BSH reference");
          var staticName = ParseTaggedReference(
            lod.StaticShapeRef,
            "shs",
            $"SVD '{visual.Name}' static LOD '{lod.Name}'",
            limits);
          var staticSource = staticShapes.ResolveRequired(
            staticName,
            allowedPaths,
            $"SVD '{visual.Name}'");
          links.Add(new RideTrackVisualShapeLodLink(
            lod,
            staticSource,
            null,
            MaterialIdentities(staticSource.Resource, budget, limits)));
          break;
        case SvdLodType.BoneShape:
          if (lod.StaticShapeRef != null)
            throw Invalid(
              $"SVD '{visual.Name}' bone LOD '{lod.Name}' also declares an SHS reference");
          var boneName = ParseTaggedReference(
            lod.BoneShapeRef,
            "bsh",
            $"SVD '{visual.Name}' bone LOD '{lod.Name}'",
            limits);
          var boneSource = boneShapes.ResolveRequired(
            boneName,
            allowedPaths,
            $"SVD '{visual.Name}'");
          links.Add(new RideTrackVisualShapeLodLink(
            lod,
            null,
            boneSource,
            MaterialIdentities(boneSource.Resource, budget, limits)));
          break;
        case SvdLodType.Billboard:
          if (lod.StaticShapeRef != null || lod.BoneShapeRef != null)
            throw Invalid(
              $"SVD '{visual.Name}' billboard LOD '{lod.Name}' declares a shape reference");
          ValidateOptionalTaggedReference(
            lod.FlexibleTextureRef,
            "ftx",
            $"SVD '{visual.Name}' billboard LOD '{lod.Name}' FTX",
            limits);
          ValidateOptionalTaggedReference(
            lod.TextureStyleRef,
            "txs",
            $"SVD '{visual.Name}' billboard LOD '{lod.Name}' TXS",
            limits);
          break;
        default:
          throw Invalid(
            $"SVD '{visual.Name}' LOD '{lod.Name}' has unsupported type {lod.Type}");
      }
    }
    return Array.AsReadOnly(links.ToArray());
  }

  private static IReadOnlyList<RideTrackVisualMeshMaterialIdentity> MaterialIdentities(
    StaticShape shape,
    ResolutionBudget budget,
    RideTrackVisualResourceBridgeLimits limits
  ) => MaterialIdentities(
    shape.Name,
    shape.Meshes,
    mesh => mesh.Name,
    mesh => mesh.FtxRef,
    mesh => mesh.TxsRef,
    budget,
    limits);

  private static IReadOnlyList<RideTrackVisualMeshMaterialIdentity> MaterialIdentities(
    BoneShape shape,
    ResolutionBudget budget,
    RideTrackVisualResourceBridgeLimits limits
  ) => MaterialIdentities(
    shape.Name,
    shape.Meshes,
    mesh => mesh.Name,
    mesh => mesh.FtxRef,
    mesh => mesh.TxsRef,
    budget,
    limits);

  private static IReadOnlyList<RideTrackVisualMeshMaterialIdentity> MaterialIdentities<T>(
    string shapeName,
    IReadOnlyList<T> meshes,
    Func<T, string> getName,
    Func<T, string?> getFlexibleTextureReference,
    Func<T, string?> getTextureStyleReference,
    ResolutionBudget budget,
    RideTrackVisualResourceBridgeLimits limits
  ) where T : class {
    if (meshes == null) throw Invalid($"shape '{shapeName}' has a null mesh list");
    budget.ReserveRelationships(meshes.Count, $"shape '{shapeName}' meshes");
    var result = new RideTrackVisualMeshMaterialIdentity[meshes.Count];
    foreach (var index in Enumerable.Range(0, meshes.Count)) {
      var mesh = meshes[index]
        ?? throw Invalid($"shape '{shapeName}' mesh list contains null");
      var meshName = getName(mesh);
      ValidateIdentifier(meshName, $"shape '{shapeName}' mesh {index} name", limits);
      var ftx = getFlexibleTextureReference(mesh);
      var txs = getTextureStyleReference(mesh);
      ValidateOptionalTaggedReference(
        ftx,
        "ftx",
        $"shape '{shapeName}' mesh '{meshName}' FTX",
        limits);
      ValidateOptionalTaggedReference(
        txs,
        "txs",
        $"shape '{shapeName}' mesh '{meshName}' TXS",
        limits);
      result[index] = new RideTrackVisualMeshMaterialIdentity(
        index,
        meshName,
        ftx,
        txs);
    }
    return Array.AsReadOnly(result);
  }

  private static Dictionary<string, IReadOnlySet<string>> BuildClosureIndex(
    IReadOnlyList<OvlResourceDependencyClosure> closures,
    ResolutionBudget budget,
    RideTrackVisualResourceBridgeLimits limits
  ) {
    budget.ReserveResources(closures.Count, "dependency closure index");
    var result = new Dictionary<string, IReadOnlySet<string>>(
      closures.Count,
      StringComparer.OrdinalIgnoreCase);
    foreach (var closure in closures) {
      if (closure == null) throw Invalid("dependency closure list contains null");
      ValidateIdentifier(closure.SourcePath, "dependency closure source", limits);
      if (closure.AllowedTargetPaths == null)
        throw Invalid($"dependency closure '{closure.SourcePath}' has a null target list");
      budget.ReserveResources(
        closure.AllowedTargetPaths.Count,
        $"dependency closure '{closure.SourcePath}' targets");
      var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var path in closure.AllowedTargetPaths) {
        ValidateIdentifier(path, "dependency closure target", limits);
        if (!paths.Add(path))
          throw Invalid(
            $"dependency closure '{closure.SourcePath}' repeats target '{path}'");
      }
      if (!result.TryAdd(closure.SourcePath, paths))
        throw Invalid($"dependency closure repeats source '{closure.SourcePath}'");
    }
    return result;
  }

  private static string ParseTaggedReference(
    string? reference,
    string expectedTag,
    string description,
    RideTrackVisualResourceBridgeLimits limits
  ) {
    if (reference == null) throw Invalid($"{description} has no reference");
    ValidateIdentifier(reference, $"{description} reference", limits);
    var separator = reference.IndexOf(':');
    if (separator <= 0 || separator != reference.LastIndexOf(':') ||
        separator == reference.Length - 1)
      throw Invalid($"{description} reference '{reference}' is not one exact name:tag key");
    var name = reference[..separator];
    var tag = reference[(separator + 1)..];
    ValidateIdentifier(name, $"{description} resource name", limits);
    if (!tag.Equals(expectedTag, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{description} reference '{reference}' has tag '{tag}', expected '{expectedTag}'");
    return name;
  }

  private static void ValidateOptionalTaggedReference(
    string? reference,
    string expectedTag,
    string description,
    RideTrackVisualResourceBridgeLimits limits
  ) {
    if (reference == null) return;
    _ = ParseTaggedReference(reference, expectedTag, description, limits);
  }

  private static void ValidateSource<T>(
    OvlFile? file,
    T? resource,
    Func<T, string> getName,
    FileType expectedType,
    string tag,
    RideTrackVisualResourceBridgeLimits limits
  ) where T : class {
    if (file == null || resource == null)
      throw Invalid($"{tag} source has no OVL file or decoded resource");
    var name = getName(resource);
    ValidateIdentifier(file.Path, $"{tag} OVL path", limits);
    ValidateIdentifier(file.Name, $"{tag} OVL name", limits);
    ValidateIdentifier(name, $"decoded {tag} name", limits);
    if (file.Type != expectedType)
      throw Invalid(
        $"{tag} resource '{file.Name}' has type '{file.Type.ToTagString()}', expected " +
        $"'{expectedType.ToTagString()}'");
    if (!string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{tag} OVL name '{file.Name}' does not match decoded name '{name}'");
  }

  private static void ValidateIdentifier(
    string? value,
    string description,
    RideTrackVisualResourceBridgeLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(value) ||
        value.Length > limits.MaximumStringCharacters ||
        !string.Equals(value, value.Trim(), StringComparison.Ordinal))
      throw Invalid(
        $"{description} is empty, padded, or exceeds " +
        $"{limits.MaximumStringCharacters} characters");
  }

  private static void ValidateLimits(RideTrackVisualResourceBridgeLimits limits) {
    if (limits.MaximumResources == 0 ||
        limits.MaximumRelationships == 0 ||
        limits.MaximumStringCharacters <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits), "Bridge limits must be positive.");
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid ride-track visual resource graph: {message}.");

  private sealed class ResourceIndex<TSource, TResource>
    where TSource : class
    where TResource : class {
    private readonly Dictionary<string, Dictionary<string, TSource>> resourcesByArchive =
      new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<TSource>> resourcesByName;
    private readonly Func<TSource, OvlFile> getFile;
    private readonly string tag;

    public ResourceIndex(
      IReadOnlyList<TSource> sources,
      Func<TSource, OvlFile> getFile,
      Func<TSource, TResource> getResource,
      Func<TResource, string> getName,
      FileType expectedType,
      string tag,
      ResolutionBudget budget,
      RideTrackVisualResourceBridgeLimits limits
    ) {
      ArgumentNullException.ThrowIfNull(sources);
      this.getFile = getFile;
      this.tag = tag;
      budget.ReserveResources(sources.Count, $"{tag} resource index");
      var byName = new Dictionary<string, List<TSource>>(StringComparer.OrdinalIgnoreCase);
      foreach (var source in sources) {
        if (source == null) throw Invalid($"{tag} resource list contains null");
        var file = getFile(source);
        var resource = getResource(source);
        ValidateSource(file, resource, getName, expectedType, tag, limits);
        var name = getName(resource);
        if (!resourcesByArchive.TryGetValue(file.Path, out var byArchiveName)) {
          byArchiveName = new Dictionary<string, TSource>(StringComparer.OrdinalIgnoreCase);
          resourcesByArchive.Add(file.Path, byArchiveName);
        }
        if (!byArchiveName.TryAdd(name, source))
          throw Invalid(
            $"duplicate {tag} identity '{file.Path}|{name}:{tag.ToLowerInvariant()}'");
        if (!byName.TryGetValue(name, out var candidates))
          byName.Add(name, candidates = []);
        candidates.Add(source);
      }
      resourcesByName = byName.ToDictionary(
        pair => pair.Key,
        pair => (IReadOnlyList<TSource>)Array.AsReadOnly(pair.Value.ToArray()),
        StringComparer.OrdinalIgnoreCase);
    }

    public TSource ResolveRequired(
      string name,
      IReadOnlySet<string> allowedPaths,
      string owner
    ) {
      if (!resourcesByName.TryGetValue(name, out var candidates))
        throw Invalid($"{owner} references missing '{name}:{tag.ToLowerInvariant()}'");
      TSource? match = null;
      string? matchPath = null;
      foreach (var candidate in candidates) {
        var path = getFile(candidate).Path;
        if (!allowedPaths.Contains(path)) continue;
        if (match != null)
          throw Invalid(
            $"{owner} reference '{name}:{tag.ToLowerInvariant()}' is ambiguous between " +
            $"allowed archives '{matchPath}' and '{path}'");
        match = candidate;
        matchPath = path;
      }
      if (match != null) return match;
      throw Invalid(
        $"{owner} reference '{name}:{tag.ToLowerInvariant()}' resolves only outside its " +
        "dependency closure");
    }
  }

  private sealed class ResolutionBudget(RideTrackVisualResourceBridgeLimits limits) {
    private ulong resources;
    private ulong relationships;

    public void ReserveResources(int count, string description) =>
      Reserve(ref resources, count, limits.MaximumResources, "resources", description);

    public void ReserveRelationships(int count, string description) =>
      Reserve(
        ref relationships,
        count,
        limits.MaximumRelationships,
        "relationships",
        description);

    private static void Reserve(
      ref ulong current,
      int count,
      ulong maximum,
      string kind,
      string description
    ) {
      if (count < 0) throw Invalid($"{description} has a negative {kind} count");
      var converted = Convert.ToUInt64(count);
      if (converted > maximum || current > maximum - converted)
        throw Invalid(
          $"aggregate {kind} exceed the limit {maximum} while processing {description}");
      current += converted;
    }
  }
}

internal readonly record struct RideTrackVisualResourceBridgeLimits(
  ulong MaximumResources,
  ulong MaximumRelationships,
  int MaximumStringCharacters
) {
  public static RideTrackVisualResourceBridgeLimits Default { get; } = new(
    MaximumResources: 256 * 1024,
    MaximumRelationships: 4_000_000,
    MaximumStringCharacters: 4 * 1024);
}
