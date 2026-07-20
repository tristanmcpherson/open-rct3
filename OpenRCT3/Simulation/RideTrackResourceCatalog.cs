// Ride Track Resource Catalog
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>One TRR edge that declares a resolved TKS used by a DAT placement.</summary>
internal sealed record RideTrackResourceDeclaration(
  TrackedRideTrackResourceLink Ride,
  TrackedRideTrackSectionLink Section
);

/// <summary>A DAT track placement and its exact provenance-backed TKS/SPL graph data.</summary>
internal sealed record RideTrackResourceLink(
  RideTrackSectionResourceLink PlacementSection,
  TrackSectionResourceLink? Section,
  IReadOnlyList<RideTrackResourceDeclaration> Declarations
) {
  public RideTrackPlacement Placement => PlacementSection.Placement;
  public bool IsResolved => Section != null;
  public IReadOnlyList<TrackSectionSplineLink> Splines =>
    Section?.Splines ?? Array.Empty<TrackSectionSplineLink>();
}

/// <summary>The bounded, order-preserving result of resolving DAT track placements.</summary>
internal sealed record RideTrackResourceResolution(
  IReadOnlyList<RideTrackResourceLink> Placements,
  int UnresolvedPlacementCount
);

/// <summary>
/// Composes exact DAT overlay resolution with the provenance already proven by TKS and TRR graphs.
/// </summary>
/// <remarks>
/// The catalog never searches by a bare resource name. A resolved DAT source must match one graph
/// section by raw OVL path, name, type, and decoded-resource identity. Graph SPL sources and TRR
/// declarations are retained unchanged so later geometry code can preserve their archive owners.
/// </remarks>
internal sealed class RideTrackResourceCatalog {
  private readonly RideTrackSectionResourceResolver placementResolver;
  private readonly IReadOnlyDictionary<ResourceKey, TrackSectionResourceLink> sections;
  private readonly IReadOnlyDictionary<
    ResourceKey,
    IReadOnlyList<RideTrackResourceDeclaration>> declarations;
  private readonly RideTrackResourceCatalogLimits limits;

  public RideTrackResourceCatalog(
    IReadOnlyList<RideTrackSectionResourceSource> placementSources,
    TrackSectionResourceGraph sectionGraph,
    TrackedRideTrackResourceGraph rideGraph
  ) : this(
    placementSources,
    sectionGraph,
    rideGraph,
    RideTrackResourceCatalogLimits.Default) { }

  internal RideTrackResourceCatalog(
    IReadOnlyList<RideTrackSectionResourceSource> placementSources,
    TrackSectionResourceGraph sectionGraph,
    TrackedRideTrackResourceGraph rideGraph,
    RideTrackResourceCatalogLimits limits
  ) {
    ArgumentNullException.ThrowIfNull(placementSources);
    ArgumentNullException.ThrowIfNull(sectionGraph);
    ArgumentNullException.ThrowIfNull(rideGraph);

    this.limits = limits;
    var budget = new CatalogBudget(limits);
    sections = BuildSectionIndex(sectionGraph, budget);
    ValidatePlacementSources(placementSources, sections, budget);
    placementResolver = new RideTrackSectionResourceResolver(placementSources);
    declarations = BuildDeclarationIndex(rideGraph, sections, budget);
  }

  /// <summary>Resolves placements in DAT order without any cross-archive fallback.</summary>
  public RideTrackResourceResolution ResolveAll(
    IReadOnlyList<RideTrackPlacement> placements
  ) {
    ArgumentNullException.ThrowIfNull(placements);
    ValidateCount(
      placements.Count,
      Convert.ToUInt64(limits.MaximumPlacements),
      "DAT placements");

    var placementLinks = placementResolver.ResolveAll(placements);
    var resolved = new RideTrackResourceLink[placementLinks.Count];
    var unresolvedCount = 0;
    foreach (var index in Enumerable.Range(0, placementLinks.Count)) {
      var placementLink = placementLinks[index];
      if (placementLink.Source is null) {
        resolved[index] = new RideTrackResourceLink(
          placementLink,
          null,
          Array.Empty<RideTrackResourceDeclaration>());
        unresolvedCount++;
        continue;
      }

      var key = Key(placementLink.Source.File);
      if (!sections.TryGetValue(key, out var section))
        throw Invalid(
          $"resolved DAT source '{Describe(key)}' is absent from the TKS graph");
      if (!ReferenceEquals(placementLink.Source.Resource, section.Source.Resource))
        throw Invalid(
          $"resolved DAT source '{Describe(key)}' changed decoded TKS identity");
      var sourceDeclarations = declarations.TryGetValue(key, out var values)
        ? values
        : Array.Empty<RideTrackResourceDeclaration>();
      resolved[index] = new RideTrackResourceLink(
        placementLink,
        section,
        sourceDeclarations);
    }
    return new RideTrackResourceResolution(
      Array.AsReadOnly(resolved),
      unresolvedCount);
  }

