// Path Surface Resource Resolver
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK.Assets;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;

using Texture = OpenCobra.GDK.Materials.Texture;

namespace OpenRCT3.Simulation;

/// <summary>A decoded PTD or QTD resource linked to one DAT path surface.</summary>
internal abstract record ResolvedPathSurfaceResource(string SystemName);

/// <summary>A DAT ordinary-path surface linked to its decoded PTD resource.</summary>
internal sealed record ResolvedPathTypeResource(PathType Resource)
  : ResolvedPathSurfaceResource(Resource.InternalName) {
  /// <summary>The first serialized PTD ground-texture name.</summary>
  public string PrimaryTextureReference => Resource.Texture1Ref;

  /// <summary>
  /// The second serialized PTD ground-texture name. The stock selection rule between the two
  /// textures is not yet proven, so runtime rendering currently keeps this reference without
  /// inventing a switching heuristic.
  /// </summary>
  public string AlternateTextureReference => Resource.Texture2Ref;
}

/// <summary>A DAT queue-path surface linked to its decoded QTD resource.</summary>
internal sealed record ResolvedQueueTypeResource(QueueType Resource)
  : ResolvedPathSurfaceResource(Resource.InternalName) {
  /// <summary>The exact QTD flexi-texture SymbolRef.</summary>
  public string TextureReference => Resource.FlexiTextureRef;
}

/// <summary>
/// Resolves exact, case-insensitive DAT path surface system names against decoded PTD/QTD internal
/// names while keeping the two resource namespaces type-safe.
/// </summary>
internal sealed class PathSurfaceResourceResolver : IDisposable {
  private const int MaximumResourceCount = 100_000;
  private const int MaximumSystemNameLength = 4_096;
  private readonly IReadOnlyDictionary<string, ResolvedPathTypeResource> pathTypes;
  private readonly IReadOnlyDictionary<string, ResolvedQueueTypeResource> queueTypes;
  private IReadOnlyDictionary<string, InstalledPathSurfaceContext> installedPathContexts =
    new Dictionary<string, InstalledPathSurfaceContext>(StringComparer.OrdinalIgnoreCase);
  private IReadOnlyDictionary<string, InstalledPathSurfaceContext> installedQueueContexts =
    new Dictionary<string, InstalledPathSurfaceContext>(StringComparer.OrdinalIgnoreCase);
  private bool disposed;

  public PathSurfaceResourceResolver(
    IReadOnlyList<PathType> pathTypes,
    IReadOnlyList<QueueType> queueTypes
  ) {
    ArgumentNullException.ThrowIfNull(pathTypes);
    ArgumentNullException.ThrowIfNull(queueTypes);
    if (pathTypes.Count > MaximumResourceCount || queueTypes.Count > MaximumResourceCount)
      throw new InvalidDataException(
        $"Path surface resource count exceeds the resolver limit {MaximumResourceCount}.");

    this.pathTypes = BuildPathTypeIndex(pathTypes);
    this.queueTypes = BuildQueueTypeIndex(queueTypes);
  }

  private PathSurfaceResourceResolver(IReadOnlyList<InstalledPathSurfaceContext> contexts)
    : this(
      contexts
        .Where(context => context.PathType != null)
        .Select(context => context.PathType!)
        .ToArray(),
      contexts
        .Where(context => context.QueueType != null)
        .Select(context => context.QueueType!)
        .ToArray()) {
    installedPathContexts = contexts
      .Where(context => context.PathType != null)
      .ToDictionary(
        context => context.PathType!.InternalName,
        StringComparer.OrdinalIgnoreCase);
    installedQueueContexts = contexts
      .Where(context => context.QueueType != null)
      .ToDictionary(
        context => context.QueueType!.InternalName,
        StringComparer.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Loads the exact installed PTD/QTD resources needed by the supplied path tiles.
  /// </summary>
  /// <remarks>
  /// Missing complete stub pairs remain unresolved so custom or legacy DAT surfaces keep the flat
  /// fallback. An incomplete pair, malformed decoded resource, or missing texture belonging to a
  /// resolved resource fails closed.
  /// </remarks>
  public static PathSurfaceResourceResolver LoadInstalled(
    string installRoot,
    IReadOnlyList<PathTile> tiles
  ) {
    ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
    ArgumentNullException.ThrowIfNull(tiles);

    var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installRoot));
    if (!Directory.Exists(root))
      throw new DirectoryNotFoundException(
        $"RCT3 installation directory was not found: '{root}'.");

    var pathNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var queueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var tile in tiles) {
      var systemName = tile.SurfaceSystemName;
      if (string.IsNullOrWhiteSpace(systemName)) continue;
      ValidateInstalledSystemName(systemName);
      var names = tile.IsQueue ? queueNames : pathNames;
      if (!names.Add(systemName)) continue;
      if (pathNames.Count + queueNames.Count > MaximumResourceCount)
        throw new InvalidDataException(
          $"DAT path surface count exceeds the resolver limit {MaximumResourceCount}.");
    }

