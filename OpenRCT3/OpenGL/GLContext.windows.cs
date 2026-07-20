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
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using static OpenRCT3.Platforms.Windows.Win32;

namespace OpenRCT3.OpenGL;

public partial class GLContext : IGLContext, INativeContext, IDisposable {
  public const string CreateContextError =
    "Could not create an OpenGL context. Please upgrade your graphics drivers.";

  private const string OPENGL32 = "opengl32.dll";
  internal const uint PfdDoubleBuffer = 0x00000001;
  internal const uint PfdDrawToWindow = 0x00000004;
  internal const uint PfdSupportOpenGl = 0x00000020;
  internal const uint RequiredPixelFormatFlags =
    PfdDoubleBuffer | PfdDrawToWindow | PfdSupportOpenGl;
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

      // Recreate the context when the HDC changes
      var didRecreate = lifetime.ContextHandle != nint.Zero;
      if (didRecreate) ReleaseHandle();
      field = value;
      if (hdc == nint.Zero) return;

      // Try to create an appropriate pixel format
      var pfd = CreatePixelFormatDescriptor();
      var pix = ChoosePixelFormat(hdc, ref pfd);
      if (pix == nint.Zero) throw new Exception("Could not choose an appropriate pixel format for OpenGL.");
      var selectedPfd = new PIXELFORMATDESCRIPTOR();
      var described = DescribePixelFormat(
        hdc,
        Convert.ToInt32(pix.ToInt64()),
        Convert.ToUInt32(Marshal.SizeOf<PIXELFORMATDESCRIPTOR>()),
        ref selectedPfd);
      ValidateSelectedPixelFormat(described, selectedPfd.dwFlags);
      if (!SetPixelFormat(hdc, pix, ref pfd)) throw new Exception("Could not set the surface's pixel format.");
      if (hdc == nint.Zero) throw new InvalidOperationException("Surface HDC context is invalid!");
      // Create a staging OpenGL context
      var tempContext = wgl.CreateContext(hdc);
      if (tempContext == nint.Zero) throw new Exception(CreateContextError);
      lifetime.SetContext(tempContext);
      if (!wgl.MakeCurrent(hdc, tempContext))
        throw new Exception("Could not make the staging GL context current.");

      // Create a customized OpenGL context
      if (wgl.TryGetExtension<ArbCreateContext>(out var ext) == false)
        throw new PlatformNotSupportedException("OpenGL wglCreateContextAttribsARB extension is unavailable.");
      var arbCreateContext = ext ?? throw new Exception(CreateContextError);
#if DEBUG
      // Request a debugging context
      var contextFlags = Settings.Flags | ContextFlagMask.DebugBit;
#else
      var contextFlags = Settings.Flags;
#endif
      var context = arbCreateContext.CreateContextAttrib(hdc, nint.Zero, [
        (int)ContextAttribute.MajorVersion, Settings.Version.Major,
        (int)ContextAttribute.MinorVersion, Settings.Version.Minor,
        (int)ContextAttribute.ProfileMask, (int)Settings.Profile,
        (int)ContextAttribute.Flags, (int)contextFlags,
        0 // NULL terminator
      ]);
      // Cleanup temporary context
      try {
        ReleaseHandle();
      } catch {
        lifetime.RetainContext(context);
        throw;
      }

      if (context == nint.Zero) context = wgl.CreateContext(hdc);
      if (context == nint.Zero) throw new Exception(CreateContextError);
      lifetime.SetContext(context);

      // Make the new context current
      MakeCurrent();

      if (didRecreate) Recreated?.Invoke(this, EventArgs.Empty);
    }
  }

  internal static PIXELFORMATDESCRIPTOR CreatePixelFormatDescriptor() =>
    new() {
      nSize = Convert.ToUInt16(Marshal.SizeOf<PIXELFORMATDESCRIPTOR>()),
      nVersion = 1,
      // PFD_DOUBLEBUFFER | PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL
      dwFlags = RequiredPixelFormatFlags,
      iPixelType = 0, // PFD_TYPE_RGBA
      cColorBits = Convert.ToByte(PreferredColorDepth),
      cDepthBits = Convert.ToByte(PreferredDepthBufferBits),
      cStencilBits = Convert.ToByte(PreferredStencilBufferBits),
      iLayerType = 0 // PFD_MAIN_PLANE
    };

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
    var errors = new List<Exception>();
    try {
      ReleaseHandle();
    } catch (Exception error) {
      errors.Add(error);
    }
    if (lifetime.ContextHandle == nint.Zero) {
      try {
        if (!lifetime.TryReleaseLibrary(FreeLibrary))
          throw new InvalidOperationException("Could not release the OpenGL library.");
      } catch (Exception error) {
        errors.Add(error);
      }
    }
    if (errors.Count > 0) throw new AggregateException(errors);
  }

  internal void ReleaseHandle() {
    if (!lifetime.TryReleaseContext(ReleaseContext))
      throw new InvalidOperationException("Could not release the OpenGL context.");
  }

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
    EnsureBufferSwapSucceeded(Win32.SwapBuffers(Hdc));
  }

  internal static void ValidateSelectedPixelFormat(int described, uint flags) {
    if (described == 0)
      throw new Exception("Could not describe the selected OpenGL pixel format.");
    var missingFlags = RequiredPixelFormatFlags & ~flags;
    if (missingFlags != 0)
      throw new PlatformNotSupportedException(
        $"The selected OpenGL pixel format is missing required presentation flags: 0x{missingFlags:X8}.");
  }

  internal static void EnsureBufferSwapSucceeded(bool succeeded) {
    if (!succeeded) throw new Exception("Could not swap graphics buffers.");
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

  private bool ReleaseContext(nint handle) {
    if (wgl.GetCurrentContext() != handle) {
      if (Hdc == nint.Zero || !wgl.MakeCurrent(Hdc, handle)) return false;
    }
    if (!wgl.MakeCurrent(nint.Zero, nint.Zero)) return false;
    return wgl.DeleteContext(handle);
  }

  [LibraryImport(OPENGL32, EntryPoint = "wglGetProcAddress", StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
  private static partial nint WglGetProcAddress(string proc);

  [LibraryImport("gdi32.dll", SetLastError = true)]
  private static partial int DescribePixelFormat(
    nint hdc,
    int pixelFormat,
    uint bytes,
    ref PIXELFORMATDESCRIPTOR descriptor);

  [LibraryImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true, StringMarshalling = StringMarshalling.Custom, StringMarshallingCustomType = typeof(System.Runtime.InteropServices.Marshalling.AnsiStringMarshaller))]
  private static partial nint GetProcAddress(nint lib, string proc);

  [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
  private static partial nint LoadLibrary(string lib);

  [LibraryImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool FreeLibrary(nint lib);
}
