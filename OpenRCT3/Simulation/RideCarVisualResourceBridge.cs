// Ride Car Visual Resource Bridge
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

namespace OpenRCT3.Simulation;

/// <summary>An exact decoded SHS resource and its owning OVL symbol.</summary>
internal sealed record RideStaticShapeResourceSource(OvlFile File, StaticShape Resource);

/// <summary>An exact decoded BSH resource and its owning OVL symbol.</summary>
internal sealed record RideBoneShapeResourceSource(OvlFile File, BoneShape Resource);

/// <summary>The decoded shape resources retained from the ride archive closure.</summary>
internal sealed record RideVisualShapeResourceSet(
  IReadOnlyList<RideStaticShapeResourceSource> StaticShapes,
  IReadOnlyList<RideBoneShapeResourceSource> BoneShapes
);

/// <summary>Associates decoded SHS/BSH resources with exact symbols in retained OVL pairs.</summary>
internal static class RideVisualShapeResourceDecoder {
  public static RideVisualShapeResourceSet Decode(IReadOnlyList<Ovl> archives) =>
    Decode(
      archives,
      StaticShapes.Extract,
      BoneShapes.Extract,
      RideCarVisualResourceBridgeLimits.Default);

  internal static RideVisualShapeResourceSet Decode(
    IReadOnlyList<Ovl> archives,
    Func<Ovl, IReadOnlyList<StaticShape>> decodeStaticShapes,
    Func<Ovl, IReadOnlyList<BoneShape>> decodeBoneShapes,
    RideCarVisualResourceBridgeLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(archives);
    ArgumentNullException.ThrowIfNull(decodeStaticShapes);
    ArgumentNullException.ThrowIfNull(decodeBoneShapes);
    if (Convert.ToUInt64(archives.Count) > limits.MaximumResources)
      throw Invalid($"archive count exceeds the limit {limits.MaximumResources}");

    var seenArchives = new HashSet<Ovl>(ReferenceEqualityComparer.Instance);
    var staticSources = new List<RideStaticShapeResourceSource>();
    var boneSources = new List<RideBoneShapeResourceSource>();
    foreach (var archive in archives) {
      if (archive == null) throw Invalid("archive list contains null");
      if (!seenArchives.Add(archive))
        throw Invalid("archive list contains the same OVL instance more than once");
      Associate(
        archive,
        decodeStaticShapes(archive),
        FileType.StaticShape,
        shape => shape.Name,
        (file, shape) => new RideStaticShapeResourceSource(file, shape),
        staticSources,
        "SHS",
        limits);
      Associate(
        archive,
        decodeBoneShapes(archive),
        FileType.BoneShape,
        shape => shape.Name,
        (file, shape) => new RideBoneShapeResourceSource(file, shape),
        boneSources,
        "BSH",
        limits);
    }
    return new RideVisualShapeResourceSet(
      Array.AsReadOnly(staticSources.ToArray()),
      Array.AsReadOnly(boneSources.ToArray()));
  }

  private static void Associate<TResource, TSource>(
    Ovl archive,
    IReadOnlyList<TResource>? resources,
    FileType type,
    Func<TResource, string> getName,
    Func<OvlFile, TResource, TSource> create,
    ICollection<TSource> sources,
    string tag,
    RideCarVisualResourceBridgeLimits limits
  ) where TResource : class {
    if (resources == null) throw Invalid($"{tag} decoder returned null");
    if (Convert.ToUInt64(resources.Count) > limits.MaximumResources ||
        Convert.ToUInt64(sources.Count) >
          limits.MaximumResources - Convert.ToUInt64(resources.Count))
      throw Invalid($"aggregate {tag} resources exceed the limit {limits.MaximumResources}");

    // OvlFile.Path retains the half containing the relocation-resolved resource data. Frontier
    // normally stores shape headers in unique OVLs, but installed track archives also contain
    // loader-owned SHS headers in their common half. Keep that exact provenance instead of
    // inferring shape ownership from a filename suffix.
    var files = archive.Keys.Where(file => file.Type == type).ToArray();
    if (files.Length != resources.Count)
      throw Invalid(
        $"{tag} decoder returned {resources.Count} resources for {files.Length} exact OVL symbols");
    var available = files.GroupBy(
      file => file.Name,
      StringComparer.OrdinalIgnoreCase).ToDictionary(
        group => group.Key,
        group => group.ToArray(),
        StringComparer.OrdinalIgnoreCase);
    var consumed = new HashSet<OvlFile>();
    foreach (var resource in resources) {
      if (resource == null) throw Invalid($"{tag} decoder returned a null resource");
      var name = getName(resource);
      ValidateIdentifier(name, $"decoded {tag} name", limits);
      if (!available.TryGetValue(name, out var candidates) || candidates.Length != 1)
        throw Invalid(
          $"{tag} decoded resource '{name}' does not have one exact OVL symbol");
      if (!consumed.Add(candidates[0]))
        throw Invalid($"{tag} decoder returned duplicate resource '{name}'");
      sources.Add(create(candidates[0], resource));
    }
  }

