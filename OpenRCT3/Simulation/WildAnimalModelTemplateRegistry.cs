// Wild Animal Model Template Registry
//
// Copyright Â© 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Meshes;
using OpenCobra.OVL.Files;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace OpenRCT3.Simulation;

/// <summary>One distinct exact MDL source adapted into registry-owned neutral mesh batches.</summary>
internal sealed record WildAnimalModelTemplate(
  WildAnimalSpeciesModelResourceSource ModelSource,
  IReadOnlyList<ModelDefinitionMeshBatch> Batches
);

/// <summary>One serialized WAS variant retaining its exact bridge link and shared template.</summary>
internal sealed record WildAnimalModelVariantTemplateLink(
  WildAnimalSpeciesModelVariantLink VariantLink,
  WildAnimalModelTemplate Template
);

/// <summary>Owns neutral reusable MDL templates for one exact WAS-to-MDL bridge result.</summary>
/// <remarks>
/// The bridge records and decoded resources are borrowed. This registry owns only the adapted
/// <see cref="Mesh"/> instances. It does not infer materials, choose a variant, apply bones, place
/// animals, or interpret native matrices.
/// </remarks>
internal sealed class WildAnimalModelTemplateRegistry : IDisposable {
  private const int SerializedVariantCount = 4;
  private readonly Mesh[] ownedMeshes;
  private readonly Action<Mesh> disposeMesh;
  private bool disposed;

  private WildAnimalModelTemplateRegistry(
    IReadOnlyList<WildAnimalModelVariantTemplateLink> variants,
    IReadOnlyList<WildAnimalModelTemplate> templates,
    Mesh[] ownedMeshes,
    Action<Mesh> disposeMesh,
    int groupCount,
    int nonemptyGroupCount,
    int batchCount,
    ulong vertexCount,
    ulong indexCount
  ) {
    Variants = variants;
    Templates = templates;
    this.ownedMeshes = ownedMeshes;
    this.disposeMesh = disposeMesh;
    GroupCount = groupCount;
    NonemptyGroupCount = nonemptyGroupCount;
    BatchCount = batchCount;
    VertexCount = vertexCount;
    IndexCount = indexCount;
  }

  public IReadOnlyList<WildAnimalModelVariantTemplateLink> Variants { get; }
  public IReadOnlyList<WildAnimalModelTemplate> Templates { get; }
  public int VariantCount => Variants.Count;
  public int DistinctModelSourceCount => Templates.Count;
  public int GroupCount { get; }
  public int NonemptyGroupCount { get; }
  public int BatchCount { get; }
  public ulong VertexCount { get; }
  public ulong IndexCount { get; }
  public bool IsDisposed => disposed;

  public static WildAnimalModelTemplateRegistry Build(
    WildAnimalSpeciesModelResourceBridgeResult resources
  ) => Build(
    resources,
    ModelDefinitionMeshBuilder.BuildBatches,
    mesh => mesh.Dispose());

