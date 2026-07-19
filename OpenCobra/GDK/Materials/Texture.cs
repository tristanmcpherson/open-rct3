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
using System.Collections;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace OpenCobra.GDK.Materials;

public class Texture : IResource, IDisposable {
  public static readonly string UniformName = "u_Texture";
  private readonly object lifetimeLock = new();
  private readonly Rgba32[] uploadPixels;
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
  public Image<Rgba32> Pixels { get; }

  [Browsable(false)]
  public TextureCacheKey CacheKey { get; }

  public Texture(
    string name,
    int width,
    int height,
    Image<Rgba32> texture,
    Recolorable recolorable = 0
  ) {
    Name = name;
    Width = width;
    Height = height;
    Recolorable = recolorable;
    Pixels = texture;
    uploadPixels = new Rgba32[texture.Width * texture.Height];
    texture.CopyPixelDataTo(uploadPixels);
    CacheKey = TextureCacheKey.Create(name, width, height, recolorable, uploadPixels);
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
  public void Upload(IGpuApi gpu) => EnsureUploaded(pixels => {
    var uploadedHandle = gpu.CreateTexture();
    if (uploadedHandle == 0)
      throw new InvalidOperationException("A texture upload must allocate a non-zero GPU handle.");
    try {
      // FIXME: SAFELY upload texture pixels to GPU!
      gpu.UploadTexture(uploadedHandle, Width, Height, pixels);
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
    EnsureUploaded(_ => upload());
  }

  /// <summary>
  /// Attaches a handle created from the immutable pixel snapshot exactly once.
  /// </summary>
  public void EnsureUploaded(TextureUpload upload) {
    lock (lifetimeLock) {
      ObjectDisposedException.ThrowIf(disposed, this);
      if (handle != 0) return;

      var uploadedHandle = upload(uploadPixels);
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
    Pixels.Dispose();
    disposed = true;
  }

  public delegate uint TextureUpload(ReadOnlySpan<Rgba32> pixels);

  public interface IGpuApi {
    uint CreateTexture();
    void UploadTexture(uint handle, int width, int height, ReadOnlySpan<Rgba32> pixels);
    void DeleteTexture(uint handle);
  }

  private sealed class TextureLease(Texture texture) : IDisposable {
    private Texture? resource = texture;

    public void Dispose() {
      var owned = Interlocked.Exchange(ref resource, null);
      owned?.ReleaseLease();
    }
  }

  private sealed class SilkTextureGpuApi(GL gl) : IGpuApi {
    public uint CreateTexture() => gl.GenTexture();

    public void UploadTexture(
      uint handle,
      int width,
      int height,
      ReadOnlySpan<Rgba32> pixels
    ) {
      try {
        gl.BindTexture(TextureTarget.Texture2D, handle);
        gl.TexImage2D(
          TextureTarget.Texture2D,
          0,
          InternalFormat.Rgba,
          Convert.ToUInt32(width),
          Convert.ToUInt32(height),
          0,
          PixelFormat.Rgba,
          PixelType.UnsignedByte,
          pixels
        );

        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
      } finally {
        gl.BindTexture(TextureTarget.Texture2D, 0);
      }
    }

    public void DeleteTexture(uint handle) => gl.DeleteTexture(handle);
  }
}

public readonly record struct TextureCacheKey(
  string Name,
  int Width,
  int Height,
  Recolorable Recolorable,
  string PixelHash
) {
  internal static TextureCacheKey Create(
    string name,
    int width,
    int height,
    Recolorable recolorable,
    ReadOnlySpan<Rgba32> pixels
  ) {
    var bytes = MemoryMarshal.AsBytes(pixels);
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    return new(name, width, height, recolorable, hash);
  }
}

public class AnimatedTexture(string name, FlexiTextureList textures) : IEnumerable<Texture> {
  private readonly Texture[] _textures = [.. textures.Frames.Select(
    frame => new Texture(
      name,
      frame.Texture.Width,
      frame.Texture.Height,
      frame.Texture,
      frame.Recolorable)
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