  private static void ValidateIdentifier(
    string? value,
    string description,
    RideCarVisualResourceBridgeLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(value) ||
        value.Length > limits.MaximumStringCharacters ||
        !string.Equals(value, value.Trim(), StringComparison.Ordinal))
      throw Invalid(
        $"{description} is empty, padded, or exceeds " +
        $"{limits.MaximumStringCharacters} characters");
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid ride visual shape resource set: {message}.");
}

/// <summary>One supported SVD LOD linked to its exact decoded shape resource.</summary>
internal sealed record RideVisualShapeLodLink(
  SceneryItemVisualLod Lod,
  RideStaticShapeResourceSource? StaticShapeSource,
  RideBoneShapeResourceSource? BoneShapeSource
) {
  public bool IsResolved => StaticShapeSource != null || BoneShapeSource != null;
  public StaticShape? StaticShape => StaticShapeSource?.Resource;
  public BoneShape? BoneShape => BoneShapeSource?.Resource;
}

/// <summary>One resolved RIC visual occurrence and its supported SVD shape LODs.</summary>
internal sealed record RideCarVisualShapeLink(
  TrackedRideResourceLink Ride,
  RideTrainLink Train,
  RideCarLink Car,
  RideVisualLink Visual,
  IReadOnlyList<RideVisualShapeLodLink> Lods
);

/// <summary>Ride-car visual links plus explicit missing SHS/BSH edge evidence.</summary>
/// <remarks>
/// The result owns no meshes, models, GPU handles, or OVL archives. It retains only managed decoded
/// resource records; a scene builder must create and own render objects from the linked shapes.
/// </remarks>
internal sealed record RideCarVisualResourceBridgeResult(
  IReadOnlyList<RideCarVisualShapeLink> Visuals,
  int UnresolvedShapeReferenceCount
) {
  public int ResolvedShapeLodCount => Visuals.Sum(visual =>
    visual.Lods.Count(lod => lod.IsResolved));
}

/// <summary>
/// Resolves the RIT/RIC/SVD graph's shape LODs within each car's proven archive closure.
/// </summary>
/// <remarks>
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerRIC.cpp">
/// ManagerRIC</see> writes the body, moving-part, wheel, and axle SVDs as tagged SymbolRefs.
/// <see href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerSVD.cpp">
/// ManagerSVD</see> then writes exactly one optional SHS or BSH SymbolRef according to each LOD's
/// mesh type. This bridge follows those edges only. It never searches outside the dependency closure
/// already proven for the referring RIC, and it leaves billboard LODs to the texture renderer. Shape
/// adaptation is deliberately deferred so this resource layer never acquires render-object ownership.
/// </remarks>
internal static class RideCarVisualResourceBridge {
  public static RideCarVisualResourceBridgeResult Resolve(
    RideResourceGraph graph,
    RideVisualShapeResourceSet resources
  ) => Resolve(
    graph,
    resources,
    RideCarVisualResourceBridgeLimits.Default);

