// Ride Resource Graph
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using System.Collections.Generic;
using System.Linq;

namespace OpenCobra.OVL.Files;

/// <summary>The serialized position of a <c>ric</c> SymbolRef in a ride train.</summary>
public enum RideTrainCarRole {
  Front,
  Second,
  Middle,
  Penultimate,
  Rear,
  Link,
  WildUnknown,
}

/// <summary>The serialized position of an <c>svd</c> SymbolRef in a ride car.</summary>
public enum RideVisualRole {
  Body,
  Moving,
  FrontRightWheel,
  FrontLeftWheel,
  BackRightWheel,
  BackLeftWheel,
  FrontAxle,
  RearAxle,
  WildFlippedBody,
  WildFlippedMoving,
  TrackedRideSplitter,
}

/// <summary>
/// A decoded TRR plus its exact OVL file and caller-proven archive dependency closure.
/// </summary>
/// <param name="AllowedArchivePaths">
/// Exact <see cref="OvlFile.Path"/> values for the source archive and every archive the caller
/// permits this source to reference.
/// </param>
public sealed record TrackedRideResourceSource(
  OvlFile File,
  TrackedRide Resource,
  IReadOnlyList<string> AllowedArchivePaths);

/// <summary>
/// A decoded RIT plus its exact OVL file and caller-proven archive dependency closure.
/// </summary>
public sealed record RideTrainResourceSource(
  OvlFile File,
  RideTrain Resource,
  IReadOnlyList<string> AllowedArchivePaths);

/// <summary>
/// A decoded RIC plus its exact OVL file and caller-proven archive dependency closure.
/// </summary>
public sealed record RideCarResourceSource(
  OvlFile File,
  RideCar Resource,
  IReadOnlyList<string> AllowedArchivePaths);

/// <summary>A decoded SVD target with its exact OVL file provenance.</summary>
public sealed record RideVisualResourceSource(OvlFile File, SceneryItemVisual Resource);

/// <summary>An exact <c>name:svd</c> edge and its proven target when available.</summary>
public sealed record RideVisualLink(
  RideVisualRole Role,
  string Reference,
  RideVisualResourceSource? Source
) {
  public bool IsResolved => Source != null;
  public SceneryItemVisual? Visual => Source?.Resource;
}

/// <summary>An exact <c>name:ric</c> edge and the visual edges exposed by its target.</summary>
public sealed record RideCarLink(
  RideTrainCarRole Role,
  string Reference,
  RideCarResourceSource? Source,
  IReadOnlyList<RideVisualLink> Visuals
) {
  public bool IsResolved => Source != null;
  public RideCar? Car => Source?.Resource;
}

/// <summary>A TRR train-name edge and the car edges exposed by its target.</summary>
public sealed record RideTrainLink(
  string Reference,
  RideTrainResourceSource? Source,
  IReadOnlyList<RideCarLink> Cars
) {
  public bool IsResolved => Source != null;
  public RideTrain? Train => Source?.Resource;
}

/// <summary>A tracked ride and the reference-proven resource edges reachable from it.</summary>
public sealed record TrackedRideResourceLink(
  TrackedRideResourceSource Source,
  IReadOnlyList<RideTrainLink> Trains,
  RideVisualLink? WildSplitter
) {
  public TrackedRide Ride => Source.Resource;
}

/// <summary>A bounded semantic graph over already-decoded TRR, RIT, RIC, and SVD resources.</summary>
public sealed record RideResourceGraph(
  IReadOnlyList<TrackedRideResourceLink> Rides,
  int UnresolvedReferenceCount
);

