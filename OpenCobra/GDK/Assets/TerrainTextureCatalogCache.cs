// Terrain Texture Catalog Cache
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.
using System.Collections.Concurrent;

namespace OpenCobra.GDK.Assets;

internal sealed class TerrainTextureCatalogCache(Func<string, TerrainTextureCatalog> loader) {
  private readonly ConcurrentDictionary<string, Lazy<TerrainTextureCatalog>> catalogs =
    new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

  public TerrainTextureCatalog Get(string ovlPath) {
    var key = Path.GetFullPath(ovlPath);
    var lazy = catalogs.GetOrAdd(key, path => new Lazy<TerrainTextureCatalog>(
      () => Load(path), LazyThreadSafetyMode.ExecutionAndPublication));
    try {
      return lazy.Value;
    } catch {
      Remove(key, lazy);
      throw;
    }
  }

  private TerrainTextureCatalog Load(string path) {
    var catalog = loader(path);
    catalog.SetDisposalCallback(disposedCatalog => Remove(path, disposedCatalog));
    return catalog;
  }

  private void Remove(string path, TerrainTextureCatalog catalog) {
    if (!catalogs.TryGetValue(path, out var lazy) || !lazy.IsValueCreated) return;
    if (ReferenceEquals(lazy.Value, catalog)) Remove(path, lazy);
  }

  private void Remove(string path, Lazy<TerrainTextureCatalog> lazy) =>
    ((ICollection<KeyValuePair<string, Lazy<TerrainTextureCatalog>>>)catalogs)
      .Remove(new KeyValuePair<string, Lazy<TerrainTextureCatalog>>(path, lazy));
}