  private IReadOnlyDictionary<ResourceKey, TrackSectionResourceLink> BuildSectionIndex(
    TrackSectionResourceGraph graph,
    CatalogBudget budget
  ) {
    if (graph.Sections is null)
      throw Invalid("TKS graph section list is null");
    ValidateCount(graph.Sections.Count, limits.MaximumResources, "TKS graph sections");
    budget.ReserveResources(graph.Sections.Count, "TKS graph sections");

    var splineSources = new Dictionary<ResourceKey, SplineResourceSource>(
      ResourceKeyComparer.Instance);
    var index = new Dictionary<ResourceKey, TrackSectionResourceLink>(
      graph.Sections.Count,
      ResourceKeyComparer.Instance);
    var unresolvedCount = 0;
    foreach (var link in graph.Sections) {
      if (link is null)
        throw new ArgumentException("TKS graph sections cannot contain null.", nameof(graph));
      var key = ValidateSectionSource(link.Source, budget, "TKS graph");
      if (!index.TryAdd(key, link))
        throw Invalid($"TKS graph repeats exact source '{Describe(key)}'");
      unresolvedCount = checked(unresolvedCount + ValidateSectionLink(
        link,
        splineSources,
        budget));
    }
    ValidateUnresolvedCount(
      graph.UnresolvedReferenceCount,
      unresolvedCount,
      "TKS graph");
    return index;
  }

  private void ValidatePlacementSources(
    IReadOnlyList<RideTrackSectionResourceSource> sources,
    IReadOnlyDictionary<ResourceKey, TrackSectionResourceLink> sectionIndex,
    CatalogBudget budget
  ) {
    ValidateCount(sources.Count, limits.MaximumResources, "DAT overlay TKS sources");
    budget.ReserveResources(sources.Count, "DAT overlay TKS sources");
    foreach (var source in sources) {
      if (source is null)
        throw new ArgumentException(
          "DAT overlay TKS sources cannot contain null.",
          nameof(sources));
      budget.ReserveString(source.OverlayPath, "DAT overlay path");
      var key = ValidateSectionSource(
        source.File,
        source.Resource,
        budget,
        "DAT overlay TKS source");
      if (!sectionIndex.TryGetValue(key, out var graphLink))
        throw Invalid(
          $"DAT overlay source '{Describe(key)}' is absent from the TKS graph");
      if (!ReferenceEquals(source.Resource, graphLink.Source.Resource))
        throw Invalid(
          $"DAT overlay source '{Describe(key)}' has ambiguous decoded TKS identity");
    }
  }

  private IReadOnlyDictionary<
    ResourceKey,
    IReadOnlyList<RideTrackResourceDeclaration>> BuildDeclarationIndex(
    TrackedRideTrackResourceGraph graph,
    IReadOnlyDictionary<ResourceKey, TrackSectionResourceLink> sectionIndex,
    CatalogBudget budget
  ) {
    if (graph.Rides is null) throw Invalid("TRR graph ride list is null");
    ValidateCount(graph.Rides.Count, limits.MaximumResources, "TRR graph rides");
    budget.ReserveResources(graph.Rides.Count, "TRR graph rides");

    var rideIdentities = new HashSet<ResourceKey>(ResourceKeyComparer.Instance);
    var splineSources = new Dictionary<ResourceKey, SplineResourceSource>(
      ResourceKeyComparer.Instance);
    var mutable = new Dictionary<ResourceKey, List<RideTrackResourceDeclaration>>(
      ResourceKeyComparer.Instance);
    var unresolvedCount = 0;
    foreach (var ride in graph.Rides) {
      if (ride is null)
        throw new ArgumentException("TRR graph rides cannot contain null.", nameof(graph));
      var rideKey = ValidateRideSource(ride.Source, budget);
      if (!rideIdentities.Add(rideKey))
        throw Invalid($"TRR graph repeats exact source '{Describe(rideKey)}'");
      unresolvedCount = checked(unresolvedCount + ValidateRideLink(
        ride,
        sectionIndex,
        mutable,
        splineSources,
        budget));
    }
    ValidateUnresolvedCount(
      graph.UnresolvedReferenceCount,
      unresolvedCount,
      "TRR graph");
    return mutable.ToDictionary(
      pair => pair.Key,
      pair => (IReadOnlyList<RideTrackResourceDeclaration>)
        Array.AsReadOnly(pair.Value.ToArray()),
      ResourceKeyComparer.Instance);
  }