/// <summary>Resolves the exact names and SymbolRefs serialized by the ride resource managers.</summary>
/// <remarks>
/// TRR train entries are relocated bare strings rather than SymbolRefs. ManagerTRR writes them from
/// its <c>trains</c> list, while ManagerRIT and ManagerRIC write the downstream RIC and SVD edges as
/// tagged SymbolRefs. Missing targets remain explicit because shipped rides commonly place their
/// trains and cars in external OVL pairs. Each edge is restricted to the caller-proven dependency
/// closure of its referring source; duplicate, ambiguous, or malformed provenance fails closed.
/// </remarks>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerTRR.cpp">
/// rct3-importer tracked-ride serializer
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerRIT.cpp">
/// rct3-importer ride-train serializer
/// </seealso>
/// <seealso href="https://github.com/chances/rct3-importer/blob/431fbf2b5b5038c07ed197d29d12facdf319bc68/RCT3%20Importer/src/libOVLng/ManagerRIC.cpp">
/// rct3-importer ride-car serializer
/// </seealso>
public static class RideResourceGraphResolver {
  /// <summary>Links already-decoded ride resources within proven archive dependency closures.</summary>
  public static RideResourceGraph Resolve(
    IReadOnlyList<TrackedRideResourceSource> rides,
    IReadOnlyList<RideTrainResourceSource> trains,
    IReadOnlyList<RideCarResourceSource> cars,
    IReadOnlyList<RideVisualResourceSource> visuals
  ) => Resolve(rides, trains, cars, visuals, RideResourceGraphLimits.Default);

  internal static RideResourceGraph Resolve(
    IReadOnlyList<TrackedRideResourceSource> rides,
    IReadOnlyList<RideTrainResourceSource> trains,
    IReadOnlyList<RideCarResourceSource> cars,
    IReadOnlyList<RideVisualResourceSource> visuals,
    RideResourceGraphLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(rides);
    ArgumentNullException.ThrowIfNull(trains);
    ArgumentNullException.ThrowIfNull(cars);
    ArgumentNullException.ThrowIfNull(visuals);
    ValidateCount(rides.Count, limits, "tracked rides");
    ValidateCount(trains.Count, limits, "ride trains");
    ValidateCount(cars.Count, limits, "ride cars");
    ValidateCount(visuals.Count, limits, "scenery-item visuals");

    var budget = new ResolutionBudget(limits);
    var scopedRides = PrepareScopedSources(
      rides,
      source => source.File,
      source => source.Resource,
      source => source.Resource.Name,
      source => source.AllowedArchivePaths,
      FileType.TrackedRide,
      "TRR",
      budget,
      limits);
    _ = new ArchiveResourceIndex<ScopedResource<TrackedRideResourceSource>>(
      scopedRides,
      source => source.ArchivePath,
      source => source.Name,
      "TRR");
    var trainsByArchive = new ArchiveResourceIndex<ScopedResource<RideTrainResourceSource>>(
      PrepareScopedSources(
        trains,
        source => source.File,
        source => source.Resource,
        source => source.Resource.Name,
        source => source.AllowedArchivePaths,
        FileType.RideTrain,
        "RIT",
        budget,
        limits),
      source => source.ArchivePath,
      source => source.Name,
      "RIT");
    var carsByArchive = new ArchiveResourceIndex<ScopedResource<RideCarResourceSource>>(
      PrepareScopedSources(
        cars,
        source => source.File,
        source => source.Resource,
        source => source.Resource.Name,
        source => source.AllowedArchivePaths,
        FileType.RideCar,
        "RIC",
        budget,
        limits),
      source => source.ArchivePath,
      source => source.Name,
      "RIC");
    var visualsByArchive = new ArchiveResourceIndex<ProvenancedResource<RideVisualResourceSource>>(
      PrepareTargets(
        visuals,
        source => source.File,
        source => source.Resource,
        source => source.Resource.Name,
        FileType.SceneryItemVisual,
        "SVD",
        budget,
        limits),
      source => source.ArchivePath,
      source => source.Name,
      "SVD");

    var linkedRides = new List<TrackedRideResourceLink>(scopedRides.Count);
    foreach (var rideSource in scopedRides) {
      var ride = rideSource.Source.Resource;
      ValidateCount(ride.TrainNames.Count, limits, $"TRR '{ride.Name}' train references");
      budget.ReserveRelationships(1, $"TRR '{ride.Name}'");
      var linkedTrains = new List<RideTrainLink>(ride.TrainNames.Count);
      foreach (var trainReference in ride.TrainNames) {
        ValidateBareName(trainReference, $"TRR '{ride.Name}' train reference", limits);
        budget.ReserveRelationships(1, $"TRR '{ride.Name}' train edge");
        var trainSource = trainsByArchive.Resolve(
          trainReference,
          rideSource.AllowedArchivePaths,
          $"TRR '{ride.Name}' train reference");
        if (trainSource == null) {
          linkedTrains.Add(new RideTrainLink(trainReference, null, []));
          budget.MarkUnresolved();
          continue;
        }
        linkedTrains.Add(new RideTrainLink(
          trainReference,
          trainSource.Source,
          LinkCars(trainSource, carsByArchive, visualsByArchive, budget, limits)));
      }

      var splitter = ride.Wild?.Splitter is { } splitterReference
        ? LinkVisual(
          RideVisualRole.TrackedRideSplitter,
          splitterReference,
          rideSource.AllowedArchivePaths,
          visualsByArchive,
          $"TRR '{ride.Name}' Wild splitter",
          budget,
          limits)
        : null;
      linkedRides.Add(new TrackedRideResourceLink(
        rideSource.Source,
        linkedTrains,
        splitter));
    }
    return new RideResourceGraph(linkedRides, budget.UnresolvedReferenceCount);
  }