  internal static WildAnimalModelTemplateRegistry Build(
    WildAnimalSpeciesModelResourceBridgeResult resources,
    Func<ModelDefinition, IReadOnlyList<ModelDefinitionMeshBatch>> adaptModel,
    Action<Mesh> disposeMesh
  ) {
    ArgumentNullException.ThrowIfNull(resources);
    ArgumentNullException.ThrowIfNull(adaptModel);
    ArgumentNullException.ThrowIfNull(disposeMesh);
    var links = ValidateBridge(resources);

    var templatesBySource = new Dictionary<
      WildAnimalSpeciesModelResourceSource,
      WildAnimalModelTemplate>(ReferenceEqualityComparer.Instance);
    var templates = new List<WildAnimalModelTemplate>();
    var variants = new List<WildAnimalModelVariantTemplateLink>(SerializedVariantCount);
    var ownedMeshes = new List<Mesh>();
    var ownedMeshSet = new HashSet<Mesh>(ReferenceEqualityComparer.Instance);
    var groupCount = 0;
    var nonemptyGroupCount = 0;
    var batchCount = 0;
    var vertexCount = 0ul;
    var indexCount = 0ul;

    try {
      foreach (var link in links) {
        if (!templatesBySource.TryGetValue(link.ModelSource, out var template)) {
          var batches = Adapt(
            link.ModelSource,
            adaptModel,
            ownedMeshes,
            ownedMeshSet,
            out var groups,
            out var nonemptyGroups,
            out var vertices,
            out var indices);
          template = new WildAnimalModelTemplate(link.ModelSource, batches);
          templatesBySource.Add(link.ModelSource, template);
          templates.Add(template);
          groupCount = checked(groupCount + groups);
          nonemptyGroupCount = checked(nonemptyGroupCount + nonemptyGroups);
          batchCount = checked(batchCount + batches.Count);
          vertexCount = checked(vertexCount + vertices);
          indexCount = checked(indexCount + indices);
        }
        variants.Add(new WildAnimalModelVariantTemplateLink(link, template));
      }

      return new WildAnimalModelTemplateRegistry(
        Array.AsReadOnly(variants.ToArray()),
        Array.AsReadOnly(templates.ToArray()),
        ownedMeshes.ToArray(),
        disposeMesh,
        groupCount,
        nonemptyGroupCount,
        batchCount,
        vertexCount,
        indexCount);
    } catch (Exception buildError) {
      var cleanupErrors = DisposeMeshes(ownedMeshes, disposeMesh);
      if (cleanupErrors.Count != 0)
        throw new AggregateException([buildError, .. cleanupErrors]);
      throw;
    }
  }

  /// <summary>Releases every unique adapted template mesh once in reverse construction order.</summary>
  public void Dispose() {
    if (disposed) return;
    disposed = true;
    var errors = DisposeMeshes(ownedMeshes, disposeMesh);
    if (errors.Count != 0) throw new AggregateException(errors);
  }

  private static IReadOnlyList<WildAnimalSpeciesModelVariantLink> ValidateBridge(
    WildAnimalSpeciesModelResourceBridgeResult resources
  ) {
    ValidateTaggedIdentity(resources.SpeciesReference, "was", "species reference");
    if (resources.SpeciesFile == null || resources.Species == null)
      throw Invalid("has a null species file or decoded species");
    var speciesName = TaggedName(resources.SpeciesReference);
    if (resources.SpeciesFile.Type != FileType.WildAnimalSpecies ||
        !Same(resources.SpeciesFile.Name, speciesName) ||
        !Same(resources.Species.Name, speciesName))
      throw Invalid("species reference, file, and decoded identity disagree");
    RequirePairOwner(resources.SpeciesFile.Path, resources.SpeciesCommonPath, "species file");
    if (resources.Species.Variants == null ||
        resources.Species.Variants.Count != SerializedVariantCount)
      throw Invalid("decoded species does not contain exactly four variants");
    if (resources.Variants == null || resources.Variants.Count != SerializedVariantCount)
      throw Invalid("bridge does not contain exactly four variant links");

    var sourcesByIdentity = new Dictionary<string, WildAnimalSpeciesModelResourceSource>(
      StringComparer.OrdinalIgnoreCase);
    var validated = new WildAnimalSpeciesModelVariantLink[SerializedVariantCount];
    foreach (var index in Enumerable.Range(0, SerializedVariantCount)) {
      var link = resources.Variants[index];
      if (link == null || link.SerializedIndex != index)
        throw Invalid($"variant slot {index} is null, duplicated, or out of order");
      if (!ReferenceEquals(link.Variant, resources.Species.Variants[index]))
        throw Invalid($"variant slot {index} changed its exact decoded variant identity");
      if (link.ModelSource == null || link.ModelSource.File == null ||
          link.ModelSource.Resource == null)
        throw Invalid($"variant slot {index} has a null model source");
      ValidateModelSource(resources, link, index);

      var identity = link.ModelSource.Identity;
      if (sourcesByIdentity.TryGetValue(identity, out var existing)) {
        if (!ReferenceEquals(existing, link.ModelSource))
          throw Invalid(
            $"variant slot {index} duplicates model identity '{identity}' with a changed source");
      } else {
        sourcesByIdentity.Add(identity, link.ModelSource);
      }
      validated[index] = link;
    }
    return Array.AsReadOnly(validated);
  }

