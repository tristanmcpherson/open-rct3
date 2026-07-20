using System.Numerics;
using System.Reflection;
using System.Text;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class BoneAnimationsTests {
  [Test]
  public void Decode_ReadsExactBanLayoutNamesAndOrderedTracks() {
    var animation = new BoneAnimationFixture().Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(animation.Name, Is.EqualTo("synthetic"));
      Assert.That(animation.TotalTime, Is.EqualTo(7f));
      Assert.That(animation.Bones, Has.Count.EqualTo(2));
      Assert.That(animation.Bones[0].Name, Is.EqualTo("Scene Root"));
      Assert.That(animation.Bones[1].Name, Is.EqualTo("arm"));
      Assert.That(animation.Bones[1].Translations, Is.Empty);
      Assert.That(animation.Bones[1].Rotations, Is.Empty);
    }

    using (Assert.EnterMultipleScope()) {
      Assert.That(animation.Bones[0].Translations, Is.EqualTo(new[] {
        new BoneAnimationKeyframe(0f, new Vector3(1, 2, 3)),
        new BoneAnimationKeyframe(2f, new Vector3(4, 5, 6))
      }));
      Assert.That(animation.Bones[0].Rotations, Is.EqualTo(new[] {
        new BoneAnimationKeyframe(1f, new Vector3(0.1f, 0.2f, 0.3f))
      }));
    }
  }

  [Test]
  public void Decode_AcceptsRelocatedBoneNameAtAddressZero() {
    var fixture = new BoneAnimationFixture();
    fixture.UseRootBoneNameAtAddressZero();

    var animation = fixture.Decode();

    Assert.That(animation.Bones[0].Name, Is.EqualTo("Scene Root"));
  }

  [Test]
  public void Decode_EnforcesWholeDecodeByteAndObjectBudgets() {
    var byteFixture = new BoneAnimationFixture();
    var objectFixture = new BoneAnimationFixture();

    using (Assert.EnterMultipleScope()) {
      Assert.Throws<InvalidDataException>(new Action(() =>
        byteFixture.Decode(new BoneAnimationDecodeLimits(30, 1_000))));
      Assert.Throws<InvalidDataException>(new Action(() =>
        objectFixture.Decode(new BoneAnimationDecodeLimits(1_000, 4))));
    }
  }

  [Test]
  public void Extract_PreflightsCompleteLoaderIndexObjectCharge() {
    using var ovl = new Ovl("fixture");
    ((List<OvlLoaderEntry>)ovl.LoaderEntriesInOrder).Add(
      new OvlLoaderEntry("ban", 1_000, "fixture.common.ovl", 900));

    Assert.Throws<InvalidDataException>(new Action(() =>
      BoneAnimations.Extract(ovl, new BoneAnimationDecodeLimits(1_000, 3))));
  }

  [TestCase(MalformedBoneAnimation.TruncatedHeader)]
  [TestCase(MalformedBoneAnimation.MissingLoaderOwnership)]
  [TestCase(MalformedBoneAnimation.WrongHalfSameTagOwnership)]
  [TestCase(MalformedBoneAnimation.WrongStructSameTagOwnership)]
  [TestCase(MalformedBoneAnimation.NonFiniteTotalTime)]
  [TestCase(MalformedBoneAnimation.NegativeTotalTime)]
  [TestCase(MalformedBoneAnimation.OversizedBoneCount)]
  [TestCase(MalformedBoneAnimation.MissingBoneArrayRelocation)]
  [TestCase(MalformedBoneAnimation.TruncatedBoneArray)]
  [TestCase(MalformedBoneAnimation.UnterminatedBoneName)]
  [TestCase(MalformedBoneAnimation.DuplicateBoneName)]
  [TestCase(MalformedBoneAnimation.OversizedTranslationCount)]
  [TestCase(MalformedBoneAnimation.MissingTranslationRelocation)]
  [TestCase(MalformedBoneAnimation.TruncatedTranslationTrack)]
  [TestCase(MalformedBoneAnimation.NonFiniteKeyframe)]
  [TestCase(MalformedBoneAnimation.NegativeKeyframeTime)]
  [TestCase(MalformedBoneAnimation.DuplicateKeyframeTime)]
  [TestCase(MalformedBoneAnimation.EmptyTrackWithPointer)]
  public void Decode_RejectsMalformedOrUnboundedData(MalformedBoneAnimation malformed) {
    var fixture = new BoneAnimationFixture();
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [Test]
  public void Extract_FromEmbeddedSkyBeam_DecodesDeclaredAnimationsAndSkeletonNames() {
    WithSkyBeamOvl(ovl => {
      var animations = BoneAnimations.Extract(ovl);
      var shape = BoneShapes.Extract(ovl).Single(value => value.Name == "ZodiSkyBeam");
      var visual = SceneryItemVisuals.Extract(ovl).Single(value => value.Name == "ZodiSkyBeam");
      var expectedNames = new[] {
        "ZodiAnim1Idle",
        "ZodiAnim2Start",
        "ZodiAnim3Loop",
        "ZodiAnim4Stop"
      };
      var byName = animations.ToDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
      var selected = visual.Lods.Single().AnimationRefs.Select(reference => {
        Assert.That(reference, Does.EndWith(":ban").IgnoreCase);
        return byName[reference[..^":ban".Length]];
      }).ToArray();
      var shapeBones = shape.Bones.Select(value => value.Name).ToHashSet(
        StringComparer.OrdinalIgnoreCase);

      TestContext.Progress.WriteLine(
        $"SkyBeam BAN evidence: total={animations.Count}, selected={selected.Length}, " +
        $"bones={string.Join(',', selected.Select(value => value.Bones.Count))}, " +
        $"durations={string.Join(',', selected.Select(value => value.TotalTime))}");
      using (Assert.EnterMultipleScope()) {
        Assert.That(selected.Select(value => value.Name), Is.EqualTo(expectedNames));
        Assert.That(selected.All(value => value.Bones.Count > 0), Is.True);
        Assert.That(selected.All(value => float.IsFinite(value.TotalTime)), Is.True);
        Assert.That(selected.SelectMany(value => value.Bones)
          .All(value => shapeBones.Contains(value.Name)), Is.True);
      }
    });
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_InstalledVintageCar_DecodesDeclaredBanResources() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(root, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      root!, "Cars", "TrackedRideCars", "VintageCar", "VintageCar.common.ovl");
    Assert.That(path, Does.Exist, $"Installed BAN fixture is missing: {path}");

    using var ovl = Ovl.Load(path);
    var animations = BoneAnimations.Extract(ovl);
    TestContext.Progress.WriteLine(
      $"Installed BAN evidence: path={path}, total={animations.Count}, " +
      $"names={string.Join(',', animations.Select(value => value.Name))}, " +
      $"bones={string.Join(',', animations.Select(value => value.Bones.Count))}");

    using (Assert.EnterMultipleScope()) {
      Assert.That(animations.Select(value => value.Name),
        Is.EqualTo(new[] { "DoorClose", "DoorIdle", "DoorOpen" }));
      Assert.That(animations.All(value => float.IsFinite(value.TotalTime)), Is.True);
      Assert.That(animations.Select(value => value.Bones.Count), Is.EqualTo(new[] { 3, 3, 3 }));
      Assert.That(animations.SelectMany(value => value.Bones).Select(value => value.Name),
        Has.All.Not.Empty);
    }
  }

  private static void WithSkyBeamOvl(Action<Ovl> action) {
    var assembly = Assembly.GetExecutingAssembly();
    var resources = assembly.GetManifestResourceNames();
    var commonResource = resources.Single(name => name.EndsWith(
      ".CFRs.SkyBeam.SkyBeam.common.ovl", StringComparison.OrdinalIgnoreCase));
    var uniqueResource = commonResource[..^".common.ovl".Length] + ".unique.ovl";
    var tempDir = Directory.CreateTempSubdirectory().FullName;
    try {
      var commonPath = Path.Combine(tempDir, "fixture.common.ovl");
      CopyResource(assembly, commonResource, commonPath);
      CopyResource(assembly, uniqueResource, Path.Combine(tempDir, "fixture.unique.ovl"));
      using var ovl = Ovl.Load(commonPath);
      action(ovl);
    } finally {
      Directory.Delete(tempDir, recursive: true);
    }
  }

  private static void CopyResource(Assembly assembly, string resourceName, string path) {
    using var input = assembly.GetManifestResourceStream(resourceName);
    Assert.That(input, Is.Not.Null, $"Embedded resource '{resourceName}' not found.");
    using var output = File.Create(path);
    input.CopyTo(output);
  }

  public enum MalformedBoneAnimation {
    TruncatedHeader,
    MissingLoaderOwnership,
    WrongHalfSameTagOwnership,
    WrongStructSameTagOwnership,
    NonFiniteTotalTime,
    NegativeTotalTime,
    OversizedBoneCount,
    MissingBoneArrayRelocation,
    TruncatedBoneArray,
    UnterminatedBoneName,
    DuplicateBoneName,
    OversizedTranslationCount,
    MissingTranslationRelocation,
    TruncatedTranslationTrack,
    NonFiniteKeyframe,
    NegativeKeyframeTime,
    DuplicateKeyframeTime,
    EmptyTrackWithPointer
  }

  private sealed class BoneAnimationFixture {
    private const uint HeaderAddress = 100;
    private const uint BonesAddress = 200;
    private const uint TranslationsAddress = 300;
    private const uint RotationsAddress = 400;
    private const uint RootNameAddress = 500;
    private const uint ArmNameAddress = 600;

    private readonly FakeBoneAnimationDataSource source = new();
    private OvlLoaderEntry owner = new("ban", HeaderAddress, "fixture.common.ovl", 900);

    public BoneAnimationFixture() {
      var header = source.AddBlock(HeaderAddress, 12);
      WriteUInt32(header, 0, 2);
      WritePointer(header, HeaderAddress, 4, BonesAddress);
      WriteSingle(header, 8, 7f);

      var bones = source.AddBlock(BonesAddress, 40);
      WritePointer(bones, BonesAddress, 0, RootNameAddress);
      WriteUInt32(bones, 4, 2);
      WritePointer(bones, BonesAddress, 8, TranslationsAddress);
      WriteUInt32(bones, 12, 1);
      WritePointer(bones, BonesAddress, 16, RotationsAddress);
      WritePointer(bones, BonesAddress, 20, ArmNameAddress);

      var translations = source.AddBlock(TranslationsAddress, 32);
      WriteKeyframe(translations, 0, 0f, new Vector3(1, 2, 3));
      WriteKeyframe(translations, 16, 2f, new Vector3(4, 5, 6));
      var rotations = source.AddBlock(RotationsAddress, 16);
      WriteKeyframe(rotations, 0, 1f, new Vector3(0.1f, 0.2f, 0.3f));
      source.AddBlock(RootNameAddress, Encoding.ASCII.GetBytes("Scene Root\0"));
      source.AddBlock(ArmNameAddress, Encoding.ASCII.GetBytes("arm\0"));
      source.Loaders.Add(owner);
    }

    public BoneAnimation Decode() => BoneAnimations.Decode("synthetic", owner, source);
    public BoneAnimation Decode(BoneAnimationDecodeLimits limits) =>
      BoneAnimations.Decode("synthetic", owner, source, limits);

    public void UseRootBoneNameAtAddressZero() {
      WritePointer(source.Blocks[BonesAddress], BonesAddress, 0, 0);
      source.AddBlock(0, Encoding.ASCII.GetBytes("Scene Root\0"));
    }

    public void MakeMalformed(MalformedBoneAnimation malformed) {
      switch (malformed) {
        case MalformedBoneAnimation.TruncatedHeader:
          source.ReplaceBlock(HeaderAddress, source.Blocks[HeaderAddress][..11]);
          break;
        case MalformedBoneAnimation.MissingLoaderOwnership:
          source.Loaders.Clear();
          break;
        case MalformedBoneAnimation.WrongHalfSameTagOwnership:
          owner = owner with { SourcePath = "fixture.unique.ovl" };
          break;
        case MalformedBoneAnimation.WrongStructSameTagOwnership:
          owner = owner with { StructAddress = owner.StructAddress + 1 };
          break;
        case MalformedBoneAnimation.NonFiniteTotalTime:
          WriteSingle(source.Blocks[HeaderAddress], 8, float.NaN);
          break;
        case MalformedBoneAnimation.NegativeTotalTime:
          WriteSingle(source.Blocks[HeaderAddress], 8, -1f);
          break;
        case MalformedBoneAnimation.OversizedBoneCount:
          WriteUInt32(source.Blocks[HeaderAddress], 0, 65_537);
          break;
        case MalformedBoneAnimation.MissingBoneArrayRelocation:
          source.Relocations.Remove(HeaderAddress + 4);
          break;
        case MalformedBoneAnimation.TruncatedBoneArray:
          source.ReplaceBlock(BonesAddress, source.Blocks[BonesAddress][..39]);
          break;
        case MalformedBoneAnimation.UnterminatedBoneName:
          source.ReplaceBlock(RootNameAddress, Encoding.ASCII.GetBytes("Scene Root"));
          break;
        case MalformedBoneAnimation.DuplicateBoneName:
          WritePointer(source.Blocks[BonesAddress], BonesAddress, 20, RootNameAddress);
          break;
        case MalformedBoneAnimation.OversizedTranslationCount:
          WriteUInt32(source.Blocks[BonesAddress], 4, 1_000_001);
          break;
        case MalformedBoneAnimation.MissingTranslationRelocation:
          source.Relocations.Remove(BonesAddress + 8);
          break;
        case MalformedBoneAnimation.TruncatedTranslationTrack:
          source.ReplaceBlock(
            TranslationsAddress, source.Blocks[TranslationsAddress][..31]);
          break;
        case MalformedBoneAnimation.NonFiniteKeyframe:
          WriteSingle(source.Blocks[TranslationsAddress], 4, float.PositiveInfinity);
          break;
        case MalformedBoneAnimation.NegativeKeyframeTime:
          WriteSingle(source.Blocks[TranslationsAddress], 0, -1f);
          break;
        case MalformedBoneAnimation.DuplicateKeyframeTime:
          WriteSingle(source.Blocks[TranslationsAddress], 16, 0f);
          break;
        case MalformedBoneAnimation.EmptyTrackWithPointer:
          WriteUInt32(source.Blocks[BonesAddress], 24, 0);
          WritePointer(source.Blocks[BonesAddress], BonesAddress, 28, TranslationsAddress);
          break;
      }
    }

    private void WritePointer(byte[] bytes, uint blockAddress, int offset, uint value) {
      WriteUInt32(bytes, offset, value);
      source.Relocations[blockAddress + Convert.ToUInt32(offset)] = value;
    }

    private static void WriteKeyframe(byte[] bytes, int offset, float time, Vector3 value) {
      WriteSingle(bytes, offset, time);
      WriteSingle(bytes, offset + 4, value.X);
      WriteSingle(bytes, offset + 8, value.Y);
      WriteSingle(bytes, offset + 12, value.Z);
    }

    private static void WriteSingle(byte[] bytes, int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);
  }

  private sealed class FakeBoneAnimationDataSource : IBoneAnimationDataSource {
    public Dictionary<uint, byte[]> Blocks { get; } = [];
    public Dictionary<uint, uint> Relocations { get; } = [];
    public List<OvlLoaderEntry> Loaders { get; } = [];

    public byte[] AddBlock(uint address, int length) {
      var bytes = new byte[length];
      Blocks.Add(address, bytes);
      return bytes;
    }

    public void AddBlock(uint address, byte[] bytes) => Blocks.Add(address, bytes);
    public void ReplaceBlock(uint address, byte[] bytes) => Blocks[address] = bytes;

    public bool HasExactLoader(OvlLoaderEntry owner) => Loaders.Any(loader =>
      loader.DataAddress == owner.DataAddress &&
      loader.StructAddress == owner.StructAddress &&
      string.Equals(loader.Tag, owner.Tag, StringComparison.OrdinalIgnoreCase) &&
      string.Equals(loader.SourcePath, owner.SourcePath, StringComparison.OrdinalIgnoreCase));

    public bool TryReadBytes(uint address, int length, out byte[] bytes) {
      foreach (var block in Blocks) {
        if (address < block.Key) continue;
        var offset = Convert.ToUInt64(address - block.Key);
        if (offset + Convert.ToUInt64(length) > Convert.ToUInt64(block.Value.Length)) continue;
        bytes = block.Value.AsSpan(Convert.ToInt32(offset), length).ToArray();
        return true;
      }
      bytes = [];
      return false;
    }

    public bool TryGetRelocationSource(uint address, out uint value) =>
      Relocations.TryGetValue(address, out value);

    public bool TryGetNullTerminatedStringByteLength(
      uint address,
      int maximumLength,
      out int length
    ) {
      foreach (var block in Blocks) {
        if (address < block.Key) continue;
        var offset = Convert.ToUInt64(address - block.Key);
        if (offset >= Convert.ToUInt64(block.Value.Length)) continue;
        var start = Convert.ToInt32(offset);
        var available = Math.Min(maximumLength, block.Value.Length - start);
        var end = Array.IndexOf(block.Value, Convert.ToByte(0), start, available);
        length = end < 0 ? 0 : end - start;
        return end >= 0;
      }
      length = 0;
      return false;
    }

    public bool TryReadNullTerminatedString(uint address, int maximumLength, out string value) {
      foreach (var block in Blocks) {
        if (address < block.Key) continue;
        var offset = Convert.ToUInt64(address - block.Key);
        if (offset >= Convert.ToUInt64(block.Value.Length)) continue;
        var start = Convert.ToInt32(offset);
        var available = Math.Min(maximumLength, block.Value.Length - start);
        var end = Array.IndexOf(block.Value, Convert.ToByte(0), start, available);
        if (end < 0) break;
        value = Encoding.ASCII.GetString(block.Value, start, end - start);
        return true;
      }
      value = string.Empty;
      return false;
    }
  }
}
