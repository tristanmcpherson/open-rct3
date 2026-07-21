using OpenCobra.GDK.Materials;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using DecodedTexture = OpenCobra.OVL.Files.Texture;
using RenderTexture = OpenCobra.GDK.Materials.Texture;

namespace OVL.Tests.GDK;

[TestFixture]
public class TextureSamplingTests {
  [Test]
  public void AuthoredMipmaps_UploadEachExactLevelWithoutGeneration() {
    var commands = new RecordingTextureUploadCommands();
    var alphaMask = new Rgba32(40, 50, 60, 31);
    var levels = new[] {
      CreateUpload(0, 4, 2, new Rgba32(10, 20, 30, 255)),
      CreateUpload(1, 2, 1, alphaMask),
      CreateUpload(2, 1, 1, new Rgba32(70, 80, 90, 0)),
    };

    TextureUploadExecutor.Execute(
      commands, 42, levels, TextureSamplingMode.AuthoredMipmaps);

    using (Assert.EnterMultipleScope()) {
      Assert.That(commands.Bindings, Is.EqualTo(new uint[] { 42, 0 }));
      Assert.That(
        commands.Uploads.Select(upload => (upload.Level, upload.Width, upload.Height)),
        Is.EqualTo(new[] { (0, 4, 2), (1, 2, 1), (2, 1, 1) }));
      Assert.That(commands.Uploads[1].Pixels, Is.All.EqualTo(alphaMask));
      Assert.That(commands.GenerateMipmapsCalls, Is.Zero);
      Assert.That(
        commands.Parameters[TextureParameterName.TextureMaxLevel], Is.EqualTo(2));
      Assert.That(
        commands.Parameters[TextureParameterName.TextureMinFilter],
        Is.EqualTo(Convert.ToInt32(TextureMinFilter.LinearMipmapLinear)));
      Assert.That(
        commands.Parameters[TextureParameterName.TextureMagFilter],
        Is.EqualTo(Convert.ToInt32(TextureMagFilter.Linear)));
    }
  }

  [TestCase(
    TextureSamplingMode.GeneratedMipmaps,
    1,
    TextureMinFilter.LinearMipmapLinear)]
  [TestCase(TextureSamplingMode.Linear, 0, TextureMinFilter.Linear)]
  public void BaseOnlyPolicies_UseTheirDeclaredGenerationAndFilter(
    TextureSamplingMode samplingMode,
    int expectedGenerateCalls,
    TextureMinFilter expectedFilter
  ) {
    var commands = new RecordingTextureUploadCommands();
    var levels = new[] { CreateUpload(0, 2, 2, new Rgba32(10, 20, 30, 255)) };

    TextureUploadExecutor.Execute(commands, 7, levels, samplingMode);

    using (Assert.EnterMultipleScope()) {
      Assert.That(commands.Uploads, Has.Count.EqualTo(1));
      Assert.That(commands.GenerateMipmapsCalls, Is.EqualTo(expectedGenerateCalls));
      Assert.That(
        commands.Parameters[TextureParameterName.TextureMinFilter],
        Is.EqualTo(Convert.ToInt32(expectedFilter)));
      Assert.That(
        commands.Parameters.ContainsKey(TextureParameterName.TextureMaxLevel), Is.False);
    }
  }

  [Test]
  public void FromDecoded_UploadsContiguousAuthoredMipSnapshots() {
    using var decoded = new DecodedTexture(
      "authored", TextureFormat.A8R8G8B8, 4, 4, 3);
    decoded.MipLevels[0] = new Image<Rgba32>(4, 4, new Rgba32(10, 20, 30, 255));
    decoded.MipLevels[1] = new Image<Rgba32>(2, 2, new Rgba32(40, 50, 60, 127));
    decoded.MipLevels[2] = new Image<Rgba32>(1, 1, new Rgba32(70, 80, 90, 31));
    using var texture = RenderTexture.FromDecoded("authored", decoded);
    decoded.MipLevels[1][0, 0] = new Rgba32(200, 201, 202, 0);
    var gpu = new RecordingTextureGpuApi();

    texture.Upload(gpu);

    using (Assert.EnterMultipleScope()) {
      Assert.That(texture.SamplingMode, Is.EqualTo(TextureSamplingMode.AuthoredMipmaps));
      Assert.That(texture.MipLevels, Has.Count.EqualTo(3));
      Assert.That(gpu.SamplingMode, Is.EqualTo(TextureSamplingMode.AuthoredMipmaps));
      Assert.That(
        gpu.Uploads.Select(upload => (upload.Level, upload.Width, upload.Height)),
        Is.EqualTo(new[] { (0, 4, 4), (1, 2, 2), (2, 1, 1) }));
      Assert.That(gpu.Uploads[1].Pixels[0], Is.EqualTo(new Rgba32(40, 50, 60, 127)));
    }
  }

