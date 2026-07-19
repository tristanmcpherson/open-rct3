// OpenGL Context
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using DryIoc;
using OpenCobra.GDK;
using OpenCobra.GDK.Platform;
using OpenRCT3.Platforms;
using OpenRCT3.Platforms.Windows;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;
using Silk.NET.WGL;
using Silk.NET.WGL.Extensions.ARB;
using Silk.NET.WGL.Extensions.EXT;
using System.ComponentModel;
using System.Runtime.InteropServices;
using static OpenRCT3.Platforms.Windows.Win32;

namespace OpenRCT3.OpenGL;

public partial class GLContext : IGLContext, INativeContext, IDisposable {
  public const string CreateContextError =
    "Could not create an OpenGL context. Please upgrade your graphics drivers.";

  private const string OPENGL32 = "opengl32.dll";
  private readonly WindowsGlContextLifetime lifetime = new(LoadLibrary(OPENGL32));
  private readonly WGL wgl;

  /// <summary>
  /// Raised when this context is recreated.
  /// </summary>
  public event EventHandler? Recreated;

  public static int PreferredColorDepth => 32;
  public static int PreferredDepthBufferBits => 24;
  public static int PreferredStencilBufferBits => 8;
  internal bool IsValid => lifetime.ContextHandle != nint.Zero;

  internal nint Hdc {
    get;
    set {
      var hdc = value;
      field = value;

      // Recreate the context when the HDC changes
      var didRecreate = false;
      var previousContext = lifetime.TakeContext();
      if (previousContext != nint.Zero) {
        var deletedPreviousContext = wgl.DeleteContext(previousContext);
        Debug.Assert(deletedPreviousContext);
        didRecreate = true;
      }
      if (hdc == nint.Zero) return;

      // Try to create an appropriate pixel format
      var pfd = new PIXELFORMATDESCRIPTOR {
        nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
        nVersion = 1,
        dwFlags = 0x00000004 | 0x00000020, // PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL
        iPixelType = 0, // PFD_TYPE_RGBA
        cColorBits = Convert.ToByte(PreferredColorDepth),
        cDepthBits = Convert.ToByte(PreferredDepthBufferBits),
        cStencilBits = Convert.ToByte(PreferredStencilBufferBits),
        iLayerType = 0 // PFD_MAIN_PLANE
      };
      var pix = ChoosePixelFormat(hdc, ref pfd);
      if (pix == nint.Zero) throw new Exception("Could not choose an appropriate pixel format for OpenGL.");
      if (!SetPixelFormat(hdc, pix, ref pfd)) throw new Exception("Could not set the surface's pixel format.");
      if (hdc == nint.Zero) throw new InvalidOperationException("Surface HDC context is invalid!");
      // Create a staging OpenGL context
      var tempContext = wgl.CreateContext(hdc);
      if (tempContext == nint.Zero) throw new Exception(CreateContextError);
      lifetime.SetContext(tempContext);
      wgl.MakeCurrent(hdc, tempContext);

      // Create a customized OpenGL context
      if (wgl.TryGetExtension<ArbCreateContext>(out var ext) == false)
        throw new PlatformNotSupportedException("OpenGL wglCreateContextAttribsARB extension is unavailable.");
      var arbCreateContext = ext ?? throw new Exception(CreateContextError);
      var context = arbCreateContext.CreateContextAttrib(hdc, nint.Zero, [
        (int)ContextAttribute.MajorVersion, Settings.Version.Major,
        (int)ContextAttribute.MinorVersion, Settings.Version.Minor,
        (int)ContextAttribute.ProfileMask, (int)Settings.Profile,
        (int)ContextAttribute.Flags,
        (int)(
          Settings.Flags |
  #if DEBUG
          // Request a debugging context
          ContextFlagMask.DebugBit
  #endif
        ),
        0 // NULL terminator
      ]);
      // Cleanup temporary context
      var releasedTemporaryContext = wgl.MakeCurrent(hdc, nint.Zero);
      Debug.Assert(releasedTemporaryContext);
      var temporaryContext = lifetime.TakeContext();
      var deletedTemporaryContext = wgl.DeleteContext(temporaryContext);
      Debug.Assert(deletedTemporaryContext);

      if (context == nint.Zero) context = wgl.CreateContext(hdc);
      if (context == nint.Zero) throw new Exception(CreateContextError);
      lifetime.SetContext(context);

      // Make the new context current
      var madeCurrent = wgl.MakeCurrent(hdc, context);
      Debug.Assert(madeCurrent);

      if (didRecreate) Recreated?.Invoke(this, EventArgs.Empty);
    }
  }

  public SurfaceSettings Settings { get; init; }

  [Browsable(false)]
  public nint Handle => lifetime.ContextHandle;

  [Browsable(false)]
  public IGLContextSource? Source => null;

  [Category("GPU")]
  [Description("Determines whether this context is the current context.")]
  public bool IsCurrent => lifetime.IsCurrent(wgl.GetCurrentContext);

  public GLContext(SurfaceSettings settings) {
    Settings = settings;
    wgl = new WGL(this);
  }

  public void Dispose() {
    GC.SuppressFinalize(this);
    var handles = lifetime.Release();
    try {
      ReleaseContext(handles.Context);
    } finally {
      if (handles.Library != nint.Zero) FreeLibrary(handles.Library);
    }
  }

  internal void ReleaseHandle() => ReleaseContext(lifetime.TakeContext());

  public void SwapInterval(int interval) {
    if (!wgl.TryGetExtension<ExtSwapControl>(out var ext) || ext == null) {
      // WGL_EXT_swap_control is unavailable; VSync cannot be controlled on this system.
      return;
    }

    ext.SwapInterval(interval);
  }

  public void MakeCurrent() {
    var context = lifetime.ContextHandle;
    if (Hdc == nint.Zero || context == nint.Zero)
      throw new Exception("Could not make the GL context current.");
    if (!wgl.MakeCurrent(Hdc, context))
      throw new Exception("Could not make the GL context current.");
  }

  public void SwapBuffers() {
    if (Hdc == nint.Zero) throw new Exception("Could not swap graphics buffers.");
    Win32.SwapBuffers(Hdc);
  }

  public void Clear() {
    var gl = Game.IoC.Resolve<GL>();
    gl?.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
  }

  public nint GetProcAddress(string procName) => GetProcAddress(procName, null);

  public nint GetProcAddress(string proc, int? slot = null) {
    var addr = GetProcAddress(lifetime.LibraryHandle, proc);
    if (addr != nint.Zero) return addr;
    // Fallback to extern DLL import
    return WglGetProcAddress(proc);
  }

  public bool TryGetProcAddress(string proc, out nint addr, int? slot = null) {
    try {
      addr = GetProcAddress(proc, null);
      if (addr == nint.Zero) return false;
      return true;
    } catch {
      addr = nint.Zero;
      return false;
    }
  }

  public override string ToString() => base.ToString() ?? nameof(GLContext);

  private void ReleaseContext(nint handle) {
    if (handle == nint.Zero) return;
    if (wgl.GetCurrentContext() == handle) wgl.MakeCurrent(Hdc, nint.Zero);
    wgl.DeleteContext(handle);
  }

  [LibraryImport(OPENGL32, EntryPoint = "wglGetProcAddress", StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
  private static partial nint WglGetProcAddress(string proc);

  [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
  private static partial nint GetProcAddress(nint lib, string proc);

  [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
  private static partial nint LoadLibrary(string lib);

  [LibraryImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool FreeLibrary(nint lib);
}
