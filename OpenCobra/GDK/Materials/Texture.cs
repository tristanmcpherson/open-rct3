// Texture
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using DryIoc;
using OpenCobra.GDK.Game;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Buffers.Binary;
using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenCobra.GDK.Materials;

public enum TextureSamplingMode {
  AuthoredMipmaps,
  GeneratedMipmaps,
  Linear,
}

public class Texture : IResource, IDisposable {
  public static readonly string UniformName = "u_Texture";
  private readonly object lifetimeLock = new();
  private readonly Image<Rgba32>[] ownedMipLevels;
  private readonly TextureMipUpload[] uploadMipLevels;
  private bool disposed;
  private bool ownerReleased;
  private int leaseCount;
  private uint handle;

  [Category("Design")]
  public string Name { get; private set; }
  [Category("Appearance")]
  public int Width { get; }
  [Category("Appearance")]
  public int Height { get; }
  [Category("Appearance")]
  public Recolorable Recolorable { get; }
  [Category("Appearance")]
  public TextureSamplingMode SamplingMode { get; }
  [Category("Appearance")]
  public Image<Rgba32> Pixels => ownedMipLevels[0];

  [Browsable(false)]
  public IReadOnlyList<Image<Rgba32>> MipLevels { get; }

  [Browsable(false)]
  public TextureCacheKey CacheKey { get; }

  public Texture(
    string name,
    int width,
    int height,
    Image<Rgba32> texture,
    Recolorable recolorable = 0,
    TextureSamplingMode samplingMode = TextureSamplingMode.GeneratedMipmaps
  ) : this(name, width, height, [texture], recolorable, samplingMode) { }

  public Texture(
    string name,
    int width,
    int height,
    IReadOnlyList<Image<Rgba32>> mipLevels,
    Recolorable recolorable = 0,
    TextureSamplingMode samplingMode = TextureSamplingMode.AuthoredMipmaps
  ) {
    ArgumentNullException.ThrowIfNull(mipLevels);
    ownedMipLevels = ValidateMipLevels(width, height, mipLevels, samplingMode);
    Name = name;
    Width = width;
    Height = height;
    Recolorable = recolorable;
    SamplingMode = samplingMode;
    MipLevels = Array.AsReadOnly(ownedMipLevels);
    uploadMipLevels = SnapshotMipLevels(ownedMipLevels);
    CacheKey = TextureCacheKey.Create(
      name,
      width,
      height,
      recolorable,
      samplingMode,
      uploadMipLevels);
  }

  /// <summary>
  /// Clones the contiguous authored mip prefix from a decoded RCT3 texture. A decoded texture that
  /// genuinely contains only its base level uses explicit driver-generated mipmaps instead.
  /// </summary>
  public static Texture FromDecoded(
    string name,
    OpenCobra.OVL.Files.Texture decoded,
    Recolorable recolorable = 0,
    TextureSamplingMode samplingMode = TextureSamplingMode.AuthoredMipmaps
  ) {
    ArgumentNullException.ThrowIfNull(decoded);
    if (decoded.MipLevels.Length == 0 || decoded.MipLevels[0] == null)
      throw new InvalidDataException($"Texture '{name}' has no decoded base mip.");

    var firstMissingLevel = Array.FindIndex(decoded.MipLevels, mip => mip == null);
    if (firstMissingLevel >= 0
        && decoded.MipLevels.Skip(firstMissingLevel + 1).Any(mip => mip != null))
      throw new InvalidDataException(
        $"Texture '{name}' has a non-contiguous decoded mip chain after level " +
        $"{firstMissingLevel - 1}.");
    var sourceLevels = decoded.MipLevels
      .Take(firstMissingLevel < 0 ? decoded.MipLevels.Length : firstMissingLevel)
      .ToArray();
    var effectiveSampling = samplingMode == TextureSamplingMode.AuthoredMipmaps
      && sourceLevels.Length == 1
        ? TextureSamplingMode.GeneratedMipmaps
        : samplingMode;
    var cloneCount = effectiveSampling == TextureSamplingMode.AuthoredMipmaps
      ? sourceLevels.Length
      : 1;
    var clones = new List<Image<Rgba32>>(cloneCount);
    try {
      foreach (var source in sourceLevels.Take(cloneCount)) clones.Add(source.Clone());
      return new Texture(
        name,
        clones[0].Width,
        clones[0].Height,
        clones,
        recolorable,
        effectiveSampling);
    } catch {
      foreach (var clone in clones) clone.Dispose();
      throw;
    }
  }