  private static void ValidateModelSource(
    WildAnimalSpeciesModelResourceBridgeResult resources,
    WildAnimalSpeciesModelVariantLink link,
    int index
  ) {
    ValidateTaggedIdentity(link.Variant.ModelReference, "mdl", $"variant {index} model");
    var name = TaggedName(link.Variant.ModelReference);
    var source = link.ModelSource;
    if (source.File.Type != FileType.Model ||
        !Same(source.File.Name, name) ||
        !Same(source.Resource.Name, name) ||
        !Same(source.Identity, link.Variant.ModelReference))
      throw Invalid($"variant slot {index} model reference, file, and decoded identity disagree");
    RequirePairOwner(source.File.Path, resources.ModelPackageCommonPath, "model file");
    if (!PathsEqual(source.Resource.SourcePath, source.File.Path))
      throw Invalid($"variant slot {index} decoded model changed its exact file owner");
  }

  private static IReadOnlyList<ModelDefinitionMeshBatch> Adapt(
    WildAnimalSpeciesModelResourceSource source,
    Func<ModelDefinition, IReadOnlyList<ModelDefinitionMeshBatch>> adaptModel,
    ICollection<Mesh> ownedMeshes,
    ISet<Mesh> ownedMeshSet,
    out int groupCount,
    out int nonemptyGroupCount,
    out ulong vertexCount,
    out ulong indexCount
  ) {
    var groups = source.Resource.Groups ??
      throw Invalid($"MDL '{source.Identity}' has a null group list");
    groupCount = groups.Count;
    nonemptyGroupCount = groups.Count(group => group?.Meshes is { Count: > 0 });
    var batches = adaptModel(source.Resource) ??
      throw Invalid($"MDL adapter returned null for '{source.Identity}'");

    // The adapter transfers every returned mesh into this transaction. Adopt the full list before
    // validating an early batch so a later returned mesh cannot leak when validation fails.
    var retained = new ModelDefinitionMeshBatch[batches.Count];
    var duplicateMesh = false;
    var disposedMesh = false;
    foreach (var index in Enumerable.Range(0, batches.Count)) {
      var batch = batches[index];
      retained[index] = batch;
      if (batch?.Mesh == null) continue;
      if (batch.Mesh.State == State.Disposed) {
        disposedMesh = true;
        continue;
      }
      if (!ownedMeshSet.Add(batch.Mesh)) {
        duplicateMesh = true;
        continue;
      }
      ownedMeshes.Add(batch.Mesh);
    }
    var expected = groups
      .SelectMany((group, groupIndex) => group.Meshes.Select(
        (mesh, meshIndex) => (groupIndex, meshIndex, mesh)))
      .ToArray();
    if (retained.Length != expected.Length)
      throw Invalid(
        $"MDL adapter returned {retained.Length} batches for '{source.Identity}', " +
        $"expected {expected.Length}");
    if (duplicateMesh)
      throw Invalid("MDL adapter reused a mesh across distinct owned batches");
    if (disposedMesh)
      throw Invalid("MDL adapter returned a disposed mesh");

    vertexCount = 0;
    indexCount = 0;
    foreach (var index in Enumerable.Range(0, retained.Length)) {
      var expectedBatch = expected[index];
      var batch = retained[index];
      ValidateBatch(source, expectedBatch.groupIndex, expectedBatch.meshIndex,
        expectedBatch.mesh, batch);
      vertexCount = checked(vertexCount + Convert.ToUInt64(batch.Mesh.Vertices.Count));
      indexCount = checked(indexCount + Convert.ToUInt64(batch.Mesh.Indices.Count));
    }
    return Array.AsReadOnly(retained);
  }