    var contexts = new List<InstalledPathSurfaceContext>(
      pathNames.Count + queueNames.Count);
    try {
      foreach (var systemName in pathNames.OrderBy(name => name, StringComparer.Ordinal)) {
        var context = TryLoadInstalledContext(root, systemName, isQueue: false);
        if (context != null) contexts.Add(context);
      }
      foreach (var systemName in queueNames.OrderBy(name => name, StringComparer.Ordinal)) {
        var context = TryLoadInstalledContext(root, systemName, isQueue: true);
        if (context != null) contexts.Add(context);
      }
      return new PathSurfaceResourceResolver(contexts);
    } catch {
      DisposeContexts(contexts);
      throw;
    }
  }

  public bool TryResolve(
    PathTile tile,
    out ResolvedPathSurfaceResource? resource
  ) {
    ObjectDisposedException.ThrowIf(disposed, this);
    resource = null;
    var systemName = tile.SurfaceSystemName;
    if (string.IsNullOrWhiteSpace(systemName)) return false;
    if (systemName.Length > MaximumSystemNameLength)
      throw new InvalidDataException(
        $"DAT path surface system name exceeds {MaximumSystemNameLength} characters.");

    if (tile.IsQueue) {
      if (queueTypes.TryGetValue(systemName, out var queueType)) {
        resource = queueType;
        return true;
      }
      if (pathTypes.ContainsKey(systemName))
        throw TypeMismatch(systemName, expected: "QTD", actual: "PTD");
      return false;
    }

    if (pathTypes.TryGetValue(systemName, out var pathType)) {
      resource = pathType;
      return true;
    }
    if (queueTypes.ContainsKey(systemName))
      throw TypeMismatch(systemName, expected: "PTD", actual: "QTD");
    return false;
  }

  /// <summary>
  /// Resolves the installed surface texture for one tile. The returned texture remains owned by
  /// this resolver; assigning it to a material acquires the lease needed beyond resolver disposal.
  /// </summary>
  public bool TryResolveTexture(PathTile tile, out Texture? texture) {
    ObjectDisposedException.ThrowIf(disposed, this);
    texture = null;
    if (!TryResolve(tile, out var resource)) return false;

    var contexts = tile.IsQueue ? installedQueueContexts : installedPathContexts;
    if (!contexts.TryGetValue(resource!.SystemName, out var context)) return false;
    texture = context.ResolveTexture(tile.SurfaceColours);
    return true;
  }

  /// <inheritdoc />
  public void Dispose() {
    if (disposed) return;
    disposed = true;
    var contexts = installedPathContexts.Values
      .Concat(installedQueueContexts.Values)
      .ToArray();
    installedPathContexts =
      new Dictionary<string, InstalledPathSurfaceContext>(StringComparer.OrdinalIgnoreCase);
    installedQueueContexts =
      new Dictionary<string, InstalledPathSurfaceContext>(StringComparer.OrdinalIgnoreCase);
    DisposeContexts(contexts);
    GC.SuppressFinalize(this);
  }

  private static IReadOnlyDictionary<string, ResolvedPathTypeResource> BuildPathTypeIndex(
    IReadOnlyList<PathType> resources
  ) {
    var index = new Dictionary<string, ResolvedPathTypeResource>(
      resources.Count,
      StringComparer.OrdinalIgnoreCase);
    foreach (var resource in resources) {
      if (resource is null)
        throw new ArgumentException("PTD resources cannot contain null.", nameof(resources));
      ValidateResourceIdentity(resource.Name, resource.InternalName, "PTD");
      if (!index.TryAdd(resource.InternalName, new ResolvedPathTypeResource(resource)))
        throw new InvalidDataException(
          $"Path surface resolver has duplicate PTD internal name '{resource.InternalName}'.");
    }
    return index;
  }

  private static IReadOnlyDictionary<string, ResolvedQueueTypeResource> BuildQueueTypeIndex(
    IReadOnlyList<QueueType> resources
  ) {
    var index = new Dictionary<string, ResolvedQueueTypeResource>(
      resources.Count,
      StringComparer.OrdinalIgnoreCase);
    foreach (var resource in resources) {
      if (resource is null)
        throw new ArgumentException("QTD resources cannot contain null.", nameof(resources));
      ValidateResourceIdentity(resource.Name, resource.InternalName, "QTD");
      if (!index.TryAdd(resource.InternalName, new ResolvedQueueTypeResource(resource)))
        throw new InvalidDataException(
          $"Path surface resolver has duplicate QTD internal name '{resource.InternalName}'.");
    }
    return index;
  }

  private static void ValidateIdentifier(string value, string description) {
    if (string.IsNullOrWhiteSpace(value))
      throw new InvalidDataException(
        $"Path surface resolver has an empty {description}.");
    if (value.Length > MaximumSystemNameLength)
      throw new InvalidDataException(
        $"Path surface resolver {description} exceeds " +
        $"{MaximumSystemNameLength} characters.");
  }

  private static void ValidateResourceIdentity(
    string loaderName,
    string internalName,
    string resourceKind
  ) {
    ValidateIdentifier(loaderName, $"{resourceKind} loader name");
    ValidateIdentifier(internalName, $"{resourceKind} internal name");
    if (!string.Equals(loaderName, internalName, StringComparison.OrdinalIgnoreCase))
      throw new InvalidDataException(
        $"Path surface resolver cannot disambiguate {resourceKind} loader name '{loaderName}' " +
        $"from internal name '{internalName}'.");
  }

  private static InvalidDataException TypeMismatch(
    string systemName,
    string expected,
    string actual
  ) => new(
    $"DAT path surface '{systemName}' requires a {expected} resource but resolves only to {actual}.");

  private static InstalledPathSurfaceContext? TryLoadInstalledContext(
    string installRoot,
    string systemName,
    bool isQueue
  ) {
    var category = isQueue ? "Queue" : "Path";
    var overlay = Path.Combine(category, systemName, $"{systemName}_Stub");
    var commonPath = ResolveInstalledPath(installRoot, overlay + ".common.ovl");
    var uniquePath = ResolveInstalledPath(installRoot, overlay + ".unique.ovl");
    var commonExists = File.Exists(commonPath);
    var uniqueExists = File.Exists(uniquePath);
    if (!commonExists && !uniqueExists) return null;
    if (!commonExists || !uniqueExists)
      throw new FileNotFoundException(
        $"Installed path surface OVL pair is incomplete: '{commonPath}' and '{uniquePath}' " +
        "must both exist.",
        !commonExists ? commonPath : uniquePath);

    var catalog = new SceneryResourceCatalog(installRoot, overlay);
    try {
      var type = isQueue ? FileType.QueueType : FileType.PathType;
      var owner = catalog.Find(systemName, type)
        ?? throw new InvalidDataException(
          $"Installed {type.ToTagString().ToUpperInvariant()} stub '{overlay}' has no exact " +
          $"'{systemName}' resource.");
      if (isQueue) {
        var resources = QueueTypes.Extract(owner.Archive)
          .Where(resource => string.Equals(
            resource.Name,
            systemName,
            StringComparison.OrdinalIgnoreCase))
          .ToArray();
        if (resources.Length != 1)
          throw InvalidInstalledResourceCount(systemName, "QTD", resources.Length);
        return new InstalledPathSurfaceContext(catalog, owner, resources[0]);
      }

      var pathResources = PathTypes.Extract(owner.Archive)
        .Where(resource => string.Equals(
          resource.Name,
          systemName,
          StringComparison.OrdinalIgnoreCase))
        .ToArray();
      if (pathResources.Length != 1)
        throw InvalidInstalledResourceCount(systemName, "PTD", pathResources.Length);
      return new InstalledPathSurfaceContext(catalog, owner, pathResources[0]);
    } catch {
      catalog.Dispose();
      throw;
    }
  }

  private static InvalidDataException InvalidInstalledResourceCount(
    string systemName,
    string kind,
    int count
  ) => new(
    $"Installed path surface '{systemName}' resolves to {count} exact {kind} resources; " +
    "exactly one is required.");

  private static string ResolveInstalledPath(string installRoot, string relativePath) {
    var path = Path.GetFullPath(Path.Combine(installRoot, relativePath));
    var relative = Path.GetRelativePath(installRoot, path);
    if (Path.IsPathRooted(relative) ||
        string.Equals(relative, "..", StringComparison.Ordinal) ||
        relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
      throw new InvalidDataException(
        $"Installed path surface location '{relativePath}' leaves the RCT3 installation root.");
    return path;
  }

  private static void ValidateInstalledSystemName(string systemName) {
    ValidateIdentifier(systemName, "DAT path surface system name");
    if (!string.Equals(systemName, systemName.Trim(), StringComparison.Ordinal) ||
        Path.IsPathRooted(systemName) ||
        systemName is "." or ".." ||
        systemName.IndexOfAny(['\\', '/', ':']) >= 0 ||
        systemName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
      throw new InvalidDataException(
        $"DAT path surface system name '{systemName}' is not a safe installed resource name.");
  }

  private static void DisposeContexts(IEnumerable<InstalledPathSurfaceContext> contexts) {
    var errors = new List<Exception>();
    foreach (var context in contexts) {
      try {
        context.Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    if (errors.Count > 0) throw new AggregateException(errors);
  }

  private sealed class InstalledPathSurfaceContext : IDisposable {
    private readonly SceneryResourceCatalog catalog;
    private readonly SceneryResourceEntry owner;
    private readonly SceneryTextureResolver? queueTextures;
    private Texture? primaryTexture;
    private bool disposed;

    public PathType? PathType { get; }
    public QueueType? QueueType { get; }

    public InstalledPathSurfaceContext(
      SceneryResourceCatalog catalog,
      SceneryResourceEntry owner,
      PathType pathType
    ) {
      ArgumentNullException.ThrowIfNull(catalog);
      ArgumentNullException.ThrowIfNull(owner);
      ArgumentNullException.ThrowIfNull(pathType);
      this.catalog = catalog;
      this.owner = owner;
      PathType = pathType;
    }

    public InstalledPathSurfaceContext(
      SceneryResourceCatalog catalog,
      SceneryResourceEntry owner,
      QueueType queueType
    ) {
      ArgumentNullException.ThrowIfNull(catalog);
      ArgumentNullException.ThrowIfNull(owner);
      ArgumentNullException.ThrowIfNull(queueType);
      this.catalog = catalog;
      this.owner = owner;
      QueueType = queueType;
      queueTextures = new SceneryTextureResolver(catalog);
    }

    public Texture ResolveTexture(PathSurfaceColours? colours) {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (QueueType != null) {
        var found = colours.HasValue
          ? queueTextures!.TryResolve(
            QueueType.FlexiTextureRef,
            SceneryFlexiColours.FromSerialized(
              colours.Value.First,
              colours.Value.Second,
              colours.Value.Third),
            out var queueTexture)
          : queueTextures!.TryResolve(QueueType.FlexiTextureRef, out queueTexture);
        return found
          ? queueTexture!
          : throw new InvalidDataException(
            $"Installed QTD '{QueueType.InternalName}' flexi texture " +
            $"'{QueueType.FlexiTextureRef}' was not found.");
      }

      if (primaryTexture != null) return primaryTexture;
      var pathType = PathType
        ?? throw new InvalidOperationException("Installed path context has no PTD or QTD resource.");
      var textureResource = catalog.FindFrom(owner, pathType.Texture1Ref, FileType.Texture)
        ?? throw new InvalidDataException(
          $"Installed PTD '{pathType.InternalName}' primary texture " +
          $"'{pathType.Texture1Ref}' was not found.");
      primaryTexture = TextureLoader.LoadTexture(textureResource.Archive, textureResource.File);
      return primaryTexture;
    }

    public void Dispose() {
      if (disposed) return;
      disposed = true;
      var errors = new List<Exception>();
      try {
        queueTextures?.Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
      try {
        primaryTexture?.Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
      try {
        catalog.Dispose();
      } catch (Exception error) {
        errors.Add(error);
      }
      primaryTexture = null;
      if (errors.Count > 0) throw new AggregateException(errors);
    }
  }
}