  /// <summary>
  /// Whether this texture is recolorable.
  /// </summary>
  [Category("Appearance")]
  public bool IsRecolorable => Recolorable != Recolorable.None;

  [Category("GPU")]
  public State State {
    get {
      lock (lifetimeLock)
        return disposed ? State.Disposed : (handle == 0 ? State.Uninitialized : State.Ready);
    }
  }

  [Browsable(false)]
  public uint Handle {
    get {
      lock (lifetimeLock) return handle;
    }
  }

  public void Upload() {
    ObjectDisposedException.ThrowIf(State == State.Disposed, this);
    if (State == State.Ready) return;
    Upload(new SilkTextureGpuApi(IGame.IoC.Resolve<GL>()));
  }

  /// <summary>
  /// Uploads the immutable pixel snapshot through a renderer-provided GPU API.
  /// </summary>
  public void Upload(IGpuApi gpu) => EnsureUploadedCore(() => {
    ArgumentNullException.ThrowIfNull(gpu);
    var uploadedHandle = gpu.CreateTexture();
    if (uploadedHandle == 0)
      throw new InvalidOperationException("A texture upload must allocate a non-zero GPU handle.");
    try {
      // FIXME: SAFELY upload texture pixels to GPU!
      gpu.UploadTexture(uploadedHandle, uploadMipLevels, SamplingMode);
      return uploadedHandle;
    } catch (Exception uploadError) {
      try {
        gpu.DeleteTexture(uploadedHandle);
      } catch (Exception cleanupError) {
        throw new AggregateException(uploadError, cleanupError);
      }
      throw;
    }
  });

  /// <summary>
  /// Attaches the cached GPU handle returned by <paramref name="upload"/> exactly once.
  /// Render backends can use this seam to share one upload across equivalent texture instances.
  /// </summary>
  public void EnsureUploaded(Func<uint> upload) {
    ArgumentNullException.ThrowIfNull(upload);
    EnsureUploadedCore(upload);
  }

  /// <summary>
  /// Attaches a handle created from the immutable pixel snapshot exactly once.
  /// </summary>
  public void EnsureUploaded(TextureUpload upload) {
    ArgumentNullException.ThrowIfNull(upload);
    EnsureUploadedCore(() => upload(uploadMipLevels[0].Pixels.Span));
  }

  private void EnsureUploadedCore(Func<uint> upload) {
    lock (lifetimeLock) {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (handle != 0) return;

      var uploadedHandle = upload();
      if (uploadedHandle == 0)
        throw new InvalidOperationException("A texture upload must return a non-zero GPU handle.");
      handle = uploadedHandle;
    }
  }

  /// <summary>
  /// Detaches this texture from a renderer-owned GPU handle without disposing its pixels.
  /// </summary>
  public void ResetUpload() {
    lock (lifetimeLock) handle = 0;
  }

  public void Dispose() {
    lock (lifetimeLock) {
      if (ownerReleased) return;
      ownerReleased = true;
      if (leaseCount > 0) return;
      CompleteDispose();
    }
  }

  /// <summary>
  /// Acquires a non-owning lease that keeps this texture alive until the lease is disposed.
  /// The creator remains the owner and releases ownership through <see cref="Dispose"/>.
  /// </summary>
  public IDisposable AcquireLease() {
    lock (lifetimeLock) {
      ObjectDisposedException.ThrowIf(ownerReleased || disposed, this);
      leaseCount++;
      return new TextureLease(this);
    }
  }

  private void ReleaseLease() {
    lock (lifetimeLock) {
      if (leaseCount <= 0)
        throw new InvalidOperationException("Texture ownership was released more than once.");
      leaseCount--;
      if (leaseCount == 0 && ownerReleased) CompleteDispose();
    }
  }

  private void CompleteDispose() {
    if (disposed) return;
    GC.SuppressFinalize(this);
    foreach (var mipLevel in ownedMipLevels) mipLevel.Dispose();
    disposed = true;
  }

  public delegate uint TextureUpload(ReadOnlySpan<Rgba32> pixels);

  public interface IGpuApi {
    uint CreateTexture();
    void UploadTexture(
      uint handle,
      IReadOnlyList<TextureMipUpload> mipLevels,
      TextureSamplingMode samplingMode);
    void DeleteTexture(uint handle);
  }

  private sealed class TextureLease(Texture texture) : IDisposable {
    private Texture? resource = texture;

