using System.Text;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class ModelAnimationsTests {
  [Test]
  public void Decode_PreservesExactFrameMajorTriplesAbsoluteLocalRotationsAndNames() {
    var definition = new ModelAnimationFixture().Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(definition.Name, Is.EqualTo("Walk"));
      Assert.That(definition.SourcePath, Is.EqualTo("fixture.common.ovl"));
      Assert.That(definition.DataAddress, Is.EqualTo(1_000));
      Assert.That(definition.DurationAt00, Is.EqualTo(1f / 30f));
      Assert.That(definition.FrameCountAt04, Is.EqualTo(2));
      Assert.That(definition.ValuesAt08Address, Is.EqualTo(1_088));
      Assert.That(definition.ValuesAt0CAddress, Is.EqualTo(1_096));
      Assert.That(definition.AnimatedBoneCountAt18, Is.EqualTo(2));
      Assert.That(definition.FullBoneCountAt1C, Is.EqualTo(3));
      Assert.That(definition.TriplesAt28Address, Is.EqualTo(1_108));
      Assert.That(definition.NormalizedFourTuplesAt2CAddress, Is.EqualTo(2_000));
      Assert.That(definition.ValuesAt08, Is.EqualTo(new uint[] { 0, 0 }));
      Assert.That(definition.ValuesAt0C, Is.EqualTo(new uint[] { 0, 0, 0 }));
      Assert.That(definition.TriplesAt28, Is.EqualTo(new[] {
        new ModelAnimationTriple(1, 2, 3),
        new ModelAnimationTriple(4, 5, 6),
        new ModelAnimationTriple(7, 8, 9),
        new ModelAnimationTriple(10, 11, 12),
      }));
      Assert.That(definition.NormalizedFourTuplesAt2C, Is.EqualTo(new[] {
        new ModelAnimationFourTuple(0, 0, 0, 1),
        new ModelAnimationFourTuple(0.70710677f, 0, 0, 0.70710677f),
        new ModelAnimationFourTuple(0, 0.70710677f, 0, 0.70710677f),
        new ModelAnimationFourTuple(0, 0, 0.70710677f, 0.70710677f),
        new ModelAnimationFourTuple(-0.70710677f, 0, 0, 0.70710677f),
        new ModelAnimationFourTuple(0, -0.70710677f, 0, 0.70710677f),
      }));
      Assert.That(definition.AnimatedBoneNames, Is.EqualTo(new[] { "BoneA", "BoneB" }));
      Assert.That(
        definition.FullBoneNames,
        Is.EqualTo(new[] { "Root", "BoneA", "BoneB" }));
    }
  }

  [Test]
  public void Decode_AcceptsOutOfLineArraysOnlyAtAnExactNextLoaderBoundary() {
    var fixture = new ModelAnimationFixture();
    fixture.MoveTailOutOfLine(addExactHeaderBoundary: true);

    var definition = fixture.Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(definition.ValuesAt08Address, Is.EqualTo(3_000));
      Assert.That(definition.ValuesAt0CAddress, Is.EqualTo(3_008));
      Assert.That(definition.TriplesAt28Address, Is.EqualTo(3_020));
      Assert.That(definition.TriplesAt28, Has.Count.EqualTo(4));
    }
  }

  [Test]
  public void Decode_AcceptsOutOfLineArraysAtAnExactFinalBlockBoundary() {
    var fixture = new ModelAnimationFixture();
    fixture.MoveTailOutOfLine(addExactHeaderBoundary: false);

    var definition = fixture.Decode();

    Assert.That(definition.ValuesAt08Address, Is.EqualTo(3_000));
  }

  [TestCase(MalformedModelAnimation.WrongArchiveVersion)]
  [TestCase(MalformedModelAnimation.WrongLoaderType)]
  [TestCase(MalformedModelAnimation.WrongArchiveHalf)]
  [TestCase(MalformedModelAnimation.MissingExactLoader)]
  [TestCase(MalformedModelAnimation.ExtraModelAnimationLoader)]
  [TestCase(MalformedModelAnimation.MissingOwnerInRegion)]
  [TestCase(MalformedModelAnimation.AliasedDataAddress)]
  [TestCase(MalformedModelAnimation.MissingLoaderDataRelocation)]
  [TestCase(MalformedModelAnimation.WrongLoaderDataRelocation)]
  [TestCase(MalformedModelAnimation.LoaderDataConflictsWithSymbolRef)]
  [TestCase(MalformedModelAnimation.TruncatedHeader)]
  [TestCase(MalformedModelAnimation.NegativeDuration)]
  [TestCase(MalformedModelAnimation.NonFiniteDuration)]
  [TestCase(MalformedModelAnimation.ZeroFrameCount)]
  [TestCase(MalformedModelAnimation.OversizedFrameCount)]
  [TestCase(MalformedModelAnimation.ZeroAnimatedBoneCount)]
  [TestCase(MalformedModelAnimation.ZeroFullBoneCount)]
  [TestCase(MalformedModelAnimation.AnimatedBoneCountExceedsFull)]
  [TestCase(MalformedModelAnimation.NonzeroOpaqueHeaderField)]
  [TestCase(MalformedModelAnimation.RelocatedOpaqueHeaderField)]
  [TestCase(MalformedModelAnimation.OpaqueHeaderFieldConflictsWithSymbolRef)]
  [TestCase(MalformedModelAnimation.MissingPointerRelocation)]
  [TestCase(MalformedModelAnimation.WrongPointerRelocation)]
  [TestCase(MalformedModelAnimation.PointerConflictsWithSymbolRef)]
  [TestCase(MalformedModelAnimation.NonAdjacentAt0C)]
  [TestCase(MalformedModelAnimation.NonAdjacentAt28)]
  [TestCase(MalformedModelAnimation.OutOfLineTailWithoutBoundary)]
  [TestCase(MalformedModelAnimation.TruncatedTail)]
  [TestCase(MalformedModelAnimation.NonzeroAt08)]
  [TestCase(MalformedModelAnimation.NonzeroAt0C)]
  [TestCase(MalformedModelAnimation.NonFiniteTriple)]
  [TestCase(MalformedModelAnimation.RelocatedTriple)]
  [TestCase(MalformedModelAnimation.TripleConflictsWithSymbolRef)]
  [TestCase(MalformedModelAnimation.TruncatedRotations)]
  [TestCase(MalformedModelAnimation.NonFiniteRotation)]
  [TestCase(MalformedModelAnimation.NonNormalizedRotation)]
  [TestCase(MalformedModelAnimation.RelocatedRotation)]
  [TestCase(MalformedModelAnimation.RotationConflictsWithSymbolRef)]
  [TestCase(MalformedModelAnimation.TailOverlapsHeader)]
  [TestCase(MalformedModelAnimation.RotationsOverlapTail)]
  [TestCase(MalformedModelAnimation.OwnedSymbolRef)]
  [TestCase(MalformedModelAnimation.MissingExtraChunk)]
  [TestCase(MalformedModelAnimation.MultipleExtraChunks)]
  [TestCase(MalformedModelAnimation.OversizedNameChunk)]
  [TestCase(MalformedModelAnimation.UnterminatedName)]
  [TestCase(MalformedModelAnimation.EmptyName)]
  [TestCase(MalformedModelAnimation.NonAsciiName)]
  [TestCase(MalformedModelAnimation.TooFewNames)]
  [TestCase(MalformedModelAnimation.TrailingNameBytes)]
  [TestCase(MalformedModelAnimation.DuplicateAnimatedName)]
  [TestCase(MalformedModelAnimation.DuplicateFullName)]
  [TestCase(MalformedModelAnimation.AnimatedNameMissingFromFull)]
  public void Decode_RejectsMalformedOrUnprovenData(MalformedModelAnimation malformed) {
    var fixture = new ModelAnimationFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledElephantProvesExactLayouts() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(root, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      root!, "WildAnimals", "elephant", "elephant_anims.common.ovl");
    Assert.That(path, Does.Exist, $"Installed Elephant animation OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var definitions = ModelAnimations.Extract(ovl);

    AssertInstalledDefinitions(
      definitions,
      path,
      new[] { "AdultElephantWalk", "BabyElephantWalk" },
      expectedFullBoneCount: 33,
      expectedAnimatedBoneCounts: new uint[] { 9, 10 },
      minimumFrameCount: 22,
      maximumFrameCount: 101);
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledOstrichProvesExactLayouts() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(root, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      root!, "WildAnimals", "Ostrich", "Ostrich_anims.common.ovl");
    Assert.That(path, Does.Exist, $"Installed Ostrich animation OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var definitions = ModelAnimations.Extract(ovl);

    AssertInstalledDefinitions(
      definitions,
      path,
      new[] { "MaleOstrichWalk", "BabyOstrichWalk" },
      expectedFullBoneCount: 27,
      expectedAnimatedBoneCounts: new uint[] { 7, 8 },
      minimumFrameCount: 15,
      maximumFrameCount: 101);
  }

  private static void AssertInstalledDefinitions(
    IReadOnlyList<ModelAnimationDefinition> definitions,
    string path,
    IReadOnlyList<string> requiredNames,
    uint expectedFullBoneCount,
    IReadOnlyList<uint> expectedAnimatedBoneCounts,
    uint minimumFrameCount,
    uint maximumFrameCount
  ) {
    using (Assert.EnterMultipleScope()) {
      Assert.That(definitions, Has.Count.EqualTo(54));
      Assert.That(definitions.Select(definition => definition.DataAddress), Is.Ordered.Ascending);
      Assert.That(
        definitions.Select(definition => definition.Name).Distinct(
          StringComparer.OrdinalIgnoreCase).ToArray(),
        Has.Length.EqualTo(54));
      Assert.That(
        requiredNames.All(required => definitions.Any(definition =>
          definition.Name.Equals(required, StringComparison.OrdinalIgnoreCase))),
        Is.True);
      Assert.That(definitions.Min(definition => definition.FrameCountAt04),
        Is.EqualTo(minimumFrameCount));
      Assert.That(definitions.Max(definition => definition.FrameCountAt04),
        Is.EqualTo(maximumFrameCount));
      Assert.That(
        definitions.Select(definition => definition.AnimatedBoneCountAt18)
          .Distinct().Order(),
        Is.EqualTo(expectedAnimatedBoneCounts));
    }

    foreach (var definition in definitions) {
      var tripleCount = checked(Convert.ToInt32(
        definition.FrameCountAt04 * definition.AnimatedBoneCountAt18));
      var rotationCount = checked(Convert.ToInt32(
        definition.FrameCountAt04 * definition.FullBoneCountAt1C));
      var fullNames = definition.FullBoneNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
      var durationAtThirtyFps = Convert.ToDouble(definition.FrameCountAt04 - 1) / 30.0;
      using (Assert.EnterMultipleScope()) {
        Assert.That(definition.SourcePath, Is.EqualTo(path));
        Assert.That(definition.DurationAt00, Is.GreaterThanOrEqualTo(0f));
        Assert.That(float.IsFinite(definition.DurationAt00), Is.True);
        Assert.That(
          Math.Abs(Convert.ToDouble(definition.DurationAt00) - durationAtThirtyFps),
          Is.LessThan(0.00001));
        Assert.That(definition.FullBoneCountAt1C, Is.EqualTo(expectedFullBoneCount));
        Assert.That(definition.ValuesAt08, Has.Count.EqualTo(
          Convert.ToInt32(definition.AnimatedBoneCountAt18)));
        Assert.That(definition.ValuesAt0C, Has.Count.EqualTo(
          Convert.ToInt32(definition.FullBoneCountAt1C)));
        Assert.That(definition.ValuesAt08, Has.All.Zero);
        Assert.That(definition.ValuesAt0C, Has.All.Zero);
        Assert.That(definition.TriplesAt28, Has.Count.EqualTo(tripleCount));
        Assert.That(
          definition.NormalizedFourTuplesAt2C,
          Has.Count.EqualTo(rotationCount));
        Assert.That(definition.TriplesAt28.All(IsFinite), Is.True);
        Assert.That(
          definition.NormalizedFourTuplesAt2C.All(IsFiniteAndNormalized),
          Is.True);
        Assert.That(definition.AnimatedBoneNames, Has.Count.EqualTo(
          Convert.ToInt32(definition.AnimatedBoneCountAt18)));
        Assert.That(definition.FullBoneNames, Has.Count.EqualTo(
          Convert.ToInt32(definition.FullBoneCountAt1C)));
        Assert.That(
          definition.AnimatedBoneNames.All(fullNames.Contains),
          Is.True);
      }
    }
  }

  private static bool IsFinite(ModelAnimationTriple value) =>
    float.IsFinite(value.Field00) &&
    float.IsFinite(value.Field04) &&
    float.IsFinite(value.Field08);

  private static bool IsFiniteAndNormalized(ModelAnimationFourTuple value) {
    if (!float.IsFinite(value.Field00) ||
        !float.IsFinite(value.Field04) ||
        !float.IsFinite(value.Field08) ||
        !float.IsFinite(value.Field0C)) return false;
    var lengthSquared = Convert.ToDouble(value.Field00) * value.Field00 +
      Convert.ToDouble(value.Field04) * value.Field04 +
      Convert.ToDouble(value.Field08) * value.Field08 +
      Convert.ToDouble(value.Field0C) * value.Field0C;
    return Math.Abs(lengthSquared - 1.0) <= 0.0001;
  }

  private sealed class ModelAnimationFixture {
    private const uint HeaderAddress = 1_000;
    private const uint InlineTailAddress = 1_088;
    private const uint RotationAddress = 2_000;
    private const int HeaderSize = 0x58;
    private const int AnimatedBoneCount = 2;
    private const int FullBoneCount = 3;
    private const int FrameCount = 2;
    private const int TripleCount = AnimatedBoneCount * FrameCount;
    private const int RotationCount = FullBoneCount * FrameCount;
    private const int TailSize =
      AnimatedBoneCount * sizeof(uint) +
      FullBoneCount * sizeof(uint) +
      TripleCount * 3 * sizeof(float);
    private const int RotationSize = RotationCount * 4 * sizeof(float);

    private readonly byte[] header = new byte[HeaderSize];
    private readonly byte[] tail = new byte[TailSize];
    private readonly byte[] rotations = new byte[RotationSize];
    private readonly FakeModelAnimationDataSource source = new();
    private OpenCobra.OVL.Version archiveVersion = OpenCobra.OVL.Version.Five;
    private OvlLoaderEntry owner = new("modelanim", HeaderAddress, "fixture.common.ovl", 900);

    public ModelAnimationFixture() {
      source.AddBytes(HeaderAddress, header);
      source.AddBytes(InlineTailAddress, tail);
      source.AddBytes(RotationAddress, rotations);
      source.ModelAnimationLoaders.Add(owner);
      source.RegionLoaders.Add(owner);
      source.Relocations.Add(owner.StructAddress + sizeof(uint), owner.DataAddress);

      WriteHeaderSingle(0, 1f / 30f);
      WriteHeaderUInt32(4, FrameCount);
      AddHeaderPointer(0x08, InlineTailAddress);
      AddHeaderPointer(0x0C, InlineTailAddress + AnimatedBoneCount * sizeof(uint));
      WriteHeaderUInt32(0x18, AnimatedBoneCount);
      WriteHeaderUInt32(0x1C, FullBoneCount);
      AddHeaderPointer(
        0x28,
        InlineTailAddress + (AnimatedBoneCount + FullBoneCount) * sizeof(uint));
      AddHeaderPointer(0x2C, RotationAddress);

      foreach (var index in Enumerable.Range(0, TripleCount * 3))
        WriteTailSingle((AnimatedBoneCount + FullBoneCount) * sizeof(uint) + index * 4,
          index + 1);
      WriteRotation(0, 0, 0, 0, 1);
      WriteRotation(1, 0.70710677f, 0, 0, 0.70710677f);
      WriteRotation(2, 0, 0.70710677f, 0, 0.70710677f);
      WriteRotation(3, 0, 0, 0.70710677f, 0.70710677f);
      WriteRotation(4, -0.70710677f, 0, 0, 0.70710677f);
      WriteRotation(5, 0, -0.70710677f, 0, 0.70710677f);
      SetNames("BoneA", "BoneB", "Root", "BoneA", "BoneB");
    }

    public ModelAnimationDefinition Decode() => ModelAnimations.DecodeAll(
      archiveVersion,
      new[] { new ModelAnimationDecodeInput("Walk", owner) },
      source).Single();

    public void MoveTailOutOfLine(bool addExactHeaderBoundary) {
      source.MoveBytes(InlineTailAddress, 3_000);
      AddHeaderPointer(0x08, 3_000);
      AddHeaderPointer(0x0C, 3_008);
      AddHeaderPointer(0x28, 3_020);
      if (addExactHeaderBoundary)
        source.RegionLoaders.Add(new OvlLoaderEntry(
          "mdl", HeaderAddress + HeaderSize, owner.SourcePath, 920));
    }

    public void MakeMalformed(MalformedModelAnimation malformed) {
      switch (malformed) {
        case MalformedModelAnimation.WrongArchiveVersion:
          archiveVersion = OpenCobra.OVL.Version.Four;
          break;
        case MalformedModelAnimation.WrongLoaderType:
          ReplaceOwner(owner with { Tag = "mdl" });
          break;
        case MalformedModelAnimation.WrongArchiveHalf:
          ReplaceOwner(owner with { SourcePath = "fixture.unique.ovl" });
          break;
        case MalformedModelAnimation.MissingExactLoader:
          source.ModelAnimationLoaders.Clear();
          break;
        case MalformedModelAnimation.ExtraModelAnimationLoader:
          source.ModelAnimationLoaders.Add(
            new OvlLoaderEntry("modelanim", 4_000, owner.SourcePath, 950));
          break;
        case MalformedModelAnimation.MissingOwnerInRegion:
          source.RegionLoaders.Clear();
          break;
        case MalformedModelAnimation.AliasedDataAddress:
          source.RegionLoaders.Add(owner with { StructAddress = 901 });
          break;
        case MalformedModelAnimation.MissingLoaderDataRelocation:
          source.Relocations.Remove(owner.StructAddress + sizeof(uint));
          break;
        case MalformedModelAnimation.WrongLoaderDataRelocation:
          source.Relocations[owner.StructAddress + sizeof(uint)] = HeaderAddress + 4;
          break;
        case MalformedModelAnimation.LoaderDataConflictsWithSymbolRef:
          AddForeignReference(owner.StructAddress + sizeof(uint));
          break;
        case MalformedModelAnimation.TruncatedHeader:
          source.ReplaceBytes(HeaderAddress, header[..^1]);
          break;
        case MalformedModelAnimation.NegativeDuration:
          WriteHeaderSingle(0, -1);
          break;
        case MalformedModelAnimation.NonFiniteDuration:
          WriteHeaderSingle(0, float.NaN);
          break;
        case MalformedModelAnimation.ZeroFrameCount:
          WriteHeaderUInt32(4, 0);
          break;
        case MalformedModelAnimation.OversizedFrameCount:
          WriteHeaderUInt32(4, 1_000_001);
          break;
        case MalformedModelAnimation.ZeroAnimatedBoneCount:
          WriteHeaderUInt32(0x18, 0);
          break;
        case MalformedModelAnimation.ZeroFullBoneCount:
          WriteHeaderUInt32(0x1C, 0);
          break;
        case MalformedModelAnimation.AnimatedBoneCountExceedsFull:
          WriteHeaderUInt32(0x18, FullBoneCount + 1);
          break;
        case MalformedModelAnimation.NonzeroOpaqueHeaderField:
          WriteHeaderUInt32(0x10, 1);
          break;
        case MalformedModelAnimation.RelocatedOpaqueHeaderField:
          source.Relocations[HeaderAddress + 0x10] = 3_000;
          break;
        case MalformedModelAnimation.OpaqueHeaderFieldConflictsWithSymbolRef:
          AddForeignReference(HeaderAddress + 0x10);
          break;
        case MalformedModelAnimation.MissingPointerRelocation:
          source.Relocations.Remove(HeaderAddress + 0x08);
          break;
        case MalformedModelAnimation.WrongPointerRelocation:
          source.Relocations[HeaderAddress + 0x08] = InlineTailAddress + 4;
          break;
        case MalformedModelAnimation.PointerConflictsWithSymbolRef:
          AddForeignReference(HeaderAddress + 0x08);
          break;
        case MalformedModelAnimation.NonAdjacentAt0C:
          AddHeaderPointer(0x0C, InlineTailAddress + 12);
          break;
        case MalformedModelAnimation.NonAdjacentAt28:
          AddHeaderPointer(0x28, InlineTailAddress + 24);
          break;
        case MalformedModelAnimation.OutOfLineTailWithoutBoundary:
          MoveTailOutOfLine(addExactHeaderBoundary: false);
          source.ReplaceBytes(HeaderAddress, [.. header, Convert.ToByte(0)]);
          break;
        case MalformedModelAnimation.TruncatedTail:
          source.ReplaceBytes(InlineTailAddress, tail[..^1]);
          break;
        case MalformedModelAnimation.NonzeroAt08:
          WriteTailUInt32(0, 1);
          break;
        case MalformedModelAnimation.NonzeroAt0C:
          WriteTailUInt32(AnimatedBoneCount * sizeof(uint), 1);
          break;
        case MalformedModelAnimation.NonFiniteTriple:
          WriteTailSingle((AnimatedBoneCount + FullBoneCount) * sizeof(uint), float.NaN);
          break;
        case MalformedModelAnimation.RelocatedTriple:
          source.Relocations[InlineTailAddress +
            (AnimatedBoneCount + FullBoneCount) * sizeof(uint)] = 3_000;
          break;
        case MalformedModelAnimation.TripleConflictsWithSymbolRef:
          AddForeignReference(InlineTailAddress +
            (AnimatedBoneCount + FullBoneCount) * sizeof(uint));
          break;
        case MalformedModelAnimation.TruncatedRotations:
          source.ReplaceBytes(RotationAddress, rotations[..^1]);
          break;
        case MalformedModelAnimation.NonFiniteRotation:
          WriteRotationSingle(0, float.PositiveInfinity);
          break;
        case MalformedModelAnimation.NonNormalizedRotation:
          WriteRotation(0, 1, 1, 1, 1);
          break;
        case MalformedModelAnimation.RelocatedRotation:
          source.Relocations[RotationAddress] = 3_000;
          break;
        case MalformedModelAnimation.RotationConflictsWithSymbolRef:
          AddForeignReference(RotationAddress);
          break;
        case MalformedModelAnimation.TailOverlapsHeader:
          AddHeaderPointer(0x08, HeaderAddress + 0x40);
          AddHeaderPointer(0x0C, HeaderAddress + 0x48);
          AddHeaderPointer(0x28, HeaderAddress + 0x54);
          source.RegionLoaders.Add(new OvlLoaderEntry(
            "mdl", HeaderAddress + HeaderSize, owner.SourcePath, 920));
          break;
        case MalformedModelAnimation.RotationsOverlapTail:
          AddHeaderPointer(
            0x2C,
            InlineTailAddress + (AnimatedBoneCount + FullBoneCount) * sizeof(uint));
          break;
        case MalformedModelAnimation.OwnedSymbolRef:
          source.ResourceReferences[4_000] = new OvlSymbolReference("Other:mdl", owner);
          break;
        case MalformedModelAnimation.MissingExtraChunk:
          source.HasExtraData = false;
          break;
        case MalformedModelAnimation.MultipleExtraChunks:
          source.ExtraChunks.Add(Array.Empty<byte>());
          break;
        case MalformedModelAnimation.OversizedNameChunk:
          source.ExtraChunks[0] = new byte[1024 * 1024 + 1];
          break;
        case MalformedModelAnimation.UnterminatedName:
          source.ExtraChunks[0] = source.ExtraChunks[0][..^1];
          break;
        case MalformedModelAnimation.EmptyName:
          SetNames("", "BoneB", "Root", "BoneA", "BoneB");
          break;
        case MalformedModelAnimation.NonAsciiName:
          source.ExtraChunks[0][0] = 255;
          break;
        case MalformedModelAnimation.TooFewNames:
          SetNames("BoneA", "BoneB", "Root", "BoneA");
          break;
        case MalformedModelAnimation.TrailingNameBytes:
          source.ExtraChunks[0] = [.. source.ExtraChunks[0], Convert.ToByte('x')];
          break;
        case MalformedModelAnimation.DuplicateAnimatedName:
          SetNames("BoneA", "bonea", "Root", "BoneA", "BoneB");
          break;
        case MalformedModelAnimation.DuplicateFullName:
          SetNames("BoneA", "BoneB", "Root", "BoneA", "bonea");
          break;
        case MalformedModelAnimation.AnimatedNameMissingFromFull:
          SetNames("BoneA", "BoneB", "Root", "BoneA", "Other");
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed), malformed, null);
      }
    }

    private void ReplaceOwner(OvlLoaderEntry replacement) {
      var oldOwner = owner;
      owner = replacement;
      source.ModelAnimationLoaders[source.ModelAnimationLoaders.IndexOf(oldOwner)] = owner;
      source.RegionLoaders[source.RegionLoaders.IndexOf(oldOwner)] = owner;
    }

    private void AddForeignReference(uint address) {
      var foreignOwner = new OvlLoaderEntry("mdl", 4_000, "fixture.common.ovl", 800);
      source.ResourceReferences[address] = new OvlSymbolReference("Other:mdl", foreignOwner);
    }

    private void SetNames(params string[] names) {
      var bytes = new List<byte>();
      foreach (var name in names) {
        bytes.AddRange(Encoding.ASCII.GetBytes(name));
        bytes.Add(0);
      }
      source.ExtraChunks[0] = bytes.ToArray();
    }

    private void AddHeaderPointer(int offset, uint address) {
      WriteHeaderUInt32(offset, address);
      source.Relocations[HeaderAddress + Convert.ToUInt32(offset)] = address;
    }

    private void WriteHeaderUInt32(int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(header, offset);

    private void WriteHeaderSingle(int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(header, offset);

    private void WriteTailUInt32(int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(tail, offset);

    private void WriteTailSingle(int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(tail, offset);

    private void WriteRotation(
      int index,
      float field00,
      float field04,
      float field08,
      float field0C
    ) {
      WriteRotationSingle(index * 16, field00);
      WriteRotationSingle(index * 16 + 4, field04);
      WriteRotationSingle(index * 16 + 8, field08);
      WriteRotationSingle(index * 16 + 12, field0C);
    }

    private void WriteRotationSingle(int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(rotations, offset);
  }

  private sealed class FakeModelAnimationDataSource : IModelAnimationDataSource {
    private readonly Dictionary<uint, byte[]> segments = [];

    public List<OvlLoaderEntry> ModelAnimationLoaders { get; } = [];
    IReadOnlyList<OvlLoaderEntry> IModelAnimationDataSource.ModelAnimationLoaders =>
      ModelAnimationLoaders;
    public Dictionary<uint, OvlSymbolReference> ResourceReferences { get; } = [];
    IReadOnlyDictionary<uint, OvlSymbolReference>
      IModelAnimationDataSource.ResourceReferences => ResourceReferences;
    public Dictionary<uint, uint> Relocations { get; } = [];
    public List<OvlLoaderEntry> RegionLoaders { get; } = [];
    public List<byte[]> ExtraChunks { get; } = [];
    public bool HasExtraData { get; set; } = true;

    public FakeModelAnimationDataSource() => ExtraChunks.Add([]);

    public void AddBytes(uint address, byte[] bytes) => segments.Add(address, bytes);

    public void ReplaceBytes(uint address, byte[] bytes) => segments[address] = bytes;

    public void MoveBytes(uint oldAddress, uint newAddress) {
      var bytes = segments[oldAddress];
      segments.Remove(oldAddress);
      segments.Add(newAddress, bytes);
    }

    public IReadOnlyList<OvlLoaderEntry> GetDataRegionLoaders(OvlLoaderEntry owner) =>
      RegionLoaders;

    public IReadOnlyList<KeyValuePair<uint, OvlSymbolReference>> GetOwnedResourceReferences(
      OvlLoaderEntry owner
    ) => ResourceReferences.Where(reference =>
      reference.Value.Owner.StructAddress == owner.StructAddress).ToList();

    public bool TryReadBytes(uint address, int length, out byte[] bytes) {
      foreach (var segment in segments) {
        var start = Convert.ToUInt64(segment.Key);
        var requested = Convert.ToUInt64(address);
        var end = requested + Convert.ToUInt64(length);
        var segmentEnd = start + Convert.ToUInt64(segment.Value.Length);
        if (requested < start || end > segmentEnd) continue;
        var offset = Convert.ToInt32(requested - start);
        bytes = segment.Value.AsSpan(offset, length).ToArray();
        return true;
      }
      bytes = [];
      return false;
    }

    public bool TryReadExtraData(
      OvlLoaderEntry owner,
      out IReadOnlyList<byte[]> chunks
    ) {
      chunks = ExtraChunks;
      return HasExtraData;
    }

    public bool TryGetRelocationSource(uint address, out uint value) =>
      Relocations.TryGetValue(address, out value);
  }
}

public enum MalformedModelAnimation {
  WrongArchiveVersion,
  WrongLoaderType,
  WrongArchiveHalf,
  MissingExactLoader,
  ExtraModelAnimationLoader,
  MissingOwnerInRegion,
  AliasedDataAddress,
  MissingLoaderDataRelocation,
  WrongLoaderDataRelocation,
  LoaderDataConflictsWithSymbolRef,
  TruncatedHeader,
  NegativeDuration,
  NonFiniteDuration,
  ZeroFrameCount,
  OversizedFrameCount,
  ZeroAnimatedBoneCount,
  ZeroFullBoneCount,
  AnimatedBoneCountExceedsFull,
  NonzeroOpaqueHeaderField,
  RelocatedOpaqueHeaderField,
  OpaqueHeaderFieldConflictsWithSymbolRef,
  MissingPointerRelocation,
  WrongPointerRelocation,
  PointerConflictsWithSymbolRef,
  NonAdjacentAt0C,
  NonAdjacentAt28,
  OutOfLineTailWithoutBoundary,
  TruncatedTail,
  NonzeroAt08,
  NonzeroAt0C,
  NonFiniteTriple,
  RelocatedTriple,
  TripleConflictsWithSymbolRef,
  TruncatedRotations,
  NonFiniteRotation,
  NonNormalizedRotation,
  RelocatedRotation,
  RotationConflictsWithSymbolRef,
  TailOverlapsHeader,
  RotationsOverlapTail,
  OwnedSymbolRef,
  MissingExtraChunk,
  MultipleExtraChunks,
  OversizedNameChunk,
  UnterminatedName,
  EmptyName,
  NonAsciiName,
  TooFewNames,
  TrailingNameBytes,
  DuplicateAnimatedName,
  DuplicateFullName,
  AnimatedNameMissingFromFull,
}