  private int ValidateSectionLink(
    TrackSectionResourceLink link,
    IDictionary<ResourceKey, SplineResourceSource> splineSources,
    CatalogBudget budget
  ) {
    if (link.Scenery is null) throw Invalid("TKS graph scenery link is null");
    var sceneryName = ParseTaggedReference(
      link.Scenery.Reference,
      "sid",
      "TKS scenery reference",
      budget);
    var unresolved = 0;
    if (link.Scenery.Source is null) {
      unresolved++;
    } else {
      ValidateNamedSource(
        link.Scenery.Source.File,
        link.Scenery.Source.Resource?.Name,
        FileType.SceneryItem,
        sceneryName,
        "TKS scenery target",
        budget);
    }

    if (link.Splines is null) throw Invalid("TKS graph spline list is null");
    ValidateCount(link.Splines.Count, limits.MaximumRelationships, "TKS spline links");
    budget.ReserveRelationships(link.Splines.Count, "TKS spline links");
    var identities = new HashSet<(TrackSectionSplineRole Role, int? Index)>();
    foreach (var spline in link.Splines) {
      if (spline is null) throw Invalid("TKS graph spline list contains null");
      ValidateSectionSplineIndex(spline);
      if (!identities.Add((spline.Role, spline.Index)))
        throw Invalid(
          $"TKS graph repeats spline role {spline.Role} index {spline.Index}");
      var name = ParseTaggedReference(
        spline.Reference,
        "spl",
        $"TKS {spline.Role} spline reference",
        budget);
      if (spline.Source is null) {
        unresolved++;
        continue;
      }
      RegisterSplineSource(spline.Source, name, splineSources, budget);
    }
    RequireSectionSplineRole(identities, TrackSectionSplineRole.CarLeft);
    RequireSectionSplineRole(identities, TrackSectionSplineRole.CarRight);
    RequireSectionSplineRole(identities, TrackSectionSplineRole.JoinLeft);
    RequireSectionSplineRole(identities, TrackSectionSplineRole.JoinRight);
    return unresolved;
  }