    public void Dispose() {
      var owned = Interlocked.Exchange(ref resource, null);
      owned?.ReleaseLease();
    }
  }

  private static Image<Rgba32>[] ValidateMipLevels(
    int width,
    int height,
    IReadOnlyList<Image<Rgba32>> mipLevels,
    TextureSamplingMode samplingMode
  ) {
    if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
    if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
    if (!Enum.IsDefined(samplingMode))
      throw new ArgumentOutOfRangeException(nameof(samplingMode));
    if (mipLevels.Count == 0)
      throw new ArgumentException("A texture requires at least one mip level.", nameof(mipLevels));
    if (samplingMode == TextureSamplingMode.AuthoredMipmaps && mipLevels.Count == 1)
      throw new ArgumentException(
        "Authored mipmap sampling requires more than the base level.", nameof(mipLevels));
    if (samplingMode != TextureSamplingMode.AuthoredMipmaps && mipLevels.Count != 1)
      throw new ArgumentException(
        $"{samplingMode} sampling accepts only a base mip level.", nameof(mipLevels));

    var result = new Image<Rgba32>[mipLevels.Count];
    var seen = new HashSet<Image<Rgba32>>(ReferenceEqualityComparer.Instance);
    var expectedWidth = width;
    var expectedHeight = height;
    foreach (var (mipLevel, level) in mipLevels.Select((value, index) => (value, index))) {
      if (mipLevel == null)
        throw new ArgumentException($"Mip level {level} is missing.", nameof(mipLevels));
      if (!seen.Add(mipLevel))
        throw new ArgumentException("Mip levels must own distinct images.", nameof(mipLevels));
      if (mipLevel.Width != expectedWidth || mipLevel.Height != expectedHeight)
        throw new ArgumentException(
          $"Mip level {level} is {mipLevel.Width}x{mipLevel.Height}; expected " +
          $"{expectedWidth}x{expectedHeight}.",
          nameof(mipLevels));
      result[level] = mipLevel;
      expectedWidth = Math.Max(1, expectedWidth / 2);
      expectedHeight = Math.Max(1, expectedHeight / 2);
    }
    return result;
  }

  private static TextureMipUpload[] SnapshotMipLevels(
    IReadOnlyList<Image<Rgba32>> mipLevels
  ) => [.. mipLevels.Select((mipLevel, level) => {
    var pixels = new Rgba32[checked(mipLevel.Width * mipLevel.Height)];
    mipLevel.CopyPixelDataTo(pixels);
    return new TextureMipUpload(level, mipLevel.Width, mipLevel.Height, pixels);
  })];

  private sealed class SilkTextureGpuApi(GL gl) : IGpuApi {
    public uint CreateTexture() => gl.GenTexture();

    public void UploadTexture(
      uint handle,
      IReadOnlyList<TextureMipUpload> mipLevels,
      TextureSamplingMode samplingMode
    ) => TextureUploadExecutor.Execute(
      new SilkTextureUploadCommands(gl), handle, mipLevels, samplingMode);

    public void DeleteTexture(uint handle) => gl.DeleteTexture(handle);
  }
}

public readonly record struct TextureMipUpload(
  int Level,
  int Width,
  int Height,
  ReadOnlyMemory<Rgba32> Pixels
);

internal interface ITextureUploadCommands {
  void BindTexture(uint handle);
  void UploadLevel(TextureMipUpload mipLevel);
  void GenerateMipmaps();
  void SetParameter(TextureParameterName name, int value);
}