  internal static RideCarVisualResourceBridgeResult Resolve(
    RideResourceGraph graph,
    RideVisualShapeResourceSet resources,
    RideCarVisualResourceBridgeLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(graph);
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentNullException.ThrowIfNull(resources.StaticShapes);
    ArgumentNullException.ThrowIfNull(resources.BoneShapes);
    ValidateLimits(limits);

    var budget = new ResolutionBudget(limits);
    var staticShapes = new ShapeResourceIndex<RideStaticShapeResourceSource, StaticShape>(
      resources.StaticShapes,
      source => source.File,
      source => source.Resource,
      FileType.StaticShape,
      "SHS",
      budget,
      limits);
    var boneShapes = new ShapeResourceIndex<RideBoneShapeResourceSource, BoneShape>(
      resources.BoneShapes,
      source => source.File,
      source => source.Resource,
      FileType.BoneShape,
      "BSH",
      budget,
      limits);
    var carClosures = new Dictionary<RideCarResourceSource, IReadOnlySet<string>>(
      ReferenceEqualityComparer.Instance);
    var visuals = new List<RideCarVisualShapeLink>();
    var unresolved = 0;

    if (graph.Rides == null) throw Invalid("ride list is null");
    budget.ReserveRelationships(graph.Rides.Count, "ride occurrences");
    foreach (var ride in graph.Rides) {
      if (ride == null) throw Invalid("ride list contains null");
      if (ride.Trains == null) throw Invalid("ride train list is null");
      budget.ReserveRelationships(ride.Trains.Count, "ride-train occurrences");

      foreach (var train in ride.Trains) {
        if (train == null) throw Invalid("ride train list contains null");
        if (train.Cars == null) throw Invalid("ride-train car list is null");
        if (!train.IsResolved) {
          if (train.Cars.Count != 0)
            throw Invalid($"unresolved RIT '{train.Reference}' exposes car links");
          continue;
        }
        ValidateUpstreamSource(
          train.Source!.File,
          train.Source.Resource,
          FileType.RideTrain,
          "RIT",
          limits);
        budget.ReserveRelationships(train.Cars.Count, $"RIT '{train.Train!.Name}' car occurrences");

        foreach (var car in train.Cars) {
          if (car == null) throw Invalid("ride-car list contains null");
          if (car.Visuals == null) throw Invalid("ride-car visual list is null");
          if (!car.IsResolved) {
            if (car.Visuals.Count != 0)
              throw Invalid($"unresolved RIC '{car.Reference}' exposes visual links");
            continue;
          }

          var carSource = car.Source!;
          ValidateUpstreamSource(
            carSource.File,
            carSource.Resource,
            FileType.RideCar,
            "RIC",
            limits);
          if (!carClosures.TryGetValue(carSource, out var allowedPaths)) {
            allowedPaths = ValidateClosure(carSource, budget, limits);
            carClosures.Add(carSource, allowedPaths);
          }
          budget.ReserveRelationships(
            car.Visuals.Count,
            $"RIC '{car.Car!.Name}' visual occurrences");

          foreach (var visual in car.Visuals) {
            if (visual == null) throw Invalid("ride-car visual list contains null");
            if (!visual.IsResolved) continue;

            var visualSource = visual.Source!;
            ValidateUpstreamSource(
              visualSource.File,
              visualSource.Resource,
              FileType.SceneryItemVisual,
              "SVD",
              limits);
            if (!allowedPaths.Contains(visualSource.File.Path))
              throw Invalid(
                $"RIC '{car.Car!.Name}' resolved SVD '{visualSource.Resource.Name}' from " +
                $"archive '{visualSource.File.Path}' outside its proven dependency closure");

            var linkedLods = LinkLods(
              visualSource,
              allowedPaths,
              staticShapes,
              boneShapes,
              budget,
              limits,
              ref unresolved);
            budget.ReserveRelationships(1, "resolved ride-car visual occurrence");
            visuals.Add(new RideCarVisualShapeLink(ride, train, car, visual, linkedLods));
          }
        }
      }
    }

    return new RideCarVisualResourceBridgeResult(
      Array.AsReadOnly(visuals.ToArray()),
      unresolved);
  }