  private static IReadOnlyList<RideCarLink> LinkCars(
    ScopedResource<RideTrainResourceSource> trainSource,
    ArchiveResourceIndex<ScopedResource<RideCarResourceSource>> carsByArchive,
    ArchiveResourceIndex<ProvenancedResource<RideVisualResourceSource>> visualsByArchive,
    ResolutionBudget budget,
    RideResourceGraphLimits limits
  ) {
    var train = trainSource.Source.Resource;
    var links = new List<RideCarLink>(7);
    Add(RideTrainCarRole.Front, train.Cars.Front);
    Add(RideTrainCarRole.Second, train.Cars.Second);
    Add(RideTrainCarRole.Middle, train.Cars.Middle);
    Add(RideTrainCarRole.Penultimate, train.Cars.Penultimate);
    Add(RideTrainCarRole.Rear, train.Cars.Rear);
    Add(RideTrainCarRole.Link, train.Cars.Link);
    Add(RideTrainCarRole.WildUnknown, train.Cars.WildUnknown);
    return links;

    void Add(RideTrainCarRole role, string? reference) {
      if (reference == null) return;
      var name = ParseTaggedName(reference, "ric", $"RIT '{train.Name}' {role} car", limits);
      budget.ReserveRelationships(1, $"RIT '{train.Name}' {role} car edge");
      var carSource = carsByArchive.Resolve(
        name,
        trainSource.AllowedArchivePaths,
        $"RIT '{train.Name}' {role} car reference");
      if (carSource == null) {
        links.Add(new RideCarLink(role, reference, null, []));
        budget.MarkUnresolved();
        return;
      }
      links.Add(new RideCarLink(
        role,
        reference,
        carSource.Source,
        LinkVisuals(carSource, visualsByArchive, budget, limits)));
    }
  }