  private int ValidateRideLink(
    TrackedRideTrackResourceLink ride,
    IReadOnlyDictionary<ResourceKey, TrackSectionResourceLink> sectionIndex,
    IDictionary<ResourceKey, List<RideTrackResourceDeclaration>> declarationIndex,
    IDictionary<ResourceKey, SplineResourceSource> splineSources,
    CatalogBudget budget
  ) {
    if (ride.TrackSections is null) throw Invalid("TRR graph TKS link list is null");
    if (ride.Splines is null) throw Invalid("TRR graph SPL link list is null");
    ValidateCount(
      ride.TrackSections.Count,
      limits.MaximumRelationships,
      "TRR TKS links");
    ValidateCount(ride.Splines.Count, limits.MaximumRelationships, "TRR SPL links");
    budget.ReserveRelationships(ride.TrackSections.Count, "TRR TKS links");
    budget.ReserveRelationships(ride.Splines.Count, "TRR SPL links");

    var unresolved = 0;
    var sectionRoles = new HashSet<(TrackedRideTrackSectionRole Role, int? Index)>();
    foreach (var section in ride.TrackSections) {
      if (section is null) throw Invalid("TRR graph TKS link list contains null");
      ValidateRideSectionIndex(section);
      if (!sectionRoles.Add((section.Role, section.Index)))
        throw Invalid(
          $"TRR graph repeats TKS role {section.Role} index {section.Index}");
      var name = ParseTaggedReference(
        section.Reference,
        "tks",
        $"TRR {section.Role} TKS reference",
        budget);
      if (section.Metadata != null && !string.Equals(
        section.Metadata.InternalName,
        name,
        StringComparison.OrdinalIgnoreCase))
        throw Invalid(
          $"TRR construction metadata '{section.Metadata.InternalName}' does not match " +
          $"TKS reference '{name}'");
      if (section.Source is null) {
        unresolved++;
        continue;
      }

      var key = ValidateSectionSource(section.Source, budget, "TRR TKS target");
      if (!string.Equals(name, key.Name, StringComparison.OrdinalIgnoreCase))
        throw Invalid(
          $"TRR TKS reference '{name}' resolves to '{Describe(key)}'");
      if (!sectionIndex.TryGetValue(key, out var graphSection))
        throw Invalid(
          $"TRR TKS target '{Describe(key)}' is absent from the TKS graph");
      if (!ReferenceEquals(section.Source.Resource, graphSection.Source.Resource))
        throw Invalid(
          $"TRR TKS target '{Describe(key)}' has ambiguous decoded identity");
      if (!declarationIndex.TryGetValue(key, out var values))
        declarationIndex.Add(key, values = []);
      values.Add(new RideTrackResourceDeclaration(ride, section));
    }

    var splineRoles = new HashSet<TrackedRideTrackSplineRole>();
    foreach (var spline in ride.Splines) {
      if (spline is null) throw Invalid("TRR graph SPL link list contains null");
      if (!splineRoles.Add(spline.Role))
        throw Invalid($"TRR graph repeats SPL role {spline.Role}");
      var name = ParseTaggedReference(
        spline.Reference,
        "spl",
        $"TRR {spline.Role} SPL reference",
        budget);
      if (spline.Source is null) {
        unresolved++;
        continue;
      }
      RegisterSplineSource(spline.Source, name, splineSources, budget);
    }
    return unresolved;
  }

  private ResourceKey ValidateSectionSource(
    TrackSectionResourceSource source,
    CatalogBudget budget,
    string description
  ) {
    if (source is null)
      throw new ArgumentException($"{description} cannot be null.", nameof(source));
    return ValidateSectionSource(source.File, source.Resource, budget, description);
  }

  private ResourceKey ValidateSectionSource(
    OvlFile file,
    TrackSection resource,
    CatalogBudget budget,
    string description
  ) => ValidateNamedSource(
    file,
    resource?.Name,
    FileType.TrackSection,
    expectedName: null,
    description,
    budget);

  private ResourceKey ValidateRideSource(
    TrackedRideTrackResourceSource source,
    CatalogBudget budget
  ) {
    if (source is null)
      throw new ArgumentException("TRR graph source cannot be null.", nameof(source));
    return ValidateNamedSource(
      source.File,
      source.Resource?.Name,
      FileType.TrackedRide,
      expectedName: null,
      "TRR graph source",
      budget);
  }