  private static IReadOnlyList<RideVisualShapeLodLink> LinkLods(
    RideVisualResourceSource visualSource,
    IReadOnlySet<string> allowedPaths,
    ShapeResourceIndex<RideStaticShapeResourceSource, StaticShape> staticShapes,
    ShapeResourceIndex<RideBoneShapeResourceSource, BoneShape> boneShapes,
    ResolutionBudget budget,
    RideCarVisualResourceBridgeLimits limits,
    ref int unresolved
  ) {
    var visual = visualSource.Resource;
    if (visual.Lods == null) throw Invalid($"SVD '{visual.Name}' has a null LOD list");
    if (!float.IsFinite(visual.Scale) || visual.Scale < 0f)
      throw Invalid($"SVD '{visual.Name}' scale variation is invalid");
    budget.ReserveRelationships(visual.Lods.Count, $"SVD '{visual.Name}' LODs");
    var links = new List<RideVisualShapeLodLink>();

    foreach (var lod in visual.Lods) {
      if (lod == null) throw Invalid($"SVD '{visual.Name}' has a null LOD");
      ValidateIdentifier(lod.Name, $"SVD '{visual.Name}' LOD name", limits);
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
          var staticSource = staticShapes.Resolve(staticName, allowedPaths, visual.Name);
          if (staticSource == null) {
            unresolved = IncrementUnresolved(unresolved);
            links.Add(new RideVisualShapeLodLink(lod, null, null));
            break;
          }
          links.Add(new RideVisualShapeLodLink(lod, staticSource, null));
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
          var boneSource = boneShapes.Resolve(boneName, allowedPaths, visual.Name);
          if (boneSource == null) {
            unresolved = IncrementUnresolved(unresolved);
            links.Add(new RideVisualShapeLodLink(lod, null, null));
            break;
          }
          links.Add(new RideVisualShapeLodLink(lod, null, boneSource));
          break;
        case SvdLodType.Billboard:
          if (lod.StaticShapeRef != null || lod.BoneShapeRef != null)
            throw Invalid(
              $"SVD '{visual.Name}' billboard LOD '{lod.Name}' declares a shape reference");
          break;
        default:
          throw Invalid(
            $"SVD '{visual.Name}' LOD '{lod.Name}' has unsupported type {lod.Type}");
      }
    }
    return Array.AsReadOnly(links.ToArray());
  }

  private static HashSet<string> ValidateClosure(
    RideCarResourceSource source,
    ResolutionBudget budget,
    RideCarVisualResourceBridgeLimits limits
  ) {
    if (source.AllowedArchivePaths == null)
      throw Invalid($"RIC '{source.Resource.Name}' has a null dependency closure");
    budget.ReserveResources(
      source.AllowedArchivePaths.Count,
      $"RIC '{source.Resource.Name}' dependency closure");
    var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in source.AllowedArchivePaths) {
      ValidateIdentifier(path, $"RIC '{source.Resource.Name}' archive path", limits);
      if (!paths.Add(path))
        throw Invalid(
          $"RIC '{source.Resource.Name}' dependency closure repeats archive '{path}'");
    }
    if (!paths.Contains(source.File.Path))
      throw Invalid(
        $"RIC '{source.Resource.Name}' dependency closure omits its source archive " +
        $"'{source.File.Path}'");
    return paths;
  }

  private static void ValidateUpstreamSource(
    OvlFile? file,
    object? resource,
    FileType expectedType,
    string tag,
    RideCarVisualResourceBridgeLimits limits
  ) {
    if (file == null || resource == null)
      throw Invalid($"{tag} source has no OVL file or decoded resource");
    var decodedName = resource switch {
      RideTrain train => train.Name,
      RideCar car => car.Name,
      SceneryItemVisual visual => visual.Name,
      _ => throw Invalid($"{tag} source has an unsupported decoded resource type"),
    };
    ValidateSourceIdentity(file, decodedName, expectedType, tag, limits);
  }

  private static void ValidateSourceIdentity(
    OvlFile file,
    string decodedName,
    FileType expectedType,
    string tag,
    RideCarVisualResourceBridgeLimits limits
  ) {
    ValidateIdentifier(file.Name, $"{tag} OVL name", limits);
    ValidateIdentifier(file.Path, $"{tag} OVL path", limits);
    ValidateIdentifier(decodedName, $"decoded {tag} name", limits);
    if (file.Type != expectedType)
      throw Invalid(
        $"{tag} resource '{file.Name}' has OVL type '{file.Type.ToTagString()}', expected " +
        $"'{expectedType.ToTagString()}'");
    if (!string.Equals(file.Name, decodedName, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{tag} OVL name '{file.Name}' does not match decoded name '{decodedName}'");
  }

  private static string ParseTaggedReference(
    string? reference,
    string expectedTag,
    string description,
    RideCarVisualResourceBridgeLimits limits
  ) {
    if (reference == null)
      throw Invalid($"{description} has no {expectedTag.ToUpperInvariant()} reference");
    ValidateIdentifier(reference, $"{description} reference", limits);
    var separator = reference.LastIndexOf(':');
    if (separator <= 0 || separator == reference.Length - 1 ||
        !reference[(separator + 1)..].Equals(expectedTag, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{description} reference '{reference}' is not an exact name:{expectedTag} key");
    var name = reference[..separator];
    ValidateIdentifier(name, $"{description} resource name", limits);
    return name;
  }

  private static void ValidateIdentifier(
    string? value,
    string description,
    RideCarVisualResourceBridgeLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(value) ||
        value.Length > limits.MaximumStringCharacters ||
        !string.Equals(value, value.Trim(), StringComparison.Ordinal))
      throw Invalid(
        $"{description} is empty, padded, or exceeds " +
        $"{limits.MaximumStringCharacters} characters");
  }

  private static int IncrementUnresolved(int current) => current == int.MaxValue
    ? throw Invalid("unresolved shape reference count exceeds the runtime range")
    : current + 1;

  private static void ValidateLimits(RideCarVisualResourceBridgeLimits limits) {
    if (limits.MaximumResources == 0 ||
        limits.MaximumRelationships == 0 ||
        limits.MaximumStringCharacters <= 0)
      throw new ArgumentOutOfRangeException(nameof(limits), "Bridge limits must be positive.");
  }

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid ride-car visual resource graph: {message}.");

  private sealed class ShapeResourceIndex<TSource, TResource>
    where TSource : class
    where TResource : class {
    private readonly Dictionary<string, Dictionary<string, TSource>> resourcesByArchive =
      new(StringComparer.OrdinalIgnoreCase);
    private readonly string tag;

    public ShapeResourceIndex(
      IReadOnlyList<TSource> sources,
      Func<TSource, OvlFile> getFile,
      Func<TSource, TResource> getResource,
      FileType expectedType,
      string tag,
      ResolutionBudget budget,
      RideCarVisualResourceBridgeLimits limits
    ) {
      this.tag = tag;
      budget.ReserveResources(sources.Count, $"{tag} resource index");
      foreach (var source in sources) {
        if (source == null) throw Invalid($"{tag} resource list contains null");
        var file = getFile(source);
        var resource = getResource(source);
        if (file == null || resource == null)
          throw Invalid($"{tag} source has no OVL file or decoded resource");
        var name = resource switch {
          StaticShape shape => shape.Name,
          BoneShape shape => shape.Name,
          _ => throw new ArgumentException(
            $"Unsupported ride visual shape type '{typeof(TResource).Name}'.", nameof(sources)),
        };
        ValidateSourceIdentity(file, name, expectedType, tag, limits);
        if (!resourcesByArchive.TryGetValue(file.Path, out var resourcesByName)) {
          resourcesByName = new Dictionary<string, TSource>(StringComparer.OrdinalIgnoreCase);
          resourcesByArchive.Add(file.Path, resourcesByName);
        }
        if (!resourcesByName.TryAdd(name, source))
          throw Invalid(
            $"duplicate {tag} identity '{file.Path}|{name}:{tag.ToLowerInvariant()}'");
      }
    }

    public TSource? Resolve(
      string name,
      IReadOnlySet<string> allowedPaths,
      string visualName
    ) {
      TSource? match = null;
      string? matchPath = null;
      foreach (var path in allowedPaths) {
        if (!resourcesByArchive.TryGetValue(path, out var resourcesByName) ||
            !resourcesByName.TryGetValue(name, out var candidate))
          continue;
        if (match != null)
          throw Invalid(
            $"SVD '{visualName}' shape '{name}:{tag.ToLowerInvariant()}' is ambiguous between " +
            $"allowed archives '{matchPath}' and '{path}'");
        match = candidate;
        matchPath = path;
      }
      return match;
    }
  }

  private sealed class ResolutionBudget(RideCarVisualResourceBridgeLimits limits) {
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

internal readonly record struct RideCarVisualResourceBridgeLimits(
  ulong MaximumResources,
  ulong MaximumRelationships,
  int MaximumStringCharacters
) {
  public static RideCarVisualResourceBridgeLimits Default { get; } = new(
    MaximumResources: 256 * 1024,
    MaximumRelationships: 4_000_000,
    MaximumStringCharacters: 4 * 1024);
}