  private static IReadOnlyList<RideVisualLink> LinkVisuals(
    ScopedResource<RideCarResourceSource> carSource,
    ArchiveResourceIndex<ProvenancedResource<RideVisualResourceSource>> visualsByArchive,
    ResolutionBudget budget,
    RideResourceGraphLimits limits
  ) {
    var car = carSource.Source.Resource;
    var links = new List<RideVisualLink>(10);
    Add(RideVisualRole.Body, car.Visual);
    Add(RideVisualRole.Moving, car.MovingVisual);
    Add(RideVisualRole.FrontRightWheel, car.Wheels.FrontRight.Visual);
    Add(RideVisualRole.FrontLeftWheel, car.Wheels.FrontLeft.Visual);
    Add(RideVisualRole.BackRightWheel, car.Wheels.BackRight.Visual);
    Add(RideVisualRole.BackLeftWheel, car.Wheels.BackLeft.Visual);
    Add(RideVisualRole.FrontAxle, car.Axles.Front.Visual);
    Add(RideVisualRole.RearAxle, car.Axles.Rear.Visual);
    Add(RideVisualRole.WildFlippedBody, car.Wild?.FlippedVisual);
    Add(RideVisualRole.WildFlippedMoving, car.Wild?.FlippedMovingVisual);
    return links;

    void Add(RideVisualRole role, string? reference) {
      if (reference == null) return;
      links.Add(LinkVisual(
        role,
        reference,
        carSource.AllowedArchivePaths,
        visualsByArchive,
        $"RIC '{car.Name}' {role} visual",
        budget,
        limits));
    }
  }

  private static RideVisualLink LinkVisual(
    RideVisualRole role,
    string reference,
    IReadOnlyList<string> allowedArchivePaths,
    ArchiveResourceIndex<ProvenancedResource<RideVisualResourceSource>> visualsByArchive,
    string description,
    ResolutionBudget budget,
    RideResourceGraphLimits limits
  ) {
    var name = ParseTaggedName(reference, "svd", description, limits);
    budget.ReserveRelationships(1, description);
    var visualSource = visualsByArchive.Resolve(name, allowedArchivePaths, description);
    if (visualSource != null)
      return new RideVisualLink(role, reference, visualSource.Source);
    budget.MarkUnresolved();
    return new RideVisualLink(role, reference, null);
  }

  private static IReadOnlyList<ScopedResource<TSource>> PrepareScopedSources<TSource>(
    IReadOnlyList<TSource> sources,
    Func<TSource, OvlFile> getFile,
    Func<TSource, object?> getResource,
    Func<TSource, string> getName,
    Func<TSource, IReadOnlyList<string>> getAllowedArchivePaths,
    FileType expectedType,
    string tag,
    ResolutionBudget budget,
    RideResourceGraphLimits limits
  ) where TSource : class {
    budget.ReserveResources(sources.Count, $"{tag} resource index");
    var prepared = new List<ScopedResource<TSource>>(sources.Count);
    foreach (var source in sources) {
      if (source is null)
        throw new ArgumentException($"{tag} resources cannot contain null.", nameof(sources));
      var file = getFile(source);
      var resource = getResource(source);
      if (file is null || resource is null)
        throw new ArgumentException(
          $"{tag} resource sources require an OVL file and decoded resource.",
          nameof(sources));
      var name = getName(source);
      ValidateSourceIdentity(file, name, expectedType, tag, limits);
      var allowedArchivePaths = PrepareAllowedArchivePaths(
        file,
        getAllowedArchivePaths(source),
        tag,
        name,
        budget,
        limits);
      prepared.Add(new ScopedResource<TSource>(
        source,
        file.Path,
        name,
        allowedArchivePaths));
    }
    return prepared;
  }

  private static IReadOnlyList<ProvenancedResource<TSource>> PrepareTargets<TSource>(
    IReadOnlyList<TSource> sources,
    Func<TSource, OvlFile> getFile,
    Func<TSource, object?> getResource,
    Func<TSource, string> getName,
    FileType expectedType,
    string tag,
    ResolutionBudget budget,
    RideResourceGraphLimits limits
  ) where TSource : class {
    budget.ReserveResources(sources.Count, $"{tag} resource index");
    var prepared = new List<ProvenancedResource<TSource>>(sources.Count);
    foreach (var source in sources) {
      if (source is null)
        throw new ArgumentException($"{tag} resources cannot contain null.", nameof(sources));
      var file = getFile(source);
      var resource = getResource(source);
      if (file is null || resource is null)
        throw new ArgumentException(
          $"{tag} resource sources require an OVL file and decoded resource.",
          nameof(sources));
      var name = getName(source);
      ValidateSourceIdentity(file, name, expectedType, tag, limits);
      prepared.Add(new ProvenancedResource<TSource>(source, file.Path, name));
    }
    return prepared;
  }

