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

public class Texture(string name, int width, int height, Image<Rgba32> texture, Recolorable recolorable = 0) : IResource, IDisposable {
  public static readonly string UniformName = "u_Texture";
  private readonly object lifetimeLock = new();
  private bool disposed;
  private bool disposalRequested;
  private int retainCount;
  private uint handle;

  [Category("Design")]
  public string Name { get; private set; } = name;
  [Category("Appearance")]
  public int Width { get; } = width;
  [Category("Appearance")]
  public int Height { get; } = height;
  [Category("Appearance")]
  public Recolorable Recolorable { get; } = recolorable;
  [Category("Appearance")]
  public Image<Rgba32> Pixels { get; } = texture;

  [Browsable(false)]
  public TextureCacheKey CacheKey { get; } = TextureCacheKey.Create(
    name,
    width,
    height,
    recolorable,
    texture);

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

  public void Upload() => EnsureUploaded(() => {
    var gl = IGame.IoC.Resolve<GL>();
    var uploadedHandle = gl.GenTexture();
    try {
      gl.BindTexture(TextureTarget.Texture2D, uploadedHandle);

      // FIXME: SAFELY upload texture pixels to GPU!
      var success = Pixels.DangerousTryGetSinglePixelMemory(out var pixelMemory);
      Debug.Assert(success, "Failed to get pixel memory from albedo texture");
      ReadOnlySpan<Rgba32> pixels = pixelMemory.Span;
      gl.TexImage2D(
        TextureTarget.Texture2D,
        0,
        InternalFormat.Rgba,
        Convert.ToUInt32(Width),
        Convert.ToUInt32(Height),
        0,
        PixelFormat.Rgba,
        PixelType.UnsignedByte,
        pixels
      );

      gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
      gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
      gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
      gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
      return uploadedHandle;
    } catch {
      gl.DeleteTexture(uploadedHandle);
      throw;
    } finally {
      gl.BindTexture(TextureTarget.Texture2D, 0);
    }
  });

  /// <summary>
  /// Attaches the cached GPU handle returned by <paramref name="upload"/> exactly once.
  /// Render backends can use this seam to share one upload across equivalent texture instances.
  /// </summary>
  public void EnsureUploaded(Func<uint> upload) {
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
      if (disposalRequested) return;
      disposalRequested = true;
      if (retainCount > 0) return;
      CompleteDispose();
    }
  }

  internal void Retain() {
    lock (lifetimeLock) {
      ObjectDisposedException.ThrowIf(disposalRequested || disposed, this);
      retainCount++;
    }
  }

  internal void Release() {
    lock (lifetimeLock) {
      if (retainCount <= 0)
        throw new InvalidOperationException("Texture ownership was released more than once.");
      retainCount--;
      if (retainCount == 0 && disposalRequested) CompleteDispose();
    }
  }

  private void CompleteDispose() {
    if (disposed) return;
    GC.SuppressFinalize(this);
    Pixels.Dispose();
    disposed = true;
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
    Image<Rgba32> image
  ) {
    var pixels = new Rgba32[image.Width * image.Height];
    image.CopyPixelDataTo(pixels);
    var bytes = MemoryMarshal.AsBytes(pixels.AsSpan());
    var hash = Convert.ToHexString(SHA256.HashData(bytes));
    return new(name, width, height, recolorable, hash);
  }
}

public class AnimatedTexture(string name, FlexiTextureList textures) : IEnumerable<Texture> {
  private readonly Texture[] _textures = [.. textures.Frames.Select(
    frame => new Texture(name, textures.Width, textures.Height, frame.Texture, frame.Recolorable)
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