  [Test]
  public void FromDecoded_BaseOnlyUsesGeneratedFallbackAndGapFailsClosed() {
    using var decoded = new DecodedTexture(
      "base-only", TextureFormat.A8R8G8B8, 4, 4, 3);
    decoded.MipLevels[0] = new Image<Rgba32>(4, 4, new Rgba32(10, 20, 30, 255));
    using var texture = RenderTexture.FromDecoded("base-only", decoded);
    decoded.MipLevels[2] = new Image<Rgba32>(1, 1, new Rgba32(70, 80, 90, 255));

    using (Assert.EnterMultipleScope()) {
      Assert.That(texture.SamplingMode, Is.EqualTo(TextureSamplingMode.GeneratedMipmaps));
      Assert.That(texture.MipLevels, Has.Count.EqualTo(1));
      Assert.Throws<InvalidDataException>(new Action(
        () => RenderTexture.FromDecoded("gapped", decoded)));
    }
  }

  [Test]
  public void CacheKey_IncludesEveryMipAndSamplingPolicy() {
    using var first = CreateAuthoredTexture(new Rgba32(10, 20, 30, 127));
    using var differentLowerMip = CreateAuthoredTexture(new Rgba32(30, 20, 10, 127));
    using var generated = new RenderTexture(
      "shared",
      2,
      2,
      new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)),
      samplingMode: TextureSamplingMode.GeneratedMipmaps);
    using var linear = new RenderTexture(
      "shared",
      2,
      2,
      new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255)),
      samplingMode: TextureSamplingMode.Linear);

    using (Assert.EnterMultipleScope()) {
      Assert.That(first.CacheKey, Is.Not.EqualTo(differentLowerMip.CacheKey));
      Assert.That(generated.CacheKey, Is.Not.EqualTo(linear.CacheKey));
    }
  }

  [Test]
  public void UndefinedSampling_FailsBeforeConstructionOrGpuCommands() {
    var undefined = (TextureSamplingMode)999;
    using var image = new Image<Rgba32>(1, 1, new Rgba32(1, 2, 3, 255));
    var commands = new RecordingTextureUploadCommands();
    var levels = new[] { CreateUpload(0, 1, 1, new Rgba32(1, 2, 3, 255)) };

    using (Assert.EnterMultipleScope()) {
      Assert.Throws<ArgumentOutOfRangeException>(new Action(() => new RenderTexture(
        "invalid", 1, 1, image, samplingMode: undefined)));
      Assert.Throws<ArgumentOutOfRangeException>(new Action(
        () => TextureUploadExecutor.Execute(commands, 42, levels, undefined)));
      Assert.That(commands.Bindings, Is.Empty);
      Assert.That(commands.Uploads, Is.Empty);
    }
  }

  private static RenderTexture CreateAuthoredTexture(Rgba32 lowerMipColor) => new(
    "shared",
    2,
    2,
    new Image<Rgba32>[] {
      new(2, 2, new Rgba32(1, 2, 3, 255)),
      new(1, 1, lowerMipColor),
    });

  private static TextureMipUpload CreateUpload(
    int level,
    int width,
    int height,
    Rgba32 color
  ) => new(level, width, height, Enumerable.Repeat(color, width * height).ToArray());

  private sealed class RecordingTextureUploadCommands : ITextureUploadCommands {
    public List<uint> Bindings { get; } = [];
    public List<RecordedUpload> Uploads { get; } = [];
    public int GenerateMipmapsCalls { get; private set; }
    public Dictionary<TextureParameterName, int> Parameters { get; } = [];

    public void BindTexture(uint handle) => Bindings.Add(handle);

    public void UploadLevel(TextureMipUpload mipLevel) => Uploads.Add(new(
      mipLevel.Level,
      mipLevel.Width,
      mipLevel.Height,
      mipLevel.Pixels.ToArray()));

    public void GenerateMipmaps() => GenerateMipmapsCalls++;

    public void SetParameter(TextureParameterName name, int value) => Parameters[name] = value;
  }

  private sealed class RecordingTextureGpuApi : RenderTexture.IGpuApi {
    public List<RecordedUpload> Uploads { get; } = [];
    public TextureSamplingMode? SamplingMode { get; private set; }

    public uint CreateTexture() => 12;

    public void UploadTexture(
      uint handle,
      IReadOnlyList<TextureMipUpload> mipLevels,
      TextureSamplingMode samplingMode
    ) {
      SamplingMode = samplingMode;
      Uploads.AddRange(mipLevels.Select(mipLevel => new RecordedUpload(
        mipLevel.Level,
        mipLevel.Width,
        mipLevel.Height,
        mipLevel.Pixels.ToArray())));
    }

    public void DeleteTexture(uint handle) { }
  }

  private readonly record struct RecordedUpload(
    int Level,
    int Width,
    int Height,
    Rgba32[] Pixels);
}