  private static IReadOnlyList<string> PrepareAllowedArchivePaths(
    OvlFile file,
    IReadOnlyList<string> allowedArchivePaths,
    string tag,
    string name,
    ResolutionBudget budget,
    RideResourceGraphLimits limits
  ) {
    if (allowedArchivePaths is null)
      throw new ArgumentException(
        $"{tag} resource '{name}' requires an allowed archive closure.",
        nameof(allowedArchivePaths));
    ValidateCount(
      allowedArchivePaths.Count,
      limits,
      $"{tag} '{name}' allowed archive closure");
    budget.ReserveResources(
      allowedArchivePaths.Count,
      $"{tag} '{name}' allowed archive closure");

    var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var copied = new string[allowedArchivePaths.Count];
    foreach (var index in Enumerable.Range(0, allowedArchivePaths.Count)) {
      var archivePath = allowedArchivePaths[index];
      ValidateArchivePath(archivePath, $"{tag} '{name}' allowed archive", limits);
      if (!unique.Add(archivePath))
        throw new InvalidDataException(
          $"Ride resource graph {tag} '{name}' dependency closure contains duplicate archive " +
          $"'{archivePath}'.");
      copied[index] = archivePath;
    }
    if (!unique.Contains(file.Path))
      throw new InvalidDataException(
        $"Ride resource graph {tag} '{name}' dependency closure omits its source archive " +
        $"'{file.Path}'.");
    return Array.AsReadOnly(copied);
  }

  private static void ValidateSourceIdentity(
    OvlFile file,
    string decodedName,
    FileType expectedType,
    string tag,
    RideResourceGraphLimits limits
  ) {
    ValidateBareName(file.Name, $"{tag} OVL file", limits);
    ValidateBareName(decodedName, $"decoded {tag} resource", limits);
    ValidateArchivePath(file.Path, $"{tag} OVL file", limits);
    if (file.Type != expectedType)
      throw new InvalidDataException(
        $"Ride resource graph {tag} resource '{file.Name}' has OVL type " +
        $"'{file.Type.ToTagString()}' instead of '{expectedType.ToTagString()}'.");
    if (!string.Equals(file.Name, decodedName, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Ride resource graph cannot disambiguate {tag} OVL file name '{file.Name}' from " +
        $"decoded resource name '{decodedName}'.");
  }

  private static string ParseTaggedName(
    string reference,
    string expectedTag,
    string description,
    RideResourceGraphLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(reference) || reference.Length > limits.MaximumStringCharacters)
      throw new InvalidDataException(
        $"Ride resource graph {description} is empty or exceeds " +
        $"{limits.MaximumStringCharacters} characters.");
    var separator = reference.LastIndexOf(':');
    if (separator <= 0 || separator == reference.Length - 1 ||
        !reference[(separator + 1)..].Equals(expectedTag, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Ride resource graph {description} reference '{reference}' is not an exact " +
        $"name:{expectedTag} key.");
    var name = reference[..separator];
    ValidateBareName(name, description, limits);
    return name;
  }

  private static void ValidateBareName(
    string name,
    string description,
    RideResourceGraphLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(name) ||
        name.Length > limits.MaximumStringCharacters)
      throw new InvalidDataException(
        $"Ride resource graph {description} name is empty or exceeds " +
        $"{limits.MaximumStringCharacters} characters.");
  }

  private static void ValidateArchivePath(
    string path,
    string description,
    RideResourceGraphLimits limits
  ) {
    if (string.IsNullOrWhiteSpace(path) || path.Length > limits.MaximumStringCharacters)
      throw new InvalidDataException(
        $"Ride resource graph {description} archive path is empty or exceeds " +
        $"{limits.MaximumStringCharacters} characters.");
  }

