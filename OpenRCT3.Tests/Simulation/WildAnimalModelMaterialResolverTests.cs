// Wild Animal Model Material Resolver Tests
//
// Copyright Â© 2026 OpenRCT3 Contributors. All rights reserved.

using OpenCobra.GDK;
using OpenCobra.GDK.Materials;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OpenRCT3.Serialization;
using OpenRCT3.Simulation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using DecodedTexture = OpenCobra.OVL.Files.Texture;
using RenderMesh = OpenCobra.GDK.Meshes.Mesh;
using RenderTexture = OpenCobra.GDK.Materials.Texture;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class WildAnimalModelMaterialResolverTests {
  [Test]
  public void ResolveMaterial_UsesExactCrossedGroupMeshBindingAndBatchIdentity() {
    using var fixture = ResolverFixture.Create(
      ["SIAlphaDS:txs", "SIOpaque:txs"],
      [2, 1],
      [new(0, 0, 1), new(0, 1, 0), new(1, 0, 1)]);
    using var resolver = new WildAnimalModelMaterialResolver(fixture.Resources);
    var groupZeroMeshOne = fixture.Batch(0, 1);
    var groupOneMeshZero = fixture.Batch(1, 0);
    using var alpha = (Textured)resolver.ResolveMaterial(
      fixture.Selection,
      groupZeroMeshOne);
    using var opaque = (Textured)resolver.ResolveMaterial(
      fixture.Selection,
      groupOneMeshZero);
    using var secondAlpha = (Textured)resolver.ResolveMaterial(
      fixture.Selection,
      groupZeroMeshOne);

    using (Assert.EnterMultipleScope()) {
      Assert.That(alpha.AlbedoTexture!.Name, Is.EqualTo("Texture0:tex"));
      Assert.That(alpha.AlbedoTexture.Pixels[0, 0],
        Is.EqualTo(new Rgba32(10, 20, 30, 255)));
      Assert.That(alpha.RenderState.BlendMode, Is.EqualTo(MaterialBlendMode.AlphaMask));
      Assert.That(alpha.CullBackFaces, Is.False);
      Assert.That(opaque.AlbedoTexture!.Name, Is.EqualTo("Texture1:tex"));
      Assert.That(opaque.AlbedoTexture.Pixels[0, 0],
        Is.EqualTo(new Rgba32(11, 21, 31, 255)));
      Assert.That(opaque.RenderState.BlendMode, Is.EqualTo(MaterialBlendMode.Opaque));
      Assert.That(opaque.CullBackFaces, Is.True);
      Assert.That(secondAlpha, Is.Not.SameAs(alpha));
      Assert.That(secondAlpha.AlbedoTexture, Is.SameAs(alpha.AlbedoTexture));
    }

    var copiedBatch = groupZeroMeshOne with { };
    Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.ResolveMaterial(fixture.Selection, copiedBatch)));
  }

  [Test]
  public void Dispose_ReleasesOwnerOnlyAfterEverySharedMaterialLease() {
    using var fixture = ResolverFixture.Create(
      ["SIOpaque:txs"],
      [2],
      [new(0, 0, 0), new(0, 1, 0)]);
    using var resolver = new WildAnimalModelMaterialResolver(fixture.Resources);
    var first = resolver.ResolveMaterial(fixture.Selection, fixture.Batch(0, 0));
    var second = resolver.ResolveMaterial(fixture.Selection, fixture.Batch(0, 1));
    var texture = first.AlbedoTexture!;

    fixture.Resources.Dispose();
    resolver.Dispose();
    resolver.Dispose();
    using (Assert.EnterMultipleScope()) {
      Assert.That(second.AlbedoTexture, Is.SameAs(texture));
      Assert.That(texture.State, Is.EqualTo(State.Uninitialized));
    }

    first.Dispose();
    Assert.That(texture.State, Is.EqualTo(State.Uninitialized));
    second.Dispose();
    Assert.That(texture.State, Is.EqualTo(State.Disposed));
  }

  [TestCase("SIOpaqueSpecular50:txs", MaterialBlendMode.Opaque, -1, true)]
  [TestCase("SIOpaqueSpecular50DS:txs", MaterialBlendMode.Opaque, -1, false)]
  [TestCase("SIAlphaMaskLowFur:txs", MaterialBlendMode.AlphaMask, 100, true)]
  [TestCase("SIAlphaMaskFenceDS:txs", MaterialBlendMode.AlphaMask, 208, false)]
  [TestCase("SIAlphaDS:txs", MaterialBlendMode.AlphaMask, 8, false)]
  public void ResolveMaterial_ApproximatesSupportedStyleFamiliesAndDsCulling(
    string style,
    MaterialBlendMode blendMode,
    int alphaReference,
    bool cullBackFaces
  ) {
    using var fixture = ResolverFixture.Create(
      [style],
      [1],
      [new(0, 0, 0)]);
    using var resolver = new WildAnimalModelMaterialResolver(fixture.Resources);
    using var material = (Textured)resolver.ResolveMaterial(
      fixture.Selection,
      fixture.Batch(0, 0));

    using (Assert.EnterMultipleScope()) {
      Assert.That(material.RenderState.BlendMode, Is.EqualTo(blendMode));
      var expectedAlphaReference = alphaReference < 0
        ? default(byte?)
        : Convert.ToByte(alphaReference);
      Assert.That(material.AlphaReference, Is.EqualTo(expectedAlphaReference));
      Assert.That(material.CullBackFaces, Is.EqualTo(cullBackFaces));
    }
  }

  [Test]
  public void Constructor_UnknownTxsFailsClosedAndReleasesPriorTexturesInReverse() {
    using var fixture = ResolverFixture.Create(
      ["SIOpaque:txs", "SIAlpha:txs", "SIWater:txs"],
      [3],
      [new(0, 0, 0), new(0, 1, 1), new(0, 2, 2)]);
    var created = new List<RenderTexture>();
    var released = new List<string>();
    var operations = new WildAnimalModelMaterialResolverOperations(
      material => {
        var pixels = material.Albedo.MipLevels[0].Clone();
        var texture = new RenderTexture(
          material.TextureReference,
          pixels.Width,
          pixels.Height,
          pixels);
        created.Add(texture);
        return texture;
      },
      texture => {
        released.Add(texture.Name);
        texture.Dispose();
      });

    Assert.Throws<InvalidDataException>(new Action(() =>
      new WildAnimalModelMaterialResolver(fixture.Resources, operations)));

    using (Assert.EnterMultipleScope()) {
      Assert.That(created.Select(texture => texture.Name),
        Is.EqualTo(new[] { "Texture0:tex", "Texture1:tex" }));
      Assert.That(released,
        Is.EqualTo(new[] { "Texture1:tex", "Texture0:tex" }));
      Assert.That(created.Select(texture => texture.State),
        Is.All.EqualTo(State.Disposed));
    }
  }

  [Test]
  public void ConstructorAndResolveMaterial_RejectDisposedInputsAndResolver() {
    using var disposedResources = ResolverFixture.Create(
      ["SIOpaque:txs"],
      [1],
      [new(0, 0, 0)]);
    disposedResources.Resources.Dispose();
    Assert.Throws<InvalidDataException>(new Action(() =>
      new WildAnimalModelMaterialResolver(disposedResources.Resources)));

    using var fixture = ResolverFixture.Create(
      ["SIOpaque:txs"],
      [1],
      [new(0, 0, 0)]);
    using var resolver = new WildAnimalModelMaterialResolver(fixture.Resources);
    var batch = fixture.Batch(0, 0);
    batch.Mesh.Dispose();
    Assert.Throws<InvalidDataException>(new Action(() =>
      resolver.ResolveMaterial(fixture.Selection, batch)));

    resolver.Dispose();
    Assert.Throws<ObjectDisposedException>(new Action(() =>
      resolver.ResolveMaterial(fixture.Selection, batch)));
  }

  private sealed class ResolverFixture : IDisposable {
    private readonly IReadOnlyList<ModelDefinitionMeshBatch> batches;

    private ResolverFixture(
      WildAnimalModelMaterialResourceBridgeResult resources,
      WildAnimalSavedVariantSelection selection,
      IReadOnlyList<ModelDefinitionMeshBatch> batches
    ) {
      Resources = resources;
      Selection = selection;
      this.batches = batches;
    }

    public WildAnimalModelMaterialResourceBridgeResult Resources { get; }
    public WildAnimalSavedVariantSelection Selection { get; }

    public static ResolverFixture Create(
      IReadOnlyList<string> styles,
      IReadOnlyList<int> groupMeshCounts,
      IReadOnlyList<BindingSpec> bindingSpecs
    ) {
      var root = Path.GetFullPath(Path.Combine(
        Path.GetTempPath(),
        "OpenRCT3-WildAnimalModelMaterialResolver-Fixture"));
      var modelPath = Path.Combine(root, "Animal.common.ovl");
      var texturePath = Path.Combine(root, "Animal.unique.ovl");
      var decoded = new List<DecodedTexture>();
      var materials = new List<WildAnimalModelTextureMaterialResource>();
      foreach (var index in Enumerable.Range(0, styles.Count)) {
        var name = $"Texture{index}";
        var albedo = new DecodedTexture(name, TextureFormat.A8R8G8B8, 1, 1);
        albedo.MipLevels[0] = new Image<Rgba32>(
          1,
          1,
          new Rgba32(
            Convert.ToByte(10 + index),
            Convert.ToByte(20 + index),
            Convert.ToByte(30 + index),
            255));
        decoded.Add(albedo);
        materials.Add(new(
          new OvlFile(name, FileType.Texture, texturePath),
          Convert.ToUInt32(1_000 + index),
          $"{name}:tex",
          styles[index],
          albedo));
      }

      var locatedBindings = new List<LocatedBinding>();
      var groups = new List<ModelGroup>();
      foreach (var groupIndex in Enumerable.Range(0, groupMeshCounts.Count)) {
        var specs = bindingSpecs
          .Where(spec => spec.Group == groupIndex)
          .OrderBy(spec => spec.Mesh)
          .ToArray();
        var surfaces = new List<ModelSurfaceRecord>();
        foreach (var spec in specs) {
          var surfaceIndex = surfaces.Count;
          surfaces.Add(new(
            Convert.ToUInt16(spec.Material),
            1,
            0,
            0,
            [(1u << 16) | Convert.ToUInt32(spec.Mesh)],
            []));
          locatedBindings.Add(new(spec, surfaceIndex));
        }
        groups.Add(new(
          Convert.ToUInt16(surfaces.Count),
          Convert.ToUInt16(groupMeshCounts[groupIndex]),
          0,
          0,
          [],
          0,
          Enumerable.Range(0, groupMeshCounts[groupIndex])
            .Select(_ => ModelMesh())
            .ToArray(),
          surfaces.ToArray()));
      }

      var definition = new ModelDefinition(
        "Animal",
        modelPath,
        500,
        340,
        0,
        0,
        0,
        0) {
        Strings = materials.Select(material => material.TextureFile.Name).ToArray(),
        Groups = groups.ToArray(),
      };
      var source = new WildAnimalSpeciesModelResourceSource(
        new OvlFile("Animal", FileType.Model, modelPath),
        definition);
      var batches = bindingSpecs.Select(spec => new ModelDefinitionMeshBatch(
        spec.Group,
        spec.Mesh,
        $"Animal/group-{spec.Group}/mesh-{spec.Mesh}",
        new RenderMesh([], []))).ToArray();
      var bindings = locatedBindings.Select(located =>
        new WildAnimalModelMeshMaterialBinding(
          source,
          located.Spec.Group,
          located.Spec.Mesh,
          located.Surface,
          Convert.ToUInt16(located.Spec.Material),
          materials[located.Spec.Material])).ToArray();
      var resources = new WildAnimalModelMaterialResourceBridgeResult(
        modelPath,
        materials,
        bindings,
        decoded);
      var variant = new WildAnimalSpeciesVariant("Animal:mdl", "Animal:wad");
      var variantLink = new WildAnimalSpeciesModelVariantLink(0, variant, source);
      var template = new WildAnimalModelTemplate(source, batches);
      var selection = new WildAnimalSavedVariantSelection(
        new DatWildAnimalData(1, 2, 3, true, true, 0),
        0,
        new WildAnimalModelVariantTemplateLink(variantLink, template));
      return new ResolverFixture(resources, selection, batches);
    }

    public ModelDefinitionMeshBatch Batch(int group, int mesh) =>
      batches.Single(batch =>
        batch.SourceGroupIndex == group && batch.SourceMeshIndex == mesh);

    public void Dispose() {
      Resources.Dispose();
      foreach (var batch in batches) batch.Mesh.Dispose();
    }

    private static ModelMesh ModelMesh() => new(
      0x1305,
      0,
      1,
      0,
      0,
      ushort.MaxValue,
      0,
      0,
      [],
      []);
  }

  private readonly record struct BindingSpec(int Group, int Mesh, int Material);
  private readonly record struct LocatedBinding(BindingSpec Spec, int Surface);
}