  private ResourceKey ValidateNamedSource(
    OvlFile file,
    string? decodedName,
    FileType expectedType,
    string? expectedName,
    string description,
    CatalogBudget budget
  ) {
    if (file is null || decodedName is null)
      throw new ArgumentException($"{description} requires an OVL file and decoded resource.");
    budget.ReserveString(file.Path, $"{description} path");
    budget.ReserveString(file.Name, $"{description} OVL name");
    budget.ReserveString(decodedName, $"{description} decoded name");
    if (file.Type != expectedType)
      throw Invalid(
        $"{description} '{file.Name}' has type '{file.Type.ToTagString()}', expected " +
        $"'{expectedType.ToTagString()}'");
    if (!string.Equals(file.Name, decodedName, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{description} cannot disambiguate OVL name '{file.Name}' from decoded name " +
        $"'{decodedName}'");
    if (expectedName != null && !string.Equals(
      expectedName,
      decodedName,
      StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{description} reference '{expectedName}' resolves to '{decodedName}'");
    return Key(file);
  }

  private void RegisterSplineSource(
    SplineResourceSource source,
    string expectedName,
    IDictionary<ResourceKey, SplineResourceSource> sources,
    CatalogBudget budget
  ) {
    if (source is null)
      throw new ArgumentException("Resolved SPL source cannot be null.", nameof(source));
    var resource = source.Resource
      ?? throw new ArgumentException(
        "Resolved SPL source requires a decoded resource.",
        nameof(source));
    var key = ValidateNamedSource(
      source.File,
      resource.Name,
      FileType.Spline,
      expectedName,
      "SPL target",
      budget);
    if (sources.TryGetValue(key, out var previous)) {
      if (!ReferenceEquals(previous.Resource, resource))
        throw Invalid($"SPL source '{Describe(key)}' has ambiguous decoded identity");
      return;
    }
    sources.Add(key, source);
    budget.ReserveResources(1, $"SPL source '{Describe(key)}'");
    ValidateSpline(resource, budget, key);
  }

  private void ValidateSpline(
    Spline spline,
    CatalogBudget budget,
    ResourceKey key
  ) {
    if (!float.IsFinite(spline.TotalLength) ||
        !float.IsFinite(spline.InverseTotalLength) ||
        !float.IsFinite(spline.MaximumY))
      throw Invalid($"SPL source '{Describe(key)}' has non-finite header geometry");
    if (spline.Nodes is null || spline.Segments is null)
      throw Invalid($"SPL source '{Describe(key)}' has a null geometry list");
    if (spline.Nodes.Count == 0)
      throw Invalid($"SPL source '{Describe(key)}' has no nodes");
    var expectedSegments = spline.Nodes.Count - (spline.Cyclic ? 0 : 1);
    if (spline.Segments.Count != expectedSegments)
      throw Invalid(
        $"SPL source '{Describe(key)}' has {spline.Segments.Count} segments for " +
        $"{spline.Nodes.Count} nodes");
    budget.ReserveSplineData(spline.Nodes.Count, $"SPL '{spline.Name}' nodes");
    budget.ReserveSplineData(spline.Segments.Count, $"SPL '{spline.Name}' segments");
    foreach (var node in spline.Nodes) {
      if (node is null || !IsFinite(node.Position) ||
          !IsFinite(node.PreviousControlOffset) ||
          !IsFinite(node.NextControlOffset))
        throw Invalid($"SPL source '{Describe(key)}' has non-finite node geometry");
    }
    foreach (var segment in spline.Segments) {
      if (segment is null || !float.IsFinite(segment.Length))
        throw Invalid($"SPL source '{Describe(key)}' has non-finite segment geometry");
      if (segment.TravelData is null || segment.TravelData.Count != 14)
        throw Invalid(
          $"SPL source '{Describe(key)}' has a malformed 14-byte travel table");
      budget.ReserveSplineData(segment.TravelData.Count, $"SPL '{spline.Name}' travel data");
    }
  }

  private static void ValidateSectionSplineIndex(TrackSectionSplineLink link) {
    var indexed = link.Role is TrackSectionSplineRole.Path or
      TrackSectionSplineRole.SpeedLeft or TrackSectionSplineRole.SpeedRight;
    if (indexed && link.Index is null or < 0)
      throw Invalid($"TKS spline role {link.Role} requires a nonnegative index");
    if (!indexed && link.Index != null)
      throw Invalid($"TKS spline role {link.Role} cannot have an index");
  }

  private static void ValidateRideSectionIndex(TrackedRideTrackSectionLink link) {
    if (link.Role == TrackedRideTrackSectionRole.Construction) {
      if (link.Index is null or < 0 || link.Metadata is null)
        throw Invalid("TRR construction TKS link requires an index and metadata");
      return;
    }
    if (link.Index != null || link.Metadata != null)
      throw Invalid($"TRR TKS role {link.Role} cannot have construction metadata");
  }

  private static void RequireSectionSplineRole(
    IReadOnlySet<(TrackSectionSplineRole Role, int? Index)> identities,
    TrackSectionSplineRole role
  ) {
    if (!identities.Contains((role, null)))
      throw Invalid($"TKS graph is missing required {role} spline link");
  }

  private string ParseTaggedReference(
    string reference,
    string expectedTag,
    string description,
    CatalogBudget budget
  ) {
    budget.ReserveString(reference, description);
    var separator = reference.IndexOf(':');
    if (separator <= 0 || separator != reference.LastIndexOf(':') ||
        separator == reference.Length - 1)
      throw Invalid($"{description} '{reference}' is not one exact tagged identity");
    var name = reference[..separator];
    var tag = reference[(separator + 1)..];
    budget.ValidateIdentifier(name, description);
    if (!string.Equals(tag, expectedTag, StringComparison.OrdinalIgnoreCase))
      throw Invalid(
        $"{description} '{reference}' has tag '{tag}', expected '{expectedTag}'");
    return name;
  }

  private static void ValidateUnresolvedCount(
    int advertised,
    int actual,
    string description
  ) {
    if (advertised != actual)
      throw Invalid(
        $"{description} advertises {advertised} unresolved references, found {actual}");
  }

  private static void ValidateCount(int count, ulong maximum, string description) {
    if (count < 0 || Convert.ToUInt64(count) > maximum)
      throw Invalid($"{description} count {count} exceeds the catalog limit {maximum}");
  }

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static ResourceKey Key(OvlFile file) => new(file.Path, file.Name, file.Type);

  private static string Describe(ResourceKey key) =>
    $"{key.Path}|{key.Name}:{key.Type.ToTagString()}";

  private static InvalidDataException Invalid(string message) =>
    new($"Ride-track resource catalog is invalid: {message}.");

  private readonly record struct ResourceKey(string Path, string Name, FileType Type);

  private sealed class ResourceKeyComparer : IEqualityComparer<ResourceKey> {
    public static ResourceKeyComparer Instance { get; } = new();

    public bool Equals(ResourceKey left, ResourceKey right) =>
      left.Type == right.Type &&
      string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode(ResourceKey value) => HashCode.Combine(
      StringComparer.OrdinalIgnoreCase.GetHashCode(value.Path),
      StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name),
      value.Type);
  }

  private sealed class CatalogBudget(RideTrackResourceCatalogLimits limits) {
    private ulong resources;
    private ulong relationships;
    private ulong splineData;
    private ulong stringCharacters;

    public void ReserveResources(int count, string description) =>
      Reserve(
        ref resources,
        Convert.ToUInt64(count),
        limits.MaximumResources,
        "resources",
        description);

    public void ReserveRelationships(int count, string description) =>
      Reserve(
        ref relationships,
        Convert.ToUInt64(count),
        limits.MaximumRelationships,
        "relationships",
        description);

    public void ReserveSplineData(int count, string description) =>
      Reserve(
        ref splineData,
        Convert.ToUInt64(count),
        limits.MaximumSplineDataElements,
        "spline data",
        description);

    public void ReserveString(string value, string description) {
      ValidateIdentifier(value, description);
      Reserve(
        ref stringCharacters,
        Convert.ToUInt64(value.Length),
        limits.MaximumStringCharacters,
        "string characters",
        description);
    }

    public void ValidateIdentifier(string value, string description) {
      if (string.IsNullOrWhiteSpace(value) ||
          value.Length > limits.MaximumIdentifierLength ||
          !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        throw Invalid(
          $"{description} is empty, padded, or exceeds " +
          $"{limits.MaximumIdentifierLength} characters");
    }

    private static void Reserve(
      ref ulong current,
      ulong count,
      ulong maximum,
      string kind,
      string description
    ) {
      if (count > maximum || current > maximum - count)
        throw Invalid(
          $"aggregate {kind} exceed the limit {maximum} while processing {description}");
      current += count;
    }
  }
}

internal readonly record struct RideTrackResourceCatalogLimits(
  ulong MaximumResources,
  ulong MaximumRelationships,
  ulong MaximumSplineDataElements,
  ulong MaximumStringCharacters,
  int MaximumIdentifierLength,
  int MaximumPlacements
) {
  public static RideTrackResourceCatalogLimits Default { get; } = new(
    MaximumResources: 256 * 1024,
    MaximumRelationships: 1_000_000,
    MaximumSplineDataElements: 16_000_000,
    MaximumStringCharacters: 64_000_000,
    MaximumIdentifierLength: 4 * 1024,
    MaximumPlacements: 100_000);
}