internal static class TextureUploadExecutor {
  internal static void Execute(
    ITextureUploadCommands commands,
    uint handle,
    IReadOnlyList<TextureMipUpload> mipLevels,
    TextureSamplingMode samplingMode
  ) {
    ArgumentNullException.ThrowIfNull(commands);
    ArgumentNullException.ThrowIfNull(mipLevels);
    if (handle == 0) throw new ArgumentOutOfRangeException(nameof(handle));
    if (mipLevels.Count == 0)
      throw new ArgumentException("A texture upload requires at least one mip level.", nameof(mipLevels));
    if (!Enum.IsDefined(samplingMode))
      throw new ArgumentOutOfRangeException(nameof(samplingMode));
    if (samplingMode == TextureSamplingMode.AuthoredMipmaps && mipLevels.Count == 1)
      throw new ArgumentException(
        "Authored mipmap sampling requires more than the base level.", nameof(mipLevels));
    if (samplingMode != TextureSamplingMode.AuthoredMipmaps && mipLevels.Count != 1)
      throw new ArgumentException(
        $"{samplingMode} sampling accepts only a base mip level.", nameof(mipLevels));

    commands.BindTexture(handle);
    try {
      foreach (var mipLevel in mipLevels) commands.UploadLevel(mipLevel);

      switch (samplingMode) {
        case TextureSamplingMode.AuthoredMipmaps:
          commands.SetParameter(TextureParameterName.TextureMaxLevel, mipLevels.Count - 1);
          commands.SetParameter(
            TextureParameterName.TextureMinFilter,
            Convert.ToInt32(TextureMinFilter.LinearMipmapLinear));
          break;
        case TextureSamplingMode.GeneratedMipmaps:
          commands.GenerateMipmaps();
          commands.SetParameter(
            TextureParameterName.TextureMinFilter,
            Convert.ToInt32(TextureMinFilter.LinearMipmapLinear));
          break;
        case TextureSamplingMode.Linear:
          commands.SetParameter(
            TextureParameterName.TextureMinFilter,
            Convert.ToInt32(TextureMinFilter.Linear));
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(samplingMode));
      }

      commands.SetParameter(
        TextureParameterName.TextureMagFilter,
        Convert.ToInt32(TextureMagFilter.Linear));
      commands.SetParameter(
        TextureParameterName.TextureWrapS,
        Convert.ToInt32(TextureWrapMode.Repeat));
      commands.SetParameter(
        TextureParameterName.TextureWrapT,
        Convert.ToInt32(TextureWrapMode.Repeat));
    } finally {
      commands.BindTexture(0);
    }
  }
}

internal sealed class SilkTextureUploadCommands(GL gl) : ITextureUploadCommands {
  public void BindTexture(uint handle) => gl.BindTexture(TextureTarget.Texture2D, handle);

  public void UploadLevel(TextureMipUpload mipLevel) => gl.TexImage2D(
    TextureTarget.Texture2D,
    mipLevel.Level,
    InternalFormat.Rgba,
    Convert.ToUInt32(mipLevel.Width),
    Convert.ToUInt32(mipLevel.Height),
    0,
    PixelFormat.Rgba,
    PixelType.UnsignedByte,
    mipLevel.Pixels.Span);

  public void GenerateMipmaps() => gl.GenerateMipmap(TextureTarget.Texture2D);

  public void SetParameter(TextureParameterName name, int value) =>
    gl.TexParameter(TextureTarget.Texture2D, name, value);
}

public readonly record struct TextureCacheKey(
  string Name,
  int Width,
  int Height,
  Recolorable Recolorable,
  TextureSamplingMode SamplingMode,
  int MipCount,
  string PixelHash
) {
  internal static TextureCacheKey Create(
    string name,
    int width,
    int height,
    Recolorable recolorable,
    TextureSamplingMode samplingMode,
    IReadOnlyList<TextureMipUpload> mipLevels
  ) {
    using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    Span<byte> metadata = stackalloc byte[16];
    foreach (var mipLevel in mipLevels) {
      BinaryPrimitives.WriteInt32LittleEndian(metadata, mipLevel.Level);
      BinaryPrimitives.WriteInt32LittleEndian(metadata[4..], mipLevel.Width);
      BinaryPrimitives.WriteInt32LittleEndian(metadata[8..], mipLevel.Height);
      BinaryPrimitives.WriteInt32LittleEndian(metadata[12..], mipLevel.Pixels.Length);
      hasher.AppendData(metadata);
      hasher.AppendData(MemoryMarshal.AsBytes(mipLevel.Pixels.Span));
    }
    var hash = Convert.ToHexString(hasher.GetHashAndReset());
    return new(name, width, height, recolorable, samplingMode, mipLevels.Count, hash);
  }
}

public class AnimatedTexture(string name, FlexiTextureList textures) : IEnumerable<Texture> {
  private readonly Texture[] _textures = [.. textures.Frames.Select(
    frame => new Texture(
      name,
      frame.Texture.Width,
      frame.Texture.Height,
      frame.Texture,
      frame.Recolorable,
      TextureSamplingMode.GeneratedMipmaps)
  )];

  [Category("Design")]
  public string Name { get; private set; } = name;
  [Category("Appearance")]
  public uint Fps { get; } = textures.Fps;
  [Browsable(false)]
  public Texture[] Frames => _textures;
  [Browsable(false)]
  public Texture this[int index] => _textures[index];

  public IEnumerator<Texture> GetEnumerator() => _textures.AsEnumerable().GetEnumerator();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