  private static void ValidateBatch(
    WildAnimalSpeciesModelResourceSource source,
    int groupIndex,
    int meshIndex,
    ModelMesh expected,
    ModelDefinitionMeshBatch? batch
  ) {
    var expectedName = $"{source.Resource.Name}/group-{groupIndex}/mesh-{meshIndex}";
    if (batch == null || batch.Mesh == null)
      throw Invalid($"MDL '{source.Identity}' adapter returned a null batch or mesh");
    if (batch.SourceGroupIndex != groupIndex || batch.SourceMeshIndex != meshIndex ||
        !string.Equals(batch.SourceMeshName, expectedName, StringComparison.Ordinal) ||
        !string.Equals(batch.Mesh.Name, expectedName, StringComparison.Ordinal))
      throw Invalid($"MDL '{source.Identity}' adapter changed batch order or identity");
    if (batch.Mesh.Vertices == null || batch.Mesh.Vertices.Count != expected.Vertices.Count)
      throw Invalid($"MDL '{source.Identity}' adapter changed vertex count");
    if (batch.Mesh.Indices == null || batch.Mesh.Indices.Count != expected.Indices.Count ||
        batch.Mesh.Indices.Count == 0 || batch.Mesh.Indices.Count % 3 != 0)
      throw Invalid($"MDL '{source.Identity}' adapter returned an invalid triangle list");
    foreach (var vertex in batch.Mesh.Vertices) {
      if (!IsFinite(vertex.Position) || !IsFinite(vertex.Normal) ||
          !IsFinite(vertex.TexCoord) || !IsFinite(vertex.Color))
        throw Invalid($"MDL '{source.Identity}' adapter returned a non-finite vertex");
    }
    foreach (var value in batch.Mesh.Indices) {
      if (value >= Convert.ToUInt32(batch.Mesh.Vertices.Count))
        throw Invalid($"MDL '{source.Identity}' adapter returned an out-of-range index");
    }
  }

  private static List<Exception> DisposeMeshes(
    IReadOnlyList<Mesh> meshes,
    Action<Mesh> disposeMesh
  ) {
    var errors = new List<Exception>();
    for (var index = meshes.Count - 1; index >= 0; index--) {
      try {
        disposeMesh(meshes[index]);
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    return errors;
  }

  private static void ValidateTaggedIdentity(string? value, string tag, string description) {
    if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(),
          StringComparison.Ordinal))
      throw Invalid($"{description} identity is empty or padded");
    var separator = value.LastIndexOf(':');
    if (separator <= 0 || separator != value.IndexOf(':') ||
        !value[(separator + 1)..].Equals(tag, StringComparison.OrdinalIgnoreCase))
      throw Invalid($"{description} '{value}' is not one exact name:{tag} identity");
  }

  private static string TaggedName(string value) => value[..value.LastIndexOf(':')];

  private static void RequirePairOwner(string? path, string commonPath, string description) {
    if (!PathsEqual(path, commonPath) && !PathsEqual(path, ToUniquePath(commonPath)))
      throw Invalid($"{description} is outside its exact declared OVL pair");
  }

  private static string ToUniquePath(string commonPath) =>
    commonPath.EndsWith(".common.ovl", StringComparison.OrdinalIgnoreCase)
      ? commonPath[..^".common.ovl".Length] + ".unique.ovl"
      : throw Invalid("declared model or species path is not a common OVL path");

  private static bool PathsEqual(string? left, string? right) {
    if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
    try {
      return string.Equals(
        Path.GetFullPath(left),
        Path.GetFullPath(right),
        StringComparison.OrdinalIgnoreCase);
    } catch (Exception error) when (
      error is ArgumentException or NotSupportedException or PathTooLongException) {
      return false;
    }
  }

  private static bool Same(string? left, string? right) =>
    string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

  private static bool IsFinite(Vector2 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private static bool IsFinite(Vector4 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) &&
    float.IsFinite(value.Z) && float.IsFinite(value.W);

  private static InvalidDataException Invalid(string message) =>
    new($"Invalid Wild animal model template registry: {message}.");
}