  private static void ValidateCount(
    int count,
    RideResourceGraphLimits limits,
    string description
  ) {
    if (count < 0 || Convert.ToUInt64(count) > limits.MaximumResourcesPerType)
      throw new InvalidDataException(
        $"Ride resource graph {description} count {count} exceeds the limit " +
        $"{limits.MaximumResourcesPerType}.");
  }

  private sealed record ProvenancedResource<TSource>(
    TSource Source,
    string ArchivePath,
    string Name);

  private sealed record ScopedResource<TSource>(
    TSource Source,
    string ArchivePath,
    string Name,
    IReadOnlyList<string> AllowedArchivePaths);

  private sealed class ArchiveResourceIndex<TResource> where TResource : class {
    private readonly Dictionary<string, Dictionary<string, TResource>> resourcesByArchive =
      new(StringComparer.OrdinalIgnoreCase);
    private readonly string tag;

    public ArchiveResourceIndex(
      IReadOnlyList<TResource> resources,
      Func<TResource, string> getArchivePath,
      Func<TResource, string> getName,
      string tag
    ) {
      this.tag = tag;
      foreach (var resource in resources) {
        var archivePath = getArchivePath(resource);
        var name = getName(resource);
        if (!resourcesByArchive.TryGetValue(archivePath, out var resourcesByName)) {
          resourcesByName = new Dictionary<string, TResource>(StringComparer.OrdinalIgnoreCase);
          resourcesByArchive.Add(archivePath, resourcesByName);
        }
        if (!resourcesByName.TryAdd(name, resource))
          throw new InvalidDataException(
            $"Ride resource graph has duplicate {tag} identity " +
            $"'{archivePath}|{name}:{tag.ToLowerInvariant()}'.");
      }
    }

    public TResource? Resolve(
      string name,
      IReadOnlyList<string> allowedArchivePaths,
      string description
    ) {
      TResource? match = null;
      string? matchArchive = null;
      foreach (var archivePath in allowedArchivePaths) {
        if (!resourcesByArchive.TryGetValue(archivePath, out var resourcesByName) ||
            !resourcesByName.TryGetValue(name, out var candidate))
          continue;
        if (match != null)
          throw new InvalidDataException(
            $"Ride resource graph {description} is ambiguous: '{name}:{tag.ToLowerInvariant()}' " +
            $"is defined by allowed archives '{matchArchive}' and '{archivePath}'.");
        match = candidate;
        matchArchive = archivePath;
      }
      return match;
    }
  }

  private sealed class ResolutionBudget(RideResourceGraphLimits limits) {
    private ulong resources;
    private ulong relationships;
    private int unresolvedReferences;

    public int UnresolvedReferenceCount => unresolvedReferences;

    public void ReserveResources(int count, string description) {
      var converted = Convert.ToUInt64(count);
      if (converted > limits.MaximumResources ||
          resources > limits.MaximumResources - converted)
        throw new InvalidDataException(
          $"Ride resource graph aggregate resources exceed the limit " +
          $"{limits.MaximumResources} while indexing {description}.");
      resources += converted;
    }

    public void ReserveRelationships(ulong count, string description) {
      if (count > limits.MaximumRelationships ||
          relationships > limits.MaximumRelationships - count)
        throw new InvalidDataException(
          $"Ride resource graph relationships exceed the limit " +
          $"{limits.MaximumRelationships} while linking {description}.");
      relationships += count;
    }

    public void MarkUnresolved() {
      if (unresolvedReferences == int.MaxValue)
        throw new InvalidDataException(
          "Ride resource graph unresolved reference count exceeds the decoder range.");
      unresolvedReferences++;
    }
  }
}

internal readonly record struct RideResourceGraphLimits(
  ulong MaximumResourcesPerType,
  ulong MaximumResources,
  ulong MaximumRelationships,
  int MaximumStringCharacters
) {
  public static RideResourceGraphLimits Default { get; } =
    new(64 * 1024, 256 * 1024, 1_000_000, 4 * 1024);
}
